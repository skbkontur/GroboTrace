using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using GrEmit;
using GrEmit.Utils;

namespace GroboTrace
{
    public class TracingWrapper
    {
        public TracingWrapper(TracingWrapperConfigurator configurator)
        {
            this.configurator = configurator;
        }

        public object WrapAndCreate(object instance)
        {
            Type wrapperType;
            return !TryWrap(instance.GetType(), out wrapperType) ? instance : Activator.CreateInstance(wrapperType, new[] {instance});
        }

        public bool TryWrap(Type implementationType, out Type wrapperType)
        {
            return TryWrapInternal(implementationType, out wrapperType, false);
        }

        public static ulong GetMethodHashKey(MethodInfo method)
        {
            var typeHandle = (ulong)method.ReflectedType.TypeHandle.Value.ToInt64();
            var methodHandle = (ulong)method.MethodHandle.Value.ToInt64();
            unchecked
            {
                return typeHandle * 0x9E3779B9B7E15163 + methodHandle;
            }
        }

        public static MethodInfo GetMethod(Type type, long moduleHandle, int methodToken)
        {
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return methods.Single(method => moduleRuntimeHandleGetter(method.Module.ModuleHandle) == moduleHandle && method.MetadataToken == methodToken);
        }

        public static Func<long> GetTicks { get { return getTicks; } }
        public static string DebugOutputDirectory;

        public const string WrappersAssemblyName = "b5cc8d5b-fd0e-4b90-b545-d5c09c3ea040";

        private bool TryWrapInternal(Type implementationType, out Type wrapperType, bool allowReturnCurrentlyBuilding)
        {
            if(configurator.Forbidden(implementationType))
            {
                wrapperType = null;
                return false;
            }
            if(!implementationType.IsGenericType)
            {
                wrapperType = GetWrapperType(implementationType, allowReturnCurrentlyBuilding);
                return true;
            }
            wrapperType = GetWrapperType(implementationType.GetGenericTypeDefinition(), allowReturnCurrentlyBuilding);
            wrapperType = wrapperType.MakeGenericType(implementationType.GetGenericArguments());
            return true;
        }

        private Type GetWrapperType(Type implementationType, bool allowReturnCurrentlyBuilding)
        {
            var wrapperType = (Type)wrapperTypes[implementationType];
            if(wrapperType == null)
            {
                lock(lockObject)
                {
                    wrapperType = (Type)wrapperTypes[implementationType];
                    var buildingWrapperType = (Type)buildingWrapperTypes[implementationType];
                    if(wrapperType == null && allowReturnCurrentlyBuilding && buildingWrapperType != null)
                        return buildingWrapperType;
                    wrapperTypes[implementationType] = wrapperType = WrapInternal(implementationType);
                }
            }
            return wrapperType;
        }

        private void SetConstraints(GenericTypeParameterBuilder parameter, Type[] constraints)
        {
            var baseTypeConstraint = constraints.SingleOrDefault(type => type.IsClass);
            if(baseTypeConstraint != null)
                parameter.SetBaseTypeConstraint(baseTypeConstraint);
            parameter.SetInterfaceConstraints(constraints.Where(type => !type.IsClass).ToArray());
        }

        private Type WrapInternal(Type implementationType)
        {
            var typeBuilder = module.DefineType(implementationType + "_Wrapper_" + Guid.NewGuid(), TypeAttributes.Public | TypeAttributes.Class);
            var symWriter = module.GetSymWriter();

            buildingWrapperTypes[implementationType] = typeBuilder;

            Type[] genericArguments = null;
            GenericTypeParameterBuilder[] genericParameters = null;
            if(implementationType.IsGenericTypeDefinition)
            {
                genericArguments = implementationType.GetGenericArguments();
                genericParameters = typeBuilder.DefineGenericParameters(genericArguments.Select(type => "Z" + type.Name).ToArray());
                for(var index = 0; index < genericArguments.Length; index++)
                {
                    var genericArgument = genericArguments[index];
                    var genericParameter = genericParameters[index];
                    genericParameter.SetGenericParameterAttributes(genericArgument.GenericParameterAttributes & ~(GenericParameterAttributes.Contravariant | GenericParameterAttributes.Covariant));
                    SetConstraints(genericParameter, genericArgument.GetGenericParameterConstraints());
                }
            }
            var implField = typeBuilder.DefineField("impl", typeof(object), FieldAttributes.Private | FieldAttributes.InitOnly);
            wrapperConstructors[implementationType] = BuildConstructor(typeBuilder, implField);
            var typeInitializer = typeBuilder.DefineTypeInitializer();
            var il = new GroboIL(typeInitializer);
            if(implementationType.IsInterface)
            {
                var interfaces = new[] {implementationType}.Concat(implementationType.GetInterfaces()).Where(IsPublic).ToArray();
                foreach(var interfaCe in interfaces)
                    typeBuilder.AddInterfaceImplementation(ReflectionExtensions.SubstituteGenericParameters(interfaCe, genericArguments, genericParameters));
                foreach(var interfaceType in interfaces)
                {
                    foreach(var method in interfaceType.GetMethods())
                    {
                        var methodBuilder = BuildMethod(typeBuilder, symWriter, method, method, genericArguments, genericParameters, implField, il);
                        typeBuilder.DefineMethodOverride(methodBuilder, method);
                    }
                }
            }
            else
            {
                var builtMethods = new HashSet<MethodInfo>();
                var interfaces = implementationType.GetInterfaces().Where(IsPublic).ToArray();
                foreach(var interfaCe in interfaces)
                    typeBuilder.AddInterfaceImplementation(ReflectionExtensions.SubstituteGenericParameters(interfaCe, genericArguments, genericParameters));
                foreach(var interfaceMap in interfaces.Select(implementationType.GetInterfaceMap))
                {
                    for(var index = 0; index < interfaceMap.InterfaceMethods.Length; ++index)
                    {
                        var targetMethod = interfaceMap.TargetMethods[index];
                        var interfaceMethod = interfaceMap.InterfaceMethods[index];
                        builtMethods.Add(targetMethod);
                        var methodBuilder = BuildMethod(typeBuilder, symWriter, targetMethod, interfaceMethod, genericArguments, genericParameters, implField, il);
                        typeBuilder.DefineMethodOverride(methodBuilder, interfaceMethod);
                    }
                }
                if(IsPublic(implementationType) && !implementationType.IsInterface)
                {
                    var methods = implementationType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    foreach(var method in methods.Where(method => !builtMethods.Contains(method)))
                        BuildMethod(typeBuilder, symWriter, method, method, genericArguments, genericParameters, implField, il);
                }
            }
            typeBuilder.DefineMethodOverride(BuildUnWrapMethod(typeBuilder, implField), classWrapperUnWrapMethod);
            typeBuilder.AddInterfaceImplementation(typeof(IClassWrapper));

            il.Ret();

            var result = typeBuilder.CreateType();
            wrapperConstructors[implementationType] = result.GetConstructor(new[] {typeof(object)});
            buildingWrapperTypes[implementationType] = null;
            return result;
        }

        private static bool IsPublic(Type type)
        {
            if(type.Assembly.GetCustomAttributes(true).OfType<InternalsVisibleToAttribute>().Any(attribute => attribute.AssemblyName == WrappersAssemblyName))
                return true;
            if(!type.IsNested)
                return type.IsPublic;
            return type.IsNestedPublic && IsPublic(type.DeclaringType);
        }

        private static MethodBuilder BuildUnWrapMethod(TypeBuilder typeBuilder, FieldInfo implField)
        {
            var method = typeBuilder.DefineMethod("UnWrap", MethodAttributes.Public | MethodAttributes.Virtual, CallingConventions.HasThis, typeof(object), Type.EmptyTypes);
            using(var il = new GroboIL(method))
            {
                il.Ldarg(0); // stack: [this]
                il.Ldfld(implField); // stack: [this.impl]
                il.Ret();
            }
            return method;
        }

        private MethodInfo SubstituteGenericParameters(MethodInfo method, Type[] genericArguments, GenericTypeParameterBuilder[] genericParameters)
        {
            var declaringType = method.DeclaringType;
            if(declaringType == null || !declaringType.IsGenericType)
                return method;
            var dict = new Dictionary<Type, Type>();
            for(var i = 0; i < genericArguments.Length; ++i)
                dict.Add(genericArguments[i], genericParameters[i]);
            var declaringTypeGenericTypeDefinition = declaringType.GetGenericTypeDefinition();
            var declaringTypeGenericParameters = declaringType.GetGenericArguments();
            var instantiatedGenericArguments = ReflectionExtensions.SubstituteGenericParameters(declaringTypeGenericParameters, genericArguments, genericParameters);
            declaringType = declaringTypeGenericTypeDefinition.MakeGenericType(instantiatedGenericArguments);
            if(!isTypeBuilderInstantiation(declaringType))
                return (MethodInfo)MethodBase.GetMethodFromHandle(method.MethodHandle, declaringType.TypeHandle);
            return TypeBuilder.GetMethod(declaringType, (MethodInfo)MethodBase.GetMethodFromHandle(method.MethodHandle, declaringTypeGenericTypeDefinition.TypeHandle));
        }

        private MethodBuilder BuildMethod(TypeBuilder typeBuilder, ISymbolWriter symWriter, MethodInfo implementationMethod, MethodInfo abstractionMethod, Type[] parentGenericArguments, GenericTypeParameterBuilder[] parentGenericParameters, FieldInfo implField, GroboIL typeInitializerIl)
        {
            var reflectedType = implementationMethod.ReflectedType;
            if(reflectedType.IsGenericTypeDefinition)
                reflectedType = reflectedType.MakeGenericType(parentGenericParameters);
            else if(reflectedType.IsGenericType)
                reflectedType = ReflectionExtensions.SubstituteGenericParameters(reflectedType, parentGenericArguments, parentGenericParameters);

            if(parentGenericArguments != null)
                abstractionMethod = SubstituteGenericParameters(abstractionMethod, parentGenericArguments, parentGenericParameters);

            MethodBuilder method;
            Type returnType;
            Type[] parameterTypes;
            var parameters = implementationMethod.GetParameters();
            if(!implementationMethod.IsGenericMethod)
            {
                returnType = ReflectionExtensions.SubstituteGenericParameters(implementationMethod.ReturnType, parentGenericArguments, parentGenericParameters);
                parameterTypes = ReflectionExtensions.SubstituteGenericParameters(parameters.Select(info => info.ParameterType).ToArray(), parentGenericArguments, parentGenericParameters);
                method = typeBuilder.DefineMethod(implementationMethod.Name, implementationMethod.IsVirtual ? MethodAttributes.Public | MethodAttributes.Virtual : MethodAttributes.Public, CallingConventions.HasThis, returnType, parameterTypes);
            }
            else
            {
                method = typeBuilder.DefineMethod(implementationMethod.Name, implementationMethod.IsVirtual ? MethodAttributes.Public | MethodAttributes.Virtual : MethodAttributes.Public, CallingConventions.HasThis);
                var genericArguments = implementationMethod.GetGenericArguments();
                var genericParameters = method.DefineGenericParameters(genericArguments.Select(type => type.Name + "_ForWrapper").ToArray());
                var allGenericArguments = parentGenericArguments == null ? genericArguments : parentGenericArguments.Concat(genericArguments).ToArray();
                Type[] allGenericParameters = parentGenericParameters == null ? genericParameters : parentGenericParameters.Concat(genericParameters).ToArray();
                var implementationMethodDeclaringType = implementationMethod.DeclaringType;
                if(implementationMethodDeclaringType != null && implementationMethodDeclaringType.IsGenericType)
                {
                    // The method we are wrapping may be inherited from some class or interface which is a generic type but some of the inheritors have instantiated some of the generic parameters
                    // We need to instantiate them as well
                    var implementationMethodGenericParameters = implementationMethodDeclaringType.GetGenericTypeDefinition().GetGenericArguments();
                    var implementationMethodGenericArguments = implementationMethodDeclaringType.GetGenericArguments();
                    var instantiatedParameters = implementationMethodGenericParameters.Select((type, i) => new {type, i}).Where(arg => arg.type != implementationMethodGenericArguments[arg.i]).Select(arg => arg.i).ToArray();
                    allGenericArguments = allGenericArguments.Concat(instantiatedParameters.Select(i => implementationMethodGenericParameters[i])).ToArray();
                    allGenericParameters = allGenericParameters.Concat(instantiatedParameters.Select(i => implementationMethodGenericArguments[i])).ToArray();
                }
                for(var index = 0; index < genericArguments.Length; index++)
                {
                    var genericArgument = genericArguments[index];
                    var genericParameter = genericParameters[index];
                    genericParameter.SetGenericParameterAttributes(genericArgument.GenericParameterAttributes);
                    SetConstraints(genericParameter, ReflectionExtensions.SubstituteGenericParameters(genericArgument.GetGenericParameterConstraints(), allGenericArguments, allGenericParameters));
                }
                returnType = ReflectionExtensions.SubstituteGenericParameters(implementationMethod.ReturnType, allGenericArguments, allGenericParameters);
                parameterTypes = ReflectionExtensions.SubstituteGenericParameters(parameters.Select(info => info.ParameterType).ToArray(), allGenericArguments, allGenericParameters);
                method.SetReturnType(returnType);
                method.SetParameters(parameterTypes);

                abstractionMethod = abstractionMethod.MakeGenericMethod(genericParameters);
            }
            for(var i = 0; i < parameters.Length; ++i)
                method.DefineParameter(i + 1, ParameterAttributes.In, parameters[i].Name);
            foreach(var customAttribute in implementationMethod.GetCustomAttributesData())
            {
                var constructorArgs = customAttribute.ConstructorArguments.Select(argument => argument.Value).ToArray();
                var properties = new List<PropertyInfo>();
                var propertyValues = new List<object>();
                var fields = new List<FieldInfo>();
                var fieldValues = new List<object>();
                if(customAttribute.NamedArguments != null)
                {
                    foreach(var namedArgument in customAttribute.NamedArguments)
                    {
                        var member = namedArgument.MemberInfo;
                        switch(member.MemberType)
                        {
                        case MemberTypes.Property:
                            properties.Add((PropertyInfo)member);
                            propertyValues.Add(namedArgument.TypedValue.Value);
                            break;
                        case MemberTypes.Field:
                            fields.Add((FieldInfo)member);
                            fieldValues.Add(namedArgument.TypedValue.Value);
                            break;
                        }
                    }
                }
                method.SetCustomAttribute(new CustomAttributeBuilder(customAttribute.Constructor, constructorArgs, properties.ToArray(), propertyValues.ToArray(), fields.ToArray(), fieldValues.ToArray()));
            }

            var methodField = typeBuilder.DefineField("method_" + implementationMethod.Name + "_" + Guid.NewGuid(), typeof(MethodInfo), FieldAttributes.Private | FieldAttributes.InitOnly | FieldAttributes.Static);
            var methodHandleField = typeBuilder.DefineField("methodHandle_" + implementationMethod.Name + "_" + Guid.NewGuid(), typeof(long), FieldAttributes.Private | FieldAttributes.InitOnly | FieldAttributes.Static);

/*
            if(!implementationMethod.IsGenericMethod || !reflectedType.IsGenericType)
            {
                typeInitializerIl.Ldtoken(implementationMethod);
                typeInitializerIl.Ldtoken(reflectedType);
                typeInitializerIl.Call(methodBaseGetMethodFromMethodHandleAndTypeHandleMethod);
            }
            else
            {
*/
            // todo ldtoken для generic-метода в generic-классе почему-то выбрасывает BadImageFormatException, может быть, баг в .NET
            typeInitializerIl.Ldtoken(reflectedType);
            typeInitializerIl.Call(typeFromTypeHandleMethod);
            typeInitializerIl.Ldc_I8(moduleRuntimeHandleGetter(implementationMethod.Module.ModuleHandle));
            typeInitializerIl.Ldc_I4(implementationMethod.MetadataToken);
            typeInitializerIl.Call(methodGetter);
//            }
            typeInitializerIl.Dup();
            typeInitializerIl.Stfld(methodField);
            typeInitializerIl.Call(methodHashKeyGetter);
            typeInitializerIl.Stfld(methodHandleField);

            var documentName = typeBuilder.Name + "." + method.Name + ".cil";
            using(var il = string.IsNullOrEmpty(DebugOutputDirectory)
                               ? new GroboIL(method)
                               : new GroboIL(method, symWriter.DefineDocument(documentName, SymDocumentType.Text, SymLanguageType.ILAssembly, Guid.Empty)))
            {
                var result = returnType == typeof(void) ? null : il.DeclareLocal(returnType);
                var startTicks = il.DeclareLocal(typeof(long));
                var endTicks = il.DeclareLocal(typeof(long));

                il.Ldfld(methodField); // stack: [method]
                il.Ldfld(methodHandleField); // stack: [method, methodHandle]
                il.Call(tracingAnalyzerMethodStartedMethod); // TracingAnalyzer.MethodStarted(method, methodHandle)
                il.Ldloca(startTicks); // stack: [ref startTicks]
                il.Ldc_IntPtr(ticksReaderAddress);
                il.Calli(CallingConventions.Standard, typeof(void), new[] {typeof(long).MakeByRefType()}); // GetTicks(ref startTicks)

                il.BeginExceptionBlock();

                il.Ldarg(0); // stack: [this]
                il.Ldfld(implField); // stack: [impl]
                il.Castclass(abstractionMethod.DeclaringType);
                for(var i = 0; i < parameterTypes.Length; ++i)
                    il.Ldarg(i + 1); // stack: [impl, parameters]
                il.Call(abstractionMethod); // impl.method(parameters)

                if(returnType.IsInterface)
                {
                    Type wrapperType;
                    if(TryWrapInternal(returnType, out wrapperType, true))
                    {
                        il.Dup();
                        il.Isinst(typeof(IClassWrapper));
                        var wrappedLabel = il.DefineLabel("wrapped");
                        il.Brtrue(wrappedLabel);
                        var wrapperConstructor = (ConstructorInfo)wrapperConstructors[returnType.IsGenericType ? returnType.GetGenericTypeDefinition() : returnType];
                        ConstructorInfo constructor;
                        if(wrapperType.ContainsGenericParameters)
                            constructor = TypeBuilder.GetConstructor(wrapperType, wrapperConstructor);
                        else if(returnType.IsGenericType)
                            constructor = (ConstructorInfo)MethodBase.GetMethodFromHandle(wrapperConstructor.MethodHandle, wrapperType.TypeHandle);
                        else constructor = wrapperConstructor;
                        il.Newobj(constructor);
                        il.MarkLabel(wrappedLabel);
                    }
                }

                if(result != null)
                    il.Stloc(result); // result = impl.method(parameters)

                var retLabel = il.DefineLabel("ret");
                il.Leave(retLabel);
                il.BeginFinallyBlock();

                il.Ldloca(endTicks); // stack: [ref endTicks]
                il.Ldc_IntPtr(ticksReaderAddress);
                il.Calli(CallingConventions.Standard, typeof(void), new[] {typeof(long).MakeByRefType()}); // GetTicks(ref endTicks)

                il.Ldfld(methodField); // stack: [method]
                il.Ldfld(methodHandleField); // stack: [method, methodHandle]

                il.Ldloc(endTicks); // stack: [method, methodHandle, endTicks]
                il.Ldloc(startTicks); // stack: [method, methodHandle, endTicks, startTicks]
                il.Sub(); // stack: [method, methodHandle, endTicks - startTicks = elapsed]

                il.Call(tracingAnalyzerMethodFinishedMethod); // TracingAnalyzer.MethodFinished(method, methodHandle, elapsed)

                il.EndExceptionBlock();
                il.MarkLabel(retLabel);

                if(result != null)
                    il.Ldloc(result);
                il.Ret();

                if(!string.IsNullOrEmpty(DebugOutputDirectory))
                    File.WriteAllText(Path.Combine(DebugOutputDirectory, documentName), il.GetILCode());
            }
            return method;
        }

        private static ConstructorInfo BuildConstructor(TypeBuilder typeBuilder, FieldInfo implField)
        {
            var constructor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new[] {implField.FieldType});
            using(var il = new GroboIL(constructor))
            {
                il.Ldarg(0); // stack: [this]
                il.Ldarg(1); // stack: [impl]
                il.Stfld(implField); // this.implField = impl
                il.Ret();
            }
            return constructor;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private static IntPtr GetTicksReaderAddress()
        {
            var resourceName = string.Format("rdtsc{0}.dll", IntPtr.Size == 4 ? "32" : "64");
            var rdtscDll = Assembly.GetExecutingAssembly().GetManifestResourceStream("GroboTrace.Rdtsc." + resourceName);
            if(rdtscDll != null)
            {
                var rdtscDllContent = new byte[rdtscDll.Length];
                if(rdtscDll.Read(rdtscDllContent, 0, rdtscDllContent.Length) == rdtscDllContent.Length)
                {
                    var directoryName = AppDomain.CurrentDomain.BaseDirectory; //Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);
                    if(string.IsNullOrEmpty(directoryName))
                        throw new InvalidOperationException("Unable to obtain binaries directory");
                    var rdtscDllFileName = Path.Combine(directoryName, string.Format("rdtsc{0}_{1}.dll", IntPtr.Size == 4 ? "32" : "64", Guid.NewGuid().ToString("N")));
                    File.WriteAllBytes(rdtscDllFileName, rdtscDllContent);
                    var rdtscDllModuleHandle = LoadLibrary(rdtscDllFileName);
                    if(rdtscDllModuleHandle != IntPtr.Zero)
                    {
                        var rdtscProcAddress = GetProcAddress(rdtscDllModuleHandle, "ReadTimeStampCounter");
                        if(rdtscProcAddress != IntPtr.Zero)
                            return rdtscProcAddress;
                    }
                }
            }
            var kernel32ModuleHandle = GetModuleHandle("kernel32.dll");
            return kernel32ModuleHandle == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(kernel32ModuleHandle, "QueryPerformanceCounter");
        }

        private static Func<long> EmitTicksGetter()
        {
            var dynamicMethod = new DynamicMethod("GetTicks_" + Guid.NewGuid(), typeof(long), null, typeof(TracingWrapper));
            using(var il = new GroboIL(dynamicMethod))
            {
                var ticks = il.DeclareLocal(typeof(long));
                il.Ldloca(ticks); // stack: [&ticks]
                il.Ldc_IntPtr(ticksReaderAddress);
                il.Calli(CallingConvention.StdCall, typeof(void), new[] {typeof(long).MakeByRefType()});
                il.Ldloc(ticks);
                il.Ret();
            }
            return (Func<long>)dynamicMethod.CreateDelegate(typeof(Func<long>));
        }

        private static Func<ModuleHandle, long> EmitModuleRuntimeHandleGetter()
        {
            var runtimeModuleType = typeof(ModuleHandle).Assembly.GetTypes().Single(type => type.Name == "RuntimeModule");
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(long), new[] {typeof(ModuleHandle)}, typeof(TracingWrapper), true);
            using(var il = new GroboIL(method))
            {
                il.Ldarga(0);
                il.Ldfld(typeof(ModuleHandle).GetField("m_ptr", BindingFlags.Instance | BindingFlags.NonPublic));
                il.Ldfld(runtimeModuleType.GetField("m_pData", BindingFlags.Instance | BindingFlags.NonPublic));
                var local = il.DeclareLocal(typeof(IntPtr));
                il.Stloc(local);
                il.Ldloca(local);
                il.Call(intPtrToInt64Method, typeof(IntPtr));
                il.Ret();
            }
            return (Func<ModuleHandle, long>)method.CreateDelegate(typeof(Func<ModuleHandle, long>));
        }

        private static Func<Type, bool> BuildIsTypeBuilderInstantiation()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(bool), new[] {typeof(Type)}, typeof(TracingWrapper), true);
            var typeBuilderInstantiationType = typeof(Type).Assembly.GetTypes().FirstOrDefault(type => type.Name == "TypeBuilderInstantiation");
            if(typeBuilderInstantiationType == null)
                throw new InvalidOperationException("Type 'TypeBuilderInstantiation' is not found");
            using(var il = new GroboIL(method))
            {
                il.Ldarg(0);
                il.Isinst(typeBuilderInstantiationType);
                il.Ldnull();
                il.Cgt(true);
                il.Ret();
            }
            return (Func<Type, bool>)method.CreateDelegate(typeof(Func<Type, bool>));
        }

        private static readonly Func<Type, bool> isTypeBuilderInstantiation = BuildIsTypeBuilderInstantiation();

        private readonly TracingWrapperConfigurator configurator;

        private static readonly AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(WrappersAssemblyName), AssemblyBuilderAccess.RunAndSave);
        private readonly ModuleBuilder module = assembly.DefineDynamicModule("Wrappers_" + Guid.NewGuid(), true);

        private readonly Hashtable wrapperTypes = new Hashtable();
        private readonly Hashtable buildingWrapperTypes = new Hashtable();
        private readonly Hashtable wrapperConstructors = new Hashtable();
        private readonly object lockObject = new object();

        private static readonly MethodInfo methodHashKeyGetter = HackHelpers.GetMethodDefinition<MethodInfo>(method => GetMethodHashKey(method));
        private static readonly MethodInfo methodGetter = HackHelpers.GetMethodDefinition<Type>(type => GetMethod(type, 0, 0));

        private static readonly MethodInfo tracingAnalyzerMethodStartedMethod = ((MethodCallExpression)((Expression<Action>)(() => TracingAnalyzer.MethodStarted(null, 0))).Body).Method;
        private static readonly MethodInfo tracingAnalyzerMethodFinishedMethod = ((MethodCallExpression)((Expression<Action>)(() => TracingAnalyzer.MethodFinished(null, 0, 0))).Body).Method;
        private static readonly MethodInfo classWrapperUnWrapMethod = ((MethodCallExpression)((Expression<Func<IClassWrapper, object>>)(wrapper => wrapper.UnWrap())).Body).Method;
        private static readonly MethodInfo methodBaseGetMethodFromMethodHandleMethod = ((MethodCallExpression)((Expression<Func<RuntimeMethodHandle, MethodBase>>)((methodHandle) => MethodBase.GetMethodFromHandle(methodHandle))).Body).Method;
        private static readonly MethodInfo methodBaseGetMethodFromMethodHandleAndTypeHandleMethod = ((MethodCallExpression)((Expression<Func<RuntimeMethodHandle, RuntimeTypeHandle, MethodBase>>)((methodHandle, typeHandle) => MethodBase.GetMethodFromHandle(methodHandle, typeHandle))).Body).Method;
        private static readonly MethodInfo methodBaseMethodHandleGetter = ((PropertyInfo)((MemberExpression)((Expression<Func<MethodBase, RuntimeMethodHandle>>)(method => method.MethodHandle)).Body).Member).GetGetMethod();
        private static readonly MethodInfo runtimeMethodHandleValueGetter = ((PropertyInfo)((MemberExpression)((Expression<Func<RuntimeMethodHandle, IntPtr>>)(handle => handle.Value)).Body).Member).GetGetMethod();
        private static readonly MethodInfo intPtrToInt64Method = ((MethodCallExpression)((Expression<Func<IntPtr, long>>)(x => x.ToInt64())).Body).Method;
        private static readonly MethodInfo typeFromTypeHandleMethod = HackHelpers.GetMethodDefinition<Type>(handle => Type.GetTypeFromHandle(default(RuntimeTypeHandle)));
        private static readonly MethodInfo typeTypeHandleGetter = HackHelpers.GetProp<Type>(type => type.TypeHandle).GetGetMethod();

        private static readonly IntPtr ticksReaderAddress = GetTicksReaderAddress();
        private static readonly Func<long> getTicks = EmitTicksGetter();
        private static readonly Func<ModuleHandle, long> moduleRuntimeHandleGetter = EmitModuleRuntimeHandleGetter();
    }
}