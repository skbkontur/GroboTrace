using System;
using System.Reflection;
using System.Reflection.Emit;

namespace GroboTrace.MethodBodyParsing
{
    internal class DynamicResolver : IDisposable
    {
        static DynamicResolver()
        {
            var assembly = typeof(DynamicMethod).Assembly;
            t_DynamicResolver = assembly.GetType("System.Reflection.Emit.DynamicResolver");
            t_DynamicILInfo = assembly.GetType("System.Reflection.Emit.DynamicILInfo");
            t_DynamicILGenerator = assembly.GetType("System.Reflection.Emit.DynamicILGenerator");
            BuildFactoryByDynamicILInfo();
            BuildFactoryByDynamicILGenerator();
            BuildGetCodeInfoDelegate();
            BuildGetRawEHInfoDelegate();
            BuildGetEHInfoDelegate();
            m_methodSetter = FieldsExtractor.GetSetter(t_DynamicResolver.GetField("m_method", BindingFlags.Instance | BindingFlags.NonPublic));
            var m_resolverField = typeof(DynamicMethod).GetField("m_resolver", BindingFlags.Instance | BindingFlags.NonPublic);
            m_resolverSetter = FieldsExtractor.GetSetter<DynamicMethod, object>(m_resolverField);
            var m_localSignatureField = t_DynamicResolver.GetField("m_localSignature", BindingFlags.Instance | BindingFlags.NonPublic);
            m_localSignatureExtractor = FieldsExtractor.GetExtractor<object, byte[]>(m_localSignatureField);
        }

        public DynamicResolver(DynamicMethod dynamicMethod, DynamicILInfo dynamicILInfo)
        {
            this.dynamicMethod = dynamicMethod;
            inst = factoryByDynamicILInfo(dynamicILInfo);
        }

        public DynamicResolver(DynamicMethod dynamicMethod, ILGenerator ilGenerator)
        {
            this.dynamicMethod = dynamicMethod;
            inst = factoryByDynamicILGenerator(ilGenerator);
        }

        private static void BuildGetCodeInfoDelegate()
        {
            var parameterTypes = new[] {typeof(object), typeof(int).MakeByRefType(), typeof(int).MakeByRefType(), typeof(int).MakeByRefType()};
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(byte[]), parameterTypes, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicResolver);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_3);
            var getCodeInfoMethod = t_DynamicResolver.GetMethod("GetCodeInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            il.EmitCall(System.Reflection.Emit.OpCodes.Callvirt, getCodeInfoMethod, null);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            getCodeInfoDelegate = (GetCodeInfoDelegate)method.CreateDelegate(typeof(GetCodeInfoDelegate));
        }

        private static void BuildGetEHInfoDelegate()
        {
            var parameterTypes = new[] {typeof(object), typeof(int), typeof(void*)};
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(void), parameterTypes, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicResolver);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
            var getCodeInfoMethod = t_DynamicResolver.GetMethod("GetEHInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            il.EmitCall(System.Reflection.Emit.OpCodes.Callvirt, getCodeInfoMethod, null);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            getEHInfoDelegate = (GetEHInfoDelegate)method.CreateDelegate(typeof(GetEHInfoDelegate));
        }

        private static void BuildGetRawEHInfoDelegate()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(byte[]), new[] {typeof(object)}, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicResolver);
            var getRawEHInfoMethod = t_DynamicResolver.GetMethod("GetRawEHInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            il.EmitCall(System.Reflection.Emit.OpCodes.Callvirt, getRawEHInfoMethod, null);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            getRawEHInfoDelegate = (Func<object, byte[]>)method.CreateDelegate(typeof(Func<object, byte[]>));
        }

        private static void BuildFactoryByDynamicILInfo()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(object), new[] {t_DynamicILInfo}, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            var constructor = t_DynamicResolver.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {t_DynamicILInfo}, null);
            il.Emit(System.Reflection.Emit.OpCodes.Newobj, constructor);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            factoryByDynamicILInfo = (Func<DynamicILInfo, object>)method.CreateDelegate(typeof(Func<DynamicILInfo, object>));
        }

        private static void BuildFactoryByDynamicILGenerator()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(object), new[] {typeof(ILGenerator)}, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicILGenerator);
            var constructor = t_DynamicResolver.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {t_DynamicILGenerator}, null);
            il.Emit(System.Reflection.Emit.OpCodes.Newobj, constructor);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            factoryByDynamicILGenerator = (Func<ILGenerator, object>)method.CreateDelegate(typeof(Func<ILGenerator, object>));
        }

        private delegate byte[] GetCodeInfoDelegate(object inst, out int stackSize, out int initLocals, out int EHCount);

        private unsafe delegate void GetEHInfoDelegate(object inst, int excNumber, void* exc);

        public void Dispose()
        {
            m_methodSetter(inst, null);
            m_resolverSetter(dynamicMethod, null);
        }

        public byte[] GetCodeInfo(out int stackSize, out int initLocals, out int EHCount)
        {
            return getCodeInfoDelegate(inst, out stackSize, out initLocals, out EHCount);
        }

        public byte[] GetRawEHInfo()
        {
            return getRawEHInfoDelegate(inst);
        }

        public unsafe void GetEHInfo(int excNumber, void* exc)
        {
            getEHInfoDelegate(inst, excNumber, exc);
        }

        public byte[] m_localSignature { get { return m_localSignatureExtractor(inst); } }

        public static void Init()
        {
        }

        private readonly DynamicMethod dynamicMethod;
        private readonly object inst;

        private static Func<DynamicILInfo, object> factoryByDynamicILInfo;
        private static Func<ILGenerator, object> factoryByDynamicILGenerator;
        private static GetCodeInfoDelegate getCodeInfoDelegate;
        private static GetEHInfoDelegate getEHInfoDelegate;
        private static Func<object, byte[]> getRawEHInfoDelegate;
        private static readonly Action<object, object> m_methodSetter;
        private static readonly Type t_DynamicResolver;
        private static readonly Type t_DynamicILInfo;
        private static readonly Action<DynamicMethod, object> m_resolverSetter;
        private static readonly Func<object, byte[]> m_localSignatureExtractor;
        private static readonly Type t_DynamicILGenerator;
    }
}