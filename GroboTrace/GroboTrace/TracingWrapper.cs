using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GroboTrace
{
    public static class TracingWrapper
    {
        public static Type Wrap(Type implementationType)
        {
            var @public = IsPublic(implementationType);
            TypeBuilder typeBuilder = module.DefineType(implementationType + "_Wrapper_" + Guid.NewGuid(), TypeAttributes.Public | TypeAttributes.Class);
            FieldBuilder implField = typeBuilder.DefineField("impl", typeof(object), FieldAttributes.Private | FieldAttributes.InitOnly);
            BuildConstructor(typeBuilder, implField);
            var fieldsValues = new List<KeyValuePair<FieldBuilder, object>>();
            var builtMethods = new HashSet<MethodInfo>();
            foreach(var interfaceType in implementationType.GetInterfaces())
            {
                if(!IsPublic(interfaceType))
                    continue;
                var interfaceMap = implementationType.GetInterfaceMap(interfaceType);
                for(int index = 0; index < interfaceMap.InterfaceMethods.Length; ++index)
                {
                    builtMethods.Add(interfaceMap.TargetMethods[index]);
                    var methodBuilder = BuildMethod(typeBuilder, interfaceMap.TargetMethods[index], interfaceMap.InterfaceMethods[index], implField, fieldsValues);
                    typeBuilder.DefineMethodOverride(methodBuilder, interfaceMap.InterfaceMethods[index]);
                }
                typeBuilder.AddInterfaceImplementation(interfaceType);
            }
            if(@public)
            {
                var methods = implementationType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                foreach(var method in methods)
                {
                    if(builtMethods.Contains(method))
                        continue;
                    BuildMethod(typeBuilder, method, method, implField, fieldsValues);
                }
            }

            typeBuilder.DefineMethodOverride(BuildUnWrapMethod(typeBuilder, implField), classWrapperUnWrapMethod);
            typeBuilder.AddInterfaceImplementation(typeof(IClassWrapper));

            Action initializer = GetFieldsInitializer(typeBuilder, fieldsValues);
            Type wrapper = typeBuilder.CreateType();
            initializer();
            return wrapper;
        }

        public static Func<long> GetTicks { get { return getTicks; } }
        public const string WrappersAssemblyName = "b5cc8d5b-fd0e-4b90-b545-d5c09c3ea040";

        private static bool IsPublic(Type type)
        {
            if(type.Assembly.GetCustomAttributes(true).OfType<InternalsVisibleToAttribute>().Any(attribute => attribute.AssemblyName == WrappersAssemblyName))
                return true;
            if(!type.IsNested)
                return type.IsPublic;
            return type.IsNestedPublic && IsPublic(type.DeclaringType);
        }

        private static Action GetFieldsInitializer(TypeBuilder typeBuilder, List<KeyValuePair<FieldBuilder, object>> fields)
        {
            var method = typeBuilder.DefineMethod("Initialize_" + Guid.NewGuid(), MethodAttributes.Public | MethodAttributes.Static, typeof(void), new[] {typeof(object[])});
            var il = method.GetILGenerator();
            var values = new List<object>();
            for(int index = 0; index < fields.Count; ++index)
            {
                il.Emit(OpCodes.Ldnull); // stack: [null]
                il.Emit(OpCodes.Ldarg_0); // stack: [null, values]
                il.Emit(OpCodes.Ldc_I4, index); // stack: [null, values, index]
                il.Emit(OpCodes.Ldelem_Ref); // stack: [null, values[index]]
                var field = fields[index].Key;
                if(field.FieldType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, field.FieldType);
                il.Emit(OpCodes.Stfld, field); // field = values[index]
                values.Add(fields[index].Value);
            }
            il.Emit(OpCodes.Ret);
            return () => typeBuilder.GetMethod(method.Name).Invoke(null, new object[] {values.ToArray()});
        }

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

        private static Type Refine(Type type, Type[] genericArguments, Type[] genericParameters)
        {
            if(type != null && type.IsGenericType)
            {
                return type.GetGenericTypeDefinition().MakeGenericType(type.GetGenericArguments().Select(
                    t =>
                        {
                            var i = Array.IndexOf(genericArguments, t);
                            return i >= 0 ? genericParameters[i] : t;
                        }).ToArray());
            }
            return type;
        }

        private static MethodBuilder BuildMethod(TypeBuilder typeBuilder, MethodInfo implementationMethod, MethodInfo abstractionMethod, FieldInfo implField, List<KeyValuePair<FieldBuilder, object>> fieldsValues)
        {
            var parameters = implementationMethod.GetParameters();
            var method = typeBuilder.DefineMethod(implementationMethod.Name, implementationMethod.IsVirtual ? MethodAttributes.Public | MethodAttributes.Virtual : MethodAttributes.Public, CallingConventions.HasThis, implementationMethod.ReturnType,
                                                  implementationMethod.ReturnParameter == null ? null : implementationMethod.ReturnParameter.GetRequiredCustomModifiers(),
                                                  implementationMethod.ReturnParameter == null ? null : implementationMethod.ReturnParameter.GetOptionalCustomModifiers(),
                                                  parameters.Select(parameter => parameter.ParameterType).ToArray(),
                                                  parameters.Select(parameter => parameter.GetRequiredCustomModifiers()).ToArray(),
                                                  parameters.Select(parameter => parameter.GetOptionalCustomModifiers()).ToArray());
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

            if(implementationMethod.IsGenericMethod)
            {
                var genericArguments = implementationMethod.GetGenericArguments();
                var genericParameters = method.DefineGenericParameters(genericArguments.Select(type => type.Name).ToArray());
                for(int index = 0; index < genericArguments.Length; index++)
                {
                    var genericArgument = genericArguments[index];
                    genericParameters[index].SetGenericParameterAttributes(genericArgument.GenericParameterAttributes);
                    genericParameters[index].SetBaseTypeConstraint(Refine(genericArgument.BaseType, genericArguments, genericParameters));
                    genericParameters[index].SetInterfaceConstraints(GetUnique(genericArgument.GetInterfaces(), genericArgument.BaseType == null ? new Type[0] : genericArgument.BaseType.GetInterfaces()).Select(type => Refine(type, genericArguments, genericParameters)).ToArray());
                }
            }

            var il = method.GetILGenerator();

            LocalBuilder result = implementationMethod.ReturnType == typeof(void) ? null : il.DeclareLocal(implementationMethod.ReturnType);
            var startTicks = il.DeclareLocal(typeof(long));
            var endTicks = il.DeclareLocal(typeof(long));

            FieldBuilder methodField = typeBuilder.DefineField("method_" + implementationMethod.Name, typeof(MethodInfo), FieldAttributes.Private | FieldAttributes.InitOnly | FieldAttributes.Static);
            FieldBuilder methodHandleField = typeBuilder.DefineField("methodHandle_" + implementationMethod.Name, typeof(long), FieldAttributes.Private | FieldAttributes.InitOnly | FieldAttributes.Static);
            fieldsValues.Add(new KeyValuePair<FieldBuilder, object>(methodField, implementationMethod));
            fieldsValues.Add(new KeyValuePair<FieldBuilder, object>(methodHandleField, implementationMethod.MethodHandle.Value.ToInt64()));

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
            for(int i = 0; i < parameters.Length; ++i)
                il.Emit(OpCodes.Ldarg_S, i + 1); // stack: [impl, parameters]
            il.EmitCall(OpCodes.Callvirt, abstractionMethod, null); // impl.method(parameters)
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
            var dynamicMethod = new DynamicMethod("GetTicks_" + Guid.NewGuid(), typeof(long), null, module);
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

        private static readonly AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(WrappersAssemblyName), AssemblyBuilderAccess.Run);
        private static readonly ModuleBuilder module = assembly.DefineDynamicModule(Guid.NewGuid().ToString());

        private static readonly IntPtr ticksReaderAddress = GetTicksReaderAddress();
        private static readonly Func<long> getTicks = EmitTicksGetter();

        private static readonly MethodInfo tracingAnalyzerMethodStartedMethod = ((MethodCallExpression)((Expression<Action>)(() => TracingAnalyzer.MethodStarted(null, 0))).Body).Method;
        private static readonly MethodInfo tracingAnalyzerMethodFinishedMethod = ((MethodCallExpression)((Expression<Action>)(() => TracingAnalyzer.MethodFinished(null, 0, 0))).Body).Method;
        private static readonly MethodInfo classWrapperUnWrapMethod = ((MethodCallExpression)((Expression<Func<IClassWrapper, object>>)(wrapper => wrapper.UnWrap())).Body).Method;
    }
}