using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using GroboTrace.Mono.Cecil.Cil;
using GroboTrace.Mono.Cecil.Metadata;

using CecilMethodBody = GroboTrace.Mono.Cecil.Cil.MethodBody;
using ExceptionHandler = GroboTrace.Mono.Cecil.Cil.ExceptionHandler;
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

            ticksReaderAddress = Zzz.ticksReaderAddress;
            getMethodBaseFunctionAddress = Zzz.getMethodBaseFunctionAddress;
            methodStartedAddress = Zzz.methodStartedAddress;
            methodFinishedAddress = Zzz.methodFinishedAddress;
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
            int i, j;
            long functionId;
            Zzz.AddMethod(dynamicMethod, out i, out j, out functionId);
            
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

            

            var scope = t_dynamicILGenerator.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ilGenerator);
            DynamicILInfo dynamicIlInfo = (DynamicILInfo)t_dynamicMethod
                                                              .GetMethod("GetDynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic)
                                                              .Invoke(dynamicMethod, new[] { scope });


            Zzz.ReplaceRetInstructions(methodBody.instructions, hasReturnType, resultLocalIndex);

            var ticksReaderSignature = typeof(Zzz).Module.ResolveSignature(typeof(Zzz).GetMethod("TemplateForTicksSignature", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var ticksReaderToken = new MetadataToken((uint)dynamicIlInfo.GetTokenFor(ticksReaderSignature));

            var getMethodBaseSignature = typeof(Zzz).Module.ResolveSignature(typeof(Zzz).GetMethod("getMethodBase", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var getMethodBaseToken = new MetadataToken((uint)dynamicIlInfo.GetTokenFor(getMethodBaseSignature));

            var methodStartedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var methodStartedToken = new MetadataToken((uint)dynamicIlInfo.GetTokenFor(methodStartedSignature));

            var methodFinishedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var methodFinishedToken = new MetadataToken((uint)dynamicIlInfo.GetTokenFor(methodFinishedSignature));

          


            int startIndex = 0;

            methodBody.instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress));
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, ticksReaderToken));
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Stloc, ticksLocalIndex));

            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldc_I4, i)); // [ i ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldc_I4, j)); // [ i, j ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)getMethodBaseFunctionAddress : (long)getMethodBaseFunctionAddress)); // [ i, j, funcAddr ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, getMethodBaseToken)); // [ ourMethod ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldc_I8, (long)functionId)); // [ ourMethod, functionId ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodStartedAddress : (long)methodStartedAddress)); // [ ourMethod, functionId, funcAddr ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, methodStartedToken)); // []

            methodBody.instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress)); // [ funcAddr ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, ticksReaderToken)); // [ ticksNow ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Dup)); // [ ticksNow, ticksNow ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldloc, ticksLocalIndex)); // [ ticksNow, ticksNow, oldTicks ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Sub)); // [ ticksNow, profilingOverhead ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Stloc, profilerOverheadLocalIndex)); // [ ticksNow ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Stloc, ticksLocalIndex)); // []

            var tryStartInstruction = methodBody.instructions[startIndex];


            Instruction tryEndInstruction;
            Instruction finallyStartInstruction;

            methodBody.instructions.Insert(methodBody.instructions.Count, finallyStartInstruction = tryEndInstruction = Instruction.Create(OpCodes.Ldc_I8, (long)functionId));  // [ functionId ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress)); // [ functionId, funcAddr ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Calli, ticksReaderToken));  // [ functionId, ticks ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Ldloc, ticksLocalIndex));  // [ functionId, ticks, startTicks ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Sub));  // [ functionId, elapsed ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Ldloc, profilerOverheadLocalIndex));  // [ functionId, elapsed, profilerOverhead ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodFinishedAddress : (long)methodFinishedAddress)); // [ functionId, elapsed, profilerOverhead , funcAddr ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Calli, methodFinishedToken)); // []




            Instruction endFinallyInstruction;
            methodBody.instructions.Insert(methodBody.instructions.Count, endFinallyInstruction = Instruction.Create(OpCodes.Endfinally));

            Instruction finallyEndInstruction;

            if (resultLocalIndex >= 0)
            {

                methodBody.instructions.Insert(methodBody.instructions.Count, finallyEndInstruction = Instruction.Create(OpCodes.Ldloc, resultLocalIndex));
                methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Ret));
            }
            else
            {
                methodBody.instructions.Insert(methodBody.instructions.Count, finallyEndInstruction = Instruction.Create(OpCodes.Ret));
            }

            ExceptionHandler newException = new ExceptionHandler(ExceptionHandlerType.Finally);
            newException.TryStart = tryStartInstruction;
            newException.TryEnd = tryEndInstruction;
            newException.HandlerStart = finallyStartInstruction;
            newException.HandlerEnd = finallyEndInstruction;

            methodBody.instructions.Insert(methodBody.instructions.IndexOf(tryEndInstruction), Instruction.Create(OpCodes.Leave, finallyEndInstruction));

            methodBody.ExceptionHandlers.Add(newException);

            
            



            var reflectionMethodBodyMaker = new ReflectionMethodBodyMaker(methodBody);

            
            //Console.WriteLine("Changed code");
            //Console.WriteLine(String.Join(", ", reflectionMethodBodyMaker.GetCode()));
            


            
            

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

        private static IntPtr ticksReaderAddress;
        private static IntPtr getMethodBaseFunctionAddress;
        private static IntPtr methodStartedAddress;
        private static IntPtr methodFinishedAddress;


        int resultLocalIndex = -1;
        int ticksLocalIndex;
        int profilerOverheadLocalIndex;

    }
}
