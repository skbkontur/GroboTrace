using System;
using System.Reflection;
using System.Reflection.Emit;

namespace GroboTrace.MethodBodyParsing
{
    internal class DynamicMethodWrapper
    {
        static DynamicMethodWrapper()
        {
            var assembly = typeof(DynamicMethod).Assembly;
            t_DynamicScope = assembly.GetType("System.Reflection.Emit.DynamicScope");

            var m_DynamicILInfoField = typeof(DynamicMethod).GetField("m_DynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            m_DynamicILInfoExtractor = FieldsExtractor.GetExtractor<DynamicMethod, DynamicILInfo>(m_DynamicILInfoField);

            var m_ilGeneratorField = typeof(DynamicMethod).GetField("m_ilGenerator", BindingFlags.Instance | BindingFlags.NonPublic);
            m_ilGeneratorExtractor = FieldsExtractor.GetExtractor<DynamicMethod, ILGenerator>(m_ilGeneratorField);

            getDynamicIlInfo = BuildGetDynamicILInfo();
        }

        public DynamicMethodWrapper(DynamicMethod inst)
        {
            this.inst = inst;
        }

        private static Func<DynamicMethod, object, DynamicILInfo> BuildGetDynamicILInfo()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(DynamicILInfo), new[] {typeof(DynamicMethod), typeof(object)}, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0); // stack: [dynamicMethod]
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1); // stack: [dynamicMethod, scope]
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicScope);
            var getDynamicILInfoMethod = typeof(DynamicMethod).GetMethod("GetDynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            il.EmitCall(System.Reflection.Emit.OpCodes.Call, getDynamicILInfoMethod, null); // stack: [dynamicMethod.GetDynamicILInfo(scope)]
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            return (Func<DynamicMethod, object, DynamicILInfo>)method.CreateDelegate(typeof(Func<DynamicMethod, object, DynamicILInfo>));
        }

        public DynamicILInfo m_DynamicILInfo { get { return m_DynamicILInfoExtractor(inst); } }
        public ILGenerator m_ilGenerator { get { return m_ilGeneratorExtractor(inst); } }

        public static void Init()
        {
        }

        public DynamicILInfo GetDynamicILInfoWithOldScope()
        {
            return m_DynamicILInfo ?? (m_ilGenerator == null ? inst.GetDynamicILInfo() : getDynamicIlInfo(inst, new DynamicILGenerator(m_ilGenerator).m_scope.inst));
        }

        public DynamicMethod inst;

        private static readonly Func<DynamicMethod, DynamicILInfo> m_DynamicILInfoExtractor;
        private static readonly Func<DynamicMethod, ILGenerator> m_ilGeneratorExtractor;
        private static readonly Type t_DynamicScope;
        private static readonly Func<DynamicMethod, object, DynamicILInfo> getDynamicIlInfo;
    }
}