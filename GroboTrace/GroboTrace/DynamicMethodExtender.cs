using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using GroboTrace.Mono.Cecil.Cil;

using CecilMethodBody = GroboTrace.Mono.Cecil.Cil.MethodBody;
using ReflectionMethodBody = System.Reflection.MethodBody;

namespace GroboTrace
{
    public struct CORINFO_EH_CLAUSE
    {
        internal int Flags;
        internal int TryOffset;
        internal int TryLength;
        internal int HandlerOffset;
        internal int HandlerLength;
        internal int ClassTokenOrFilterOffset;
    }





    public class DynamicMethodExtender
    {
        public DynamicMethodExtender(DynamicMethod dynamicMethod)
        {
            this.dynamicMethod = dynamicMethod;
            hasReturnType = dynamicMethod.ReturnType != typeof(void);
        }


        private unsafe CORINFO_EH_CLAUSE[] getExceptions(object dynamicResolver, int excCount)
        {
            var mscorlib = typeof(DynamicMethod).Assembly;
            var t_dynamicResolver = mscorlib.GetType("System.Reflection.Emit.DynamicResolver");
            var getEHInfo = t_dynamicResolver.GetMethod("GetEHInfo", BindingFlags.Instance | BindingFlags.NonPublic);

            var exceptions = new CORINFO_EH_CLAUSE[excCount];

            for (int i = 0; i < excCount; ++i)
                fixed (CORINFO_EH_CLAUSE* pointer = &exceptions[i])
                {
                    getEHInfo.Invoke(dynamicResolver, new object[] { i, (IntPtr)pointer });
                }

            return exceptions;
        }




        public unsafe void Trace()
        {
            addLocalVariables();

            var mscorlib = typeof(DynamicMethod).Assembly;

            var t_dynamicResolver = mscorlib.GetType("System.Reflection.Emit.DynamicResolver");

            var dynamicResolver = t_dynamicResolver
                                          .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {mscorlib.GetType("System.Reflection.Emit.DynamicILGenerator")}, null)
                                          .Invoke(new object[] {dynamicMethod.GetILGenerator()});

            var getCodeInfo = t_dynamicResolver.GetMethod("GetCodeInfo", BindingFlags.Instance | BindingFlags.NonPublic);

            byte[] code;
            int stackSize = 0;
            int initLocals = 0;
            int EHCount = 0;

            var parameters = new object[] {stackSize, initLocals, EHCount};

            code = (byte[])getCodeInfo.Invoke(dynamicResolver, parameters);

            stackSize = (int)parameters[0];
            initLocals = (int)parameters[1];
            EHCount = (int)parameters[2];


            var exceptions = getExceptions(dynamicResolver, EHCount);


            //var code = t_dynamicResolver.GetField("m_code", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicResolver);
            //var stackSize = t_dynamicResolver.GetField("m_stackSize", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicResolver);
            //var exceptionInfos = t_dynamicResolver.GetField("m_exceptions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicResolver);
            
            

            CecilMethodBody methodBody = new ReflectionMethodBodyConverter(code, stackSize, dynamicMethod.InitLocals, exceptions).GetCecilMethodBody();

            Console.WriteLine(methodBody);
            

        }

        private void addLocalVariables()
        {
            var ilGenerator = dynamicMethod.GetILGenerator();

            if (hasReturnType)
            {
                resultLocalIndex = ilGenerator.DeclareLocal(dynamicMethod.ReturnType).LocalIndex;
            }

            ticksLocalIndex = ilGenerator.DeclareLocal(typeof(long)).LocalIndex;

            profilerOverheadLocalIndex = ilGenerator.DeclareLocal(typeof(long)).LocalIndex;
        }


        


        private DynamicMethod dynamicMethod;
        private bool hasReturnType;

        int resultLocalIndex = -1;
        int ticksLocalIndex;
        int profilerOverheadLocalIndex;

    }
}
