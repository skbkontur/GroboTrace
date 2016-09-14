using System;
using System.Reflection;
using System.Reflection.Emit;

namespace GroboTrace.MethodBodyParsing
{
    internal class DynamicILGenerator
    {
        static DynamicILGenerator()
        {
            var assembly = typeof(DynamicMethod).Assembly;
            var t_DynamicILGenerator = assembly.GetType("System.Reflection.Emit.DynamicILGenerator");

            var m_methodSigTokenField = t_DynamicILGenerator.GetField("m_methodSigToken", BindingFlags.Instance | BindingFlags.NonPublic);
            m_methodSigTokenExtractor = FieldsExtractor.GetExtractor<ILGenerator, int>(m_methodSigTokenField);

            var m_scopeField = t_DynamicILGenerator.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic);
            m_scopeExtractor = FieldsExtractor.GetExtractor<ILGenerator, object>(m_scopeField);
        }

        public DynamicILGenerator(ILGenerator inst)
        {
            this.inst = inst;
        }

        public int m_methodSigToken { get { return m_methodSigTokenExtractor(inst); } }
        public DynamicScope m_scope { get { return new DynamicScope(m_scopeExtractor(inst)); } }

        public static void Init()
        {
        }

        public ILGenerator inst;

        private static readonly Func<ILGenerator, int> m_methodSigTokenExtractor;
        private static readonly Func<ILGenerator, object> m_scopeExtractor;
    }
}