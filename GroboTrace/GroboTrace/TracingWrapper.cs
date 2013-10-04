using System;
using System.Collections;
using System.Collections.Generic;
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
            if(configurator.Forbidden(implementationType))
            {
                wrapperType = null;
                return false;
            }
            if(!implementationType.IsGenericType)
            {
                wrapperType = GetWrapperType(implementationType);
                return true;
            }
            wrapperType = GetWrapperType(implementationType.GetGenericTypeDefinition());
            wrapperType = wrapperType.MakeGenericType(implementationType.GetGenericArguments()); //implementationType.ContainsGenericParameters ? wrapperType : wrapperType.MakeGenericType(implementationType.GetGenericArguments());
            return true;
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
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return methods.Single(method => moduleRuntimeHandleGetter(method.Module.ModuleHandle) == moduleHandle && method.MetadataToken == methodToken);
        }

        public static Func<long> GetTicks { get { return getTicks; } }

        public const string WrappersAssemblyName = "b5cc8d5b-fd0e-4b90-b545-d5c09c3ea040";

        private Type GetWrapperType(Type implementationType)
        {
            var wrapperType = (Type)wrapperTypes[implementationType];
            if(wrapperType == null)
            {
                lock(wrappersTypesLock)
                {
                    wrapperType = (Type)wrapperTypes[implementationType];
                    if(wrapperType == null)
                    {
                        wrapperType = WrapInternal(implementationType);
                        wrapperTypes[implementationType] = wrapperType;
                    }
                }
            }
            return wrapperType;
        }

        private Type WrapInternal(Type implementationType)
        {
            TypeBuilder typeBuilder = module.DefineType(implementationType + "_Wrapper_" + Guid.NewGuid(), TypeAttributes.Public | TypeAttributes.Class);

            Type[] genericArguments = null;
            GenericTypeParameterBuilder[] genericParameters = null;
            if(implementationType.IsGenericTypeDefinition)
            {
                genericArguments = implementationType.GetGenericArguments();
                genericParameters = typeBuilder.DefineGenericParameters(genericArguments.Select(type => "Z" + type.Name).ToArray());
                for(int index = 0; index < genericArguments.Length; index++)
                {
                    var genericArgument = genericArguments[index];
                    genericParameters[index].SetGenericParameterAttributes(genericArgument.GenericParameterAttributes & ~(GenericParameterAttributes.Contravariant | GenericParameterAttributes.Covariant));
                    genericParameters[index].SetBaseTypeConstraint(Refine(genericArgument.BaseType, genericArguments, genericParameters));
                    genericParameters[index].SetInterfaceConstraints(GetUnique(genericArgument.GetInterfaces(), genericArgument.BaseType == null ? new Type[0] : genericArgument.BaseType.GetInterfaces()).Select(type => Refine(type, genericArguments, genericParameters)).ToArray());
                }
            }
            FieldBuilder implField = typeBuilder.DefineField("impl", typeof(object), FieldAttributes.Private | FieldAttributes.InitOnly);
            BuildConstructor(typeBuilder, implField);
            var typeInitializer = typeBuilder.DefineTypeInitializer();
            var il = new GroboIL(typeInitializer);
//            var fieldsValues = new List<KeyValuePair<FieldBuilder, object>>();
            if(implementationType.IsInterface)
            {
                foreach(var interfaceType in new[] {implementationType}.Concat(implementationType.GetInterfaces()))
                {
                    if(!IsPublic(interfaceType))
                        continue;
                    var refinedInterfaceType = Refine(interfaceType, genericArguments, genericParameters);
                    foreach(var method in interfaceType.GetMethods())
                    {
                        var methodBuilder = BuildMethod(typeBuilder, method, method, genericArguments, genericParameters, implField, il);
                        typeBuilder.DefineMethodOverride(methodBuilder, method);
                    }
                    typeBuilder.AddInterfaceImplementation(refinedInterfaceType);
                }
            }
            else
            {
                var builtMethods = new HashSet<MethodInfo>();
                foreach(var interfaceType in implementationType.GetInterfaces())
                {
                    if(!IsPublic(interfaceType))
                        continue;
                    var refinedInterfaceType = Refine(interfaceType, genericArguments, genericParameters);
                    var interfaceMap = implementationType.GetInterfaceMap(interfaceType);
                    for(int index = 0; index < interfaceMap.InterfaceMethods.Length; ++index)
                    {
                        builtMethods.Add(interfaceMap.TargetMethods[index]);
                        var methodBuilder = BuildMethod(typeBuilder, interfaceMap.TargetMethods[index], interfaceMap.InterfaceMethods[index], genericArguments, genericParameters, implField, il);
                        typeBuilder.DefineMethodOverride(methodBuilder, interfaceMap.InterfaceMethods[index]);
                    }
                    typeBuilder.AddInterfaceImplementation(refinedInterfaceType);
                }
                if(IsPublic(implementationType) && !implementationType.IsInterface)
                {
                    var methods = implementationType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    foreach(var method in methods)
                    {
                        if(builtMethods.Contains(method))
                            continue;
                        BuildMethod(typeBuilder, method, method, genericArguments, genericParameters, implField, il);
                    }
                }
            }
            typeBuilder.DefineMethodOverride(BuildUnWrapMethod(typeBuilder, implField), classWrapperUnWrapMethod);
            typeBuilder.AddInterfaceImplementation(typeof(IClassWrapper));

            il.Ret();

            return typeBuilder.CreateType();
        }

        private static bool IsPublic(Type type)
        {
            if(type.Assembly.GetCustomAttributes(true).OfType<InternalsVisibleToAttribute>().Any(attribute => attribute.AssemblyName == WrappersAssemblyName))
                return true;
            if(!type.IsNested)
                return type.IsPublic;
            return type.IsNestedPublic && IsPublic(type.DeclaringType);
        }

//        private static Action GetFieldsInitializer(TypeBuilder typeBuilder, List<KeyValuePair<FieldBuilder, object>> fields)
//        {
//            var method = typeBuilder.DefineMethod("Initialize_" + Guid.NewGuid(), MethodAttributes.Public | MethodAttributes.Static, typeof(void), new[] {typeof(object[])});
//            var il = method.GetILGenerator();
//            var values = new List<object>();
//            for(int index = 0; index < fields.Count; ++index)
//            {
//                il.Emit(OpCodes.Ldnull); // stack: [null]
//                il.Emit(OpCodes.Ldarg_0); // stack: [null, values]
//                il.Emit(OpCodes.Ldc_I4, index); // stack: [null, values, index]
//                il.Emit(OpCodes.Ldelem_Ref); // stack: [null, values[index]]
//                var field = fields[index].Key;
//                if(field.FieldType.IsValueType)
//                    il.Emit(OpCodes.Unbox_Any, field.FieldType);
//                il.Emit(OpCodes.Stfld, field); // field = values[index]
//                values.Add(fields[index].Value);
//            }
//            il.Emit(OpCodes.Ret);
//            return () => typeBuilder.GetMethod(method.Name).Invoke(null, new object[] {values.ToArray()});
//        }
//
        private static Type[] GetUnique(Type[] interfaces, Type[] baseInterfaces)
        {
            // todo make topsort
            var hashSet = new HashSet<Type>(interfaces);
            foreach(var type in baseInterfaces)
            {
                if(hashSet.Contains(type))
                    hashSet.Remove(type);
            }
            while(true)
            {
                bool end = true;
                foreach(var type in hashSet.ToArray())
                {
                    var children = type.GetInterfaces();
                    foreach(var child in children)
                    {
                        if(hashSet.Contains(child))
                        {
                            end = false;
                            hashSet.Remove(child);
                        }
                    }
                }
                if(end) break;
            }
            return hashSet.ToArray();
        }

        private static MethodBuilder BuildUnWrapMethod(TypeBuilder typeBuilder, FieldInfo implField)
        {
            var method = typeBuilder.DefineMethod("UnWrap", MethodAttributes.Public | MethodAttributes.Virtual, CallingConventions.HasThis, typeof(object), null);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); // stack: [this]
            il.Emit(OpCodes.Ldfld, implField); // stack: [this.impl]
            il.Emit(OpCodes.Ret);
            return method;
        }

        private static Type Refine(Type type, Type[] genericArguments, GenericTypeParameterBuilder[] genericParameters)
        {
            if(type != null && type.IsGenericType)
                return type.GetGenericTypeDefinition().MakeGenericType(type.GetGenericArguments().Select(t => Refine(t, genericArguments, genericParameters)).ToArray());
            var i = genericArguments == null ? -1 : Array.IndexOf(genericArguments, type);
            return i >= 0 ? genericParameters[i] : type;
        }

        private MethodBuilder BuildMethod(TypeBuilder typeBuilder, MethodInfo implementationMethod, MethodInfo abstractionMethod, Type[] parentGenericArguments, GenericTypeParameterBuilder[] parentGenericParameters, FieldInfo implField, GroboIL typeInitializerIl)
        {
//            var parameters = implementationMethod.GetParameters();
//            var returnType = implementationMethod.ReturnType;
//            var method = typeBuilder.DefineMethod(implementationMethod.Name, implementationMethod.IsVirtual ? MethodAttributes.Public | MethodAttributes.Virtual : MethodAttributes.Public, CallingConventions.HasThis, returnType,
//                                                  implementationMethod.ReturnParameter == null ? null : implementationMethod.ReturnParameter.GetRequiredCustomModifiers(),
//                                                  implementationMethod.ReturnParameter == null ? null : implementationMethod.ReturnParameter.GetOptionalCustomModifiers(),
//                                                  parameters.Select(parameter => parameter.ParameterType).ToArray(),
//                                                  parameters.Select(parameter => parameter.GetRequiredCustomModifiers()).ToArray(),
//                                                  parameters.Select(parameter => parameter.GetOptionalCustomModifiers()).ToArray());
            var reflectedType = implementationMethod.ReflectedType;
            if(reflectedType.IsGenericTypeDefinition)
                reflectedType = reflectedType.MakeGenericType(parentGenericParameters);
            else if(reflectedType.IsGenericType)
                reflectedType = Refine(reflectedType, parentGenericArguments, parentGenericParameters);

            MethodBuilder method;
            Type returnType;
            Type[] parameterTypes;
            if(!implementationMethod.IsGenericMethod)
            {
                returnType = Refine(implementationMethod.ReturnType, parentGenericArguments, parentGenericParameters);
                parameterTypes = implementationMethod.GetParameters().Select(info => Refine(info.ParameterType, parentGenericArguments, parentGenericParameters)).ToArray();
                method = typeBuilder.DefineMethod(implementationMethod.Name, implementationMethod.IsVirtual ? MethodAttributes.Public | MethodAttributes.Virtual : MethodAttributes.Public, CallingConventions.HasThis, returnType, parameterTypes);
            }
            else
            {
                method = typeBuilder.DefineMethod(implementationMethod.Name, implementationMethod.IsVirtual ? MethodAttributes.Public | MethodAttributes.Virtual : MethodAttributes.Public, CallingConventions.HasThis);
                var genericArguments = implementationMethod.GetGenericArguments();
                var genericParameters = method.DefineGenericParameters(genericArguments.Select(type => type.Name).ToArray());
                var allGenericArguments = parentGenericArguments == null ? genericArguments : parentGenericArguments.Concat(genericArguments).ToArray();
                var allGenericParameters = parentGenericParameters == null ? genericParameters : parentGenericParameters.Concat(genericParameters).ToArray();
                for(int index = 0; index < genericArguments.Length; index++)
                {
                    var genericArgument = genericArguments[index];
                    genericParameters[index].SetGenericParameterAttributes(genericArgument.GenericParameterAttributes);
                    genericParameters[index].SetBaseTypeConstraint(Refine(genericArgument.BaseType, allGenericArguments, allGenericParameters));
                    genericParameters[index].SetInterfaceConstraints(GetUnique(genericArgument.GetInterfaces(), genericArgument.BaseType == null ? new Type[0] : genericArgument.BaseType.GetInterfaces()).Select(type => Refine(type, allGenericArguments, allGenericParameters)).ToArray());
                }
                returnType = Refine(implementationMethod.ReturnType, allGenericArguments, allGenericParameters);
                parameterTypes = implementationMethod.GetParameters().Select(info => Refine(info.ParameterType, allGenericArguments, allGenericParameters)).ToArray();
                method.SetReturnType(returnType);
                method.SetParameters(parameterTypes);
            }
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

            FieldBuilder methodField = typeBuilder.DefineField("method_" + implementationMethod.Name + "_" + Guid.NewGuid(), typeof(MethodInfo), FieldAttributes.Private | FieldAttributes.InitOnly | FieldAttributes.Static);
            FieldBuilder methodHandleField = typeBuilder.DefineField("methodHandle_" + implementationMethod.Name + "_" + Guid.NewGuid(), typeof(long), FieldAttributes.Private | FieldAttributes.InitOnly | FieldAttributes.Static);

            if(!implementationMethod.IsGenericMethod || !reflectedType.IsGenericType)
            {
                typeInitializerIl.Ldtoken(implementationMethod);
                typeInitializerIl.Ldtoken(reflectedType);
                typeInitializerIl.Call(methodBaseGetMethodFromMethodHandleAndTypeHandleMethod);
            }
            else
            {
                // todo ldtoken для generic-метода в generic-классе почему-то выбрасывает BadImageFormatException, может быть, баг в .NET
                typeInitializerIl.Ldtoken(reflectedType);
                typeInitializerIl.Call(typeFromTypeHandleMethod);
                typeInitializerIl.Ldc_I8(moduleRuntimeHandleGetter(implementationMethod.Module.ModuleHandle));
                typeInitializerIl.Ldc_I4(implementationMethod.MetadataToken);
                typeInitializerIl.Call(methodGetter);
            }
            typeInitializerIl.Dup();
            typeInitializerIl.Stfld(methodField);
            typeInitializerIl.Call(methodHashKeyGetter);
            typeInitializerIl.Stfld(methodHandleField);

            var il = method.GetILGenerator();
            LocalBuilder result = returnType == typeof(void) ? null : il.DeclareLocal(returnType);
            var startTicks = il.DeclareLocal(typeof(long));
            var endTicks = il.DeclareLocal(typeof(long));

            il.Emit(OpCodes.Ldsfld, methodField); // stack: [method]
            il.Emit(OpCodes.Ldsfld, methodHandleField); // stack: [method, methodHandle]
            il.EmitCall(OpCodes.Call, tracingAnalyzerMethodStartedMethod, null); // TracingAnalyzer.MethodStarted(method, methodHandle)
            il.Emit(OpCodes.Ldloca_S, startTicks); // stack: [ref startTicks]
            if(IntPtr.Size == 4)
                il.Emit(OpCodes.Ldc_I4, ticksReaderAddress.ToInt32());
            else
                il.Emit(OpCodes.Ldc_I8, ticksReaderAddress.ToInt64());
            il.EmitCalli(OpCodes.Calli, CallingConvention.StdCall, typeof(void), new[] {typeof(IntPtr)}); // GetTicks(ref startTicks)

            il.BeginExceptionBlock();

            il.Emit(OpCodes.Ldarg_0); // stack: [this]
            il.Emit(OpCodes.Ldfld, implField); // stack: [impl]
            for(int i = 0; i < parameterTypes.Length; ++i)
                il.Emit(OpCodes.Ldarg_S, i + 1); // stack: [impl, parameters]
            il.EmitCall(OpCodes.Callvirt, abstractionMethod, null); // impl.method(parameters)

            if(returnType.IsInterface)
            {
                Type wrapperType;
                if(TryWrap(returnType, out wrapperType))
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Isinst, typeof(IClassWrapper));
                    var wrappedLabel = il.DefineLabel();
                    il.Emit(OpCodes.Brtrue, wrappedLabel);
                    var constructor = !wrapperType.ContainsGenericParameters ? wrapperType.GetConstructors().Single() : TypeBuilder.GetConstructor(wrapperType, wrapperType.GetGenericTypeDefinition().GetConstructors().Single());
                    il.Emit(OpCodes.Newobj, constructor);
                    il.MarkLabel(wrappedLabel);
                }
            }

            if(result != null)
                il.Emit(OpCodes.Stloc_S, result); // result = impl.method(parameters)

            var retLabel = il.DefineLabel();
            il.Emit(OpCodes.Leave_S, retLabel);
            il.BeginFinallyBlock();

            il.Emit(OpCodes.Ldloca_S, endTicks); // stack: [ref endTicks]
            if(IntPtr.Size == 4)
                il.Emit(OpCodes.Ldc_I4, ticksReaderAddress.ToInt32());
            else
                il.Emit(OpCodes.Ldc_I8, ticksReaderAddress.ToInt64());
            il.EmitCalli(OpCodes.Calli, CallingConvention.StdCall, typeof(void), new[] {typeof(IntPtr)}); // GetTicks(ref endTicks)

            il.Emit(OpCodes.Ldsfld, methodField); // stack: [method]
            il.Emit(OpCodes.Ldsfld, methodHandleField); // stack: [method, methodHandle]

            il.Emit(OpCodes.Ldloc_S, endTicks); // stack: [method, methodHandle, endTicks]
            il.Emit(OpCodes.Ldloc_S, startTicks); // stack: [method, methodHandle, endTicks, startTicks]
            il.Emit(OpCodes.Sub); // stack: [method, methodHandle, endTicks - startTicks = elapsed]

            il.EmitCall(OpCodes.Call, tracingAnalyzerMethodFinishedMethod, null); // TracingAnalyzer.MethodFinished(method, methodHandle, elapsed)

            il.EndExceptionBlock();
            il.MarkLabel(retLabel);

            if(result != null)
                il.Emit(OpCodes.Ldloc_S, result);
            il.Emit(OpCodes.Ret);
            return method;
        }

        private static void BuildConstructor(TypeBuilder typeBuilder, FieldInfo implField)
        {
            var constructor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new[] {implField.FieldType});
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); // stack: [this]
            il.Emit(OpCodes.Ldarg_1); // stack: [impl]
            il.Emit(OpCodes.Stfld, implField); // this.implField = impl
            il.Emit(OpCodes.Ret);
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private static IntPtr GetTicksReaderAddress()
        {
            string resourceName = string.Format("rdtsc{0}.dll", IntPtr.Size == 4 ? "32" : "64");
            var rdtscDll = Assembly.GetExecutingAssembly().GetManifestResourceStream("GroboTrace.Rdtsc." + resourceName);
            if(rdtscDll != null)
            {
                var rdtscDllContent = new byte[rdtscDll.Length];
                if(rdtscDll.Read(rdtscDllContent, 0, rdtscDllContent.Length) == rdtscDllContent.Length)
                {
                    var directoryName = AppDomain.CurrentDomain.BaseDirectory; //Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);
                    if(string.IsNullOrEmpty(directoryName))
                        throw new InvalidOperationException("Unable to obtain binaries directory");
                    string rdtscDllFileName = Path.Combine(directoryName, string.Format("rdtsc{0}_{1}.dll", IntPtr.Size == 4 ? "32" : "64", Guid.NewGuid().ToString("N")));
                    File.WriteAllBytes(rdtscDllFileName, rdtscDllContent);
                    IntPtr rdtscDllModuleHandle = LoadLibrary(rdtscDllFileName);
                    if(rdtscDllModuleHandle != IntPtr.Zero)
                    {
                        IntPtr rdtscProcAddress = GetProcAddress(rdtscDllModuleHandle, "ReadTimeStampCounter");
                        if(rdtscProcAddress != IntPtr.Zero)
                            return rdtscProcAddress;
                    }
                }
            }
            IntPtr kernel32ModuleHandle = GetModuleHandle("kernel32.dll");
            return kernel32ModuleHandle == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(kernel32ModuleHandle, "QueryPerformanceCounter");
        }

        private static Func<long> EmitTicksGetter()
        {
            var dynamicMethod = new DynamicMethod("GetTicks_" + Guid.NewGuid(), typeof(long), null, typeof(TracingWrapper));
            var il = dynamicMethod.GetILGenerator();
            var ticks = il.DeclareLocal(typeof(long));
            il.Emit(OpCodes.Ldloca_S, ticks); // stack: [&ticks]
            if(IntPtr.Size == 4)
                il.Emit(OpCodes.Ldc_I4, ticksReaderAddress.ToInt32());
            else
                il.Emit(OpCodes.Ldc_I8, ticksReaderAddress.ToInt64());
            il.EmitCalli(OpCodes.Calli, CallingConvention.StdCall, typeof(void), new[] {typeof(IntPtr)});
            il.Emit(OpCodes.Ldloc_S, ticks);
            il.Emit(OpCodes.Ret);
            return (Func<long>)dynamicMethod.CreateDelegate(typeof(Func<long>));
        }

        private static Func<ModuleHandle, long> EmitModuleRuntimeHandleGetter()
        {
            var runtimeModuleType = typeof(ModuleHandle).Assembly.GetTypes().Single(type => type.Name == "RuntimeModule");
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(long), new[] {typeof(ModuleHandle)}, typeof(TracingWrapper), true);
            var il = new GroboIL(method);
            il.Ldarga(0);
            il.Ldfld(typeof(ModuleHandle).GetField("m_ptr", BindingFlags.Instance | BindingFlags.NonPublic));
            il.Ldfld(runtimeModuleType.GetField("m_pData", BindingFlags.Instance | BindingFlags.NonPublic));
            var local = il.DeclareLocal(typeof(IntPtr));
            il.Stloc(local);
            il.Ldloca(local);
            il.Call(intPtrToInt64Method, typeof(IntPtr));
            il.Ret();
            return (Func<ModuleHandle, long>)method.CreateDelegate(typeof(Func<ModuleHandle, long>));
        }

        private readonly TracingWrapperConfigurator configurator;

        private static readonly AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(WrappersAssemblyName), AssemblyBuilderAccess.RunAndSave);
        private readonly ModuleBuilder module = assembly.DefineDynamicModule("Wrappers_" + Guid.NewGuid() /*, "zzz.dll"*/);

        private readonly Hashtable wrapperTypes = new Hashtable();
        private readonly object wrappersTypesLock = new object();

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