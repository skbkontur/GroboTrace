using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

using GroboTrace.MethodBodyParsing;

using MethodBody = GroboTrace.MethodBodyParsing.MethodBody;
using ExceptionHandler = GroboTrace.MethodBodyParsing.ExceptionHandler;
using OpCodes = GroboTrace.MethodBodyParsing.OpCodes;

namespace GroboTrace
{
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
            t_DynamicResolver = mscorlib.GetType("System.Reflection.Emit.DynamicResolver");
            t_DynamicILInfo = mscorlib.GetType("System.Reflection.Emit.DynamicILInfo");
            t_DynamicILGenerator = mscorlib.GetType("System.Reflection.Emit.DynamicILGenerator");
            t_DynamicMethod = mscorlib.GetType("System.Reflection.Emit.DynamicMethod");
            t_DynamicScope = mscorlib.GetType("System.Reflection.Emit.DynamicScope");

            ticksReaderAddress = Zzz.ticksReaderAddress;
            methodStartedAddress = Zzz.methodStartedAddress;
            methodFinishedAddress = Zzz.methodFinishedAddress;
        }

        private void Extend()
        {
            if(Zzz.tracedMethods.ContainsKey(dynamicMethod))
                return;
            lock(dynamicMethod)
            {
                if(Zzz.tracedMethods.ContainsKey(dynamicMethod))
                    return;
                ExtendInternal();
                Zzz.tracedMethods.TryAdd(dynamicMethod, 0);
            }
        }

        private void ExtendInternal()
        {
            var methodBody = MethodBody.Read(dynamicMethod, false);

            bool output = true;

            if(output) Debug.WriteLine("");
            if (output) Debug.WriteLine("Initial methodBody of DynamicMethod");
            if (output) Debug.WriteLine(methodBody);

            var methodContainsCycles = CycleFinderWithoutRecursion.HasCycle(methodBody.Instructions.ToArray());
            if (output) Debug.WriteLine("Contains cycles: " + methodContainsCycles + "\n");

            if(!methodContainsCycles && methodBody.Instructions.Count < 50)
            {
                Debug.WriteLine(dynamicMethod + " too simple to be traced");
                return;
            }

            AddLocalVariables(methodBody);

            int functionId;
            Zzz.AddMethod(dynamicMethod, out functionId);

            ModifyMethodBody(methodBody, functionId);

            methodBody.WriteToDynamicMethod(dynamicMethod, Math.Max(methodBody.MaxStack, 3));

            if (output) Debug.WriteLine("");
            if (output) Debug.WriteLine("Changed methodBody of DynamicMethod");
            if (output) Debug.WriteLine(methodBody);
        }

        private void AddLocalVariables(MethodBody methodBody)
        {
            var rawSignature = methodBody.MethodSignature;

            var methodSignature = new SignatureReader(rawSignature).ReadAndParseMethodSignature();
            
            if (hasReturnType)
            {
                resultLocalIndex = methodBody.AddLocalVariable(methodSignature.ReturnTypeSignature).LocalIndex;
            }

            ticksLocalIndex = methodBody.AddLocalVariable(typeof(long)).LocalIndex;
        }

        private void ModifyMethodBody(MethodBody methodBody, int functionId)
        {
            Zzz.ReplaceRetInstructions(methodBody.Instructions, hasReturnType, resultLocalIndex);

            var ticksReaderSignature = typeof(Zzz).Module.ResolveSignature(typeof(Zzz).GetMethod("TemplateForTicksSignature", BindingFlags.Public | BindingFlags.Static).MetadataToken);

            var methodStartedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MetadataToken);

            var methodFinishedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MetadataToken);

            int startIndex = 0;

            methodBody.Instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress));
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, ticksReaderSignature));
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Stloc, ticksLocalIndex));

            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldc_I4, (int)functionId)); // [ ourMethod, functionId ]
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodStartedAddress : (long)methodStartedAddress)); // [ ourMethod, functionId, funcAddr ]
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, methodStartedSignature)); // []

            var tryStartInstruction = methodBody.Instructions[startIndex];

            Instruction tryEndInstruction;
            Instruction finallyStartInstruction;

            methodBody.Instructions.Insert(methodBody.Instructions.Count, finallyStartInstruction = tryEndInstruction = Instruction.Create(OpCodes.Ldc_I4, (int)functionId)); // [ functionId ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress)); // [ functionId, funcAddr ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Calli, ticksReaderSignature)); // [ functionId, ticks ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Ldloc, ticksLocalIndex)); // [ functionId, ticks, startTicks ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Sub)); // [ functionId, elapsed ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodFinishedAddress : (long)methodFinishedAddress)); // [ functionId, elapsed, profilerOverhead , funcAddr ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Calli, methodFinishedSignature)); // []

            Instruction endFinallyInstruction;
            methodBody.Instructions.Insert(methodBody.Instructions.Count, endFinallyInstruction = Instruction.Create(OpCodes.Endfinally));

            Instruction finallyEndInstruction;

            if (resultLocalIndex >= 0)
            {
                methodBody.Instructions.Insert(methodBody.Instructions.Count, finallyEndInstruction = Instruction.Create(OpCodes.Ldloc, resultLocalIndex));
                methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Ret));
            }
            else
            {
                methodBody.Instructions.Insert(methodBody.Instructions.Count, finallyEndInstruction = Instruction.Create(OpCodes.Ret));
            }

            ExceptionHandler newException = new ExceptionHandler(ExceptionHandlerType.Finally);
            newException.TryStart = tryStartInstruction;
            newException.TryEnd = tryEndInstruction;
            newException.HandlerStart = finallyStartInstruction;
            newException.HandlerEnd = finallyEndInstruction;

            methodBody.Instructions.Insert(methodBody.Instructions.IndexOf(tryEndInstruction), Instruction.Create(OpCodes.Leave, finallyEndInstruction));

            methodBody.ExceptionHandlers.Add(newException);
        }
        

        private DynamicMethod dynamicMethod;
        private bool hasReturnType;

        private Assembly mscorlib;
        private readonly Type t_DynamicResolver;
        private readonly Type t_DynamicILInfo;
        private readonly Type t_DynamicILGenerator;
        private readonly Type t_DynamicMethod;
        private readonly Type t_DynamicScope;


        private static IntPtr ticksReaderAddress;
        private static IntPtr methodStartedAddress;
        private static IntPtr methodFinishedAddress;


        int resultLocalIndex = -1;
        int ticksLocalIndex;
    }
}
