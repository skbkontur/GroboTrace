using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public static void Trace(DynamicMethod dynamicMethod)
        {
            new DynamicMethodExtender(dynamicMethod).Extend();
        }


        private DynamicMethodExtender(DynamicMethod dynamicMethod)
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




        private void Extend()
        {
            var ilGenerator = dynamicMethod.GetILGenerator();

            var dynamicResolver = t_dynamicResolver
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {t_dynamicILGenerator}, null)
                .Invoke(new object[] {ilGenerator});

            var getCodeInfo = t_dynamicResolver.GetMethod("GetCodeInfo", BindingFlags.Instance | BindingFlags.NonPublic);

//            return;

            byte[] code;
            int stackSize = 0;
            int initLocals = 0;
            int EHCount = 0;

            var parameters = new object[] {stackSize, initLocals, EHCount};

            code = (byte[])getCodeInfo.Invoke(dynamicResolver, parameters);

            if(code == null)
                Debug.WriteLine("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

            Debug.WriteLine("Initial code");
            Debug.WriteLine(String.Join(", ", code));

            stackSize = (int)parameters[0];
            initLocals = (int)parameters[1];
            EHCount = (int)parameters[2];

            var exceptions = getExceptions(dynamicResolver, EHCount);

            CecilMethodBody methodBody = new CecilMethodBodyBuilder(code, stackSize, dynamicMethod.InitLocals, exceptions).GetCecilMethodBody();

            Debug.WriteLine(methodBody);

            var methodContainsCycles = new CycleFinder(methodBody.Instructions.ToArray()).IsThereAnyCycles();

//            if(methodBody.isTiny || !methodContainsCycles && methodBody.Instructions.Count < 50)
//            {
//                Debug.WriteLine(dynamicMethod + " too simple to be traced");
//                t_dynamicResolver.GetField("m_method", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dynamicResolver, null);
//                typeof(DynamicMethod).GetField("m_resolver", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dynamicMethod, null);
//
//                return;
//            }

            addLocalVariables();

            t_dynamicResolver.GetField("m_method", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dynamicResolver, null);
            typeof(DynamicMethod).GetField("m_resolver", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dynamicMethod, null);

            dynamicResolver = t_dynamicResolver
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {t_dynamicILGenerator}, null)
                .Invoke(new object[] {ilGenerator});

            var localSignature = (byte[])t_dynamicResolver.GetField("m_localSignature", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicResolver);

            t_dynamicResolver.GetField("m_method", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dynamicResolver, null);
            typeof(DynamicMethod).GetField("m_resolver", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dynamicMethod, null);

            var scope = t_dynamicILGenerator.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ilGenerator);
            DynamicILInfo dynamicIlInfo = (DynamicILInfo)t_dynamicMethod
                                                             .GetMethod("GetDynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic)
                                                             .Invoke(dynamicMethod, new[] {scope});
            int functionId;
            Zzz.AddMethod(dynamicMethod, out functionId);

            Zzz.ReplaceRetInstructions(methodBody.instructions, hasReturnType, resultLocalIndex);

            var ticksReaderSignature = typeof(Zzz).Module.ResolveSignature(typeof(Zzz).GetMethod("TemplateForTicksSignature", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var ticksReaderToken = new MetadataToken((uint)dynamicIlInfo.GetTokenFor(ticksReaderSignature));

            var methodStartedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var methodStartedToken = new MetadataToken((uint)dynamicIlInfo.GetTokenFor(methodStartedSignature));

            var methodFinishedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var methodFinishedToken = new MetadataToken((uint)dynamicIlInfo.GetTokenFor(methodFinishedSignature));

            int startIndex = 0;

            methodBody.instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress));
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, ticksReaderToken));
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Stloc, ticksLocalIndex));

            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldc_I4, (int)functionId)); // [ ourMethod, functionId ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodStartedAddress : (long)methodStartedAddress)); // [ ourMethod, functionId, funcAddr ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, methodStartedToken)); // []

            var tryStartInstruction = methodBody.instructions[startIndex];

            Instruction tryEndInstruction;
            Instruction finallyStartInstruction;

            methodBody.instructions.Insert(methodBody.instructions.Count, finallyStartInstruction = tryEndInstruction = Instruction.Create(OpCodes.Ldc_I4, (int)functionId)); // [ functionId ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress)); // [ functionId, funcAddr ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Calli, ticksReaderToken)); // [ functionId, ticks ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Ldloc, ticksLocalIndex)); // [ functionId, ticks, startTicks ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Sub)); // [ functionId, elapsed ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodFinishedAddress : (long)methodFinishedAddress)); // [ functionId, elapsed, profilerOverhead , funcAddr ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Calli, methodFinishedToken)); // []

            Instruction endFinallyInstruction;
            methodBody.instructions.Insert(methodBody.instructions.Count, endFinallyInstruction = Instruction.Create(OpCodes.Endfinally));

            Instruction finallyEndInstruction;

            if(resultLocalIndex >= 0)
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

            var reflectionMethodBodyBuilder = new ReflectionMethodBodyBuilder(methodBody);

            Debug.WriteLine("Changed code");
            Debug.WriteLine(String.Join(", ", reflectionMethodBodyBuilder.GetCode()));

            dynamicIlInfo.SetCode(reflectionMethodBodyBuilder.GetCode(), Math.Max(stackSize, 3));

            if(reflectionMethodBodyBuilder.HasExceptions())
                dynamicIlInfo.SetExceptions(reflectionMethodBodyBuilder.GetExceptions());

            dynamicIlInfo.SetLocalSignature(localSignature);

//            var methodBody2 = new CecilMethodBodyBuilder(reflectionMethodBodyBuilder.GetCode(), stackSize, dynamicMethod.InitLocals, reflectionMethodBodyBuilder.GetExceptions()).GetCecilMethodBody();
//            Debug.WriteLine(methodBody2);
        }

        private void addLocalVariables()
        {
            var ilGenerator = dynamicMethod.GetILGenerator();

            if (hasReturnType)
            {
                resultLocalIndex = ilGenerator.DeclareLocal(dynamicMethod.ReturnType).LocalIndex;
            }

            ticksLocalIndex = ilGenerator.DeclareLocal(typeof(long)).LocalIndex;
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
    }
}
