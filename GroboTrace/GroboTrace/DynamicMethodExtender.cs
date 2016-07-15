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
using OpCodes = GroboTrace.Mono.Cecil.Cil.OpCodes;
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

            mscorlib = typeof(DynamicMethod).Assembly;
            t_dynamicResolver = mscorlib.GetType("System.Reflection.Emit.DynamicResolver");
            t_dynamicILGenerator = mscorlib.GetType("System.Reflection.Emit.DynamicILGenerator");
            t_dynamicMethod = mscorlib.GetType("System.Reflection.Emit.DynamicMethod");
        }


        private unsafe CORINFO_EH_CLAUSE[] getExceptions(object dynamicResolver, int excCount)
        {
            var getEHInfo = t_dynamicResolver.GetMethod("GetEHInfo", BindingFlags.Instance | BindingFlags.NonPublic);

            var exceptions = new CORINFO_EH_CLAUSE[excCount];
            
            for (int i = 0; i < excCount; ++i)
                fixed (CORINFO_EH_CLAUSE* pointer = &exceptions[i])
                {
                    getEHInfo.Invoke(dynamicResolver, new object[] { i, (IntPtr)pointer });
                }

            return exceptions;
        }




        public void Trace()
        {
            addLocalVariables();

            var ilGenerator = dynamicMethod.GetILGenerator();

            var dynamicResolver = t_dynamicResolver
                                          .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {t_dynamicILGenerator}, null)
                                          .Invoke(new object[] {ilGenerator});

            var getCodeInfo = t_dynamicResolver.GetMethod("GetCodeInfo", BindingFlags.Instance | BindingFlags.NonPublic);

            var localSignature = (byte[])t_dynamicResolver.GetField("m_localSignature", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicResolver);

            byte[] code;
            int stackSize = 0;
            int initLocals = 0;
            int EHCount = 0;

            var parameters = new object[] {stackSize, initLocals, EHCount};

            code = (byte[])getCodeInfo.Invoke(dynamicResolver, parameters);

            //Console.WriteLine("Initial code");
            //Console.WriteLine(String.Join(", ", code));

            stackSize = (int)parameters[0];
            initLocals = (int)parameters[1];
            EHCount = (int)parameters[2];
            
            var exceptions = getExceptions(dynamicResolver, EHCount);

            
            CecilMethodBody methodBody = new CecilMethodBodyMaker(code, stackSize, dynamicMethod.InitLocals, exceptions).GetCecilMethodBody();

            Console.WriteLine(methodBody);






            //methodBody.instructions.RemoveAt(2);
            //methodBody.instructions.Insert(2,Instruction.Create(OpCodes.Sub));

            var reflectionMethodBodyMaker = new ReflectionMethodBodyMaker(methodBody);

            //Console.WriteLine("Changed code");
            //Console.WriteLine(String.Join(", ", reflectionMethodBodyMaker.GetCode()));
            


            var scope = t_dynamicILGenerator.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ilGenerator);
            

            DynamicILInfo dynamicIlInfo = (DynamicILInfo) t_dynamicMethod
                                                              .GetMethod("GetDynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic)
                                                              .Invoke(dynamicMethod, new [] {scope});
            
            dynamicIlInfo.SetCode(reflectionMethodBodyMaker.GetCode(), Math.Max(stackSize, 4));

            if (reflectionMethodBodyMaker.HasExceptions())
                dynamicIlInfo.SetExceptions(reflectionMethodBodyMaker.GetExceptions());

            dynamicIlInfo.SetLocalSignature(localSignature);


            var methodBody2 = new CecilMethodBodyMaker(reflectionMethodBodyMaker.GetCode(), stackSize, dynamicMethod.InitLocals, reflectionMethodBodyMaker.GetExceptions()).GetCecilMethodBody();
            Console.WriteLine(methodBody2);

          
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

        private Assembly mscorlib;
        private Type t_dynamicResolver;
        private Type t_dynamicILGenerator;
        private Type t_dynamicMethod;


        int resultLocalIndex = -1;
        int ticksLocalIndex;
        int profilerOverheadLocalIndex;

    }
}
