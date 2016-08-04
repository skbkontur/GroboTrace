using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

using GroboTrace.MethodBodyParsing;

using CecilMethodBody = GroboTrace.MethodBodyParsing.MethodBody;
using ExceptionHandler = GroboTrace.MethodBodyParsing.ExceptionHandler;
using OpCodes = GroboTrace.MethodBodyParsing.OpCodes;

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
            var dynamicILInfo = t_DynamicMethod.GetField("m_DynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicMethod);
            var ilGenerator = t_DynamicMethod.GetField("m_ilGenerator", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicMethod);

            if (dynamicILInfo != null)
            {
                ExtendFromDynamicILInfo((DynamicILInfo)dynamicILInfo);
            }
            else if (ilGenerator != null)
            {
                ExtendFromILGenerator((ILGenerator)ilGenerator);
            }
        }

        private void ExtendFromDynamicILInfo(DynamicILInfo oldDynamicILInfo)
        {
            var dynamicResolver = t_DynamicResolver
                    .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { t_DynamicILInfo }, null)
                    .Invoke(new object[] { oldDynamicILInfo });

            byte[] code;
            int stackSize;
            int initLocals;
            int EHCount;

            GetCodeInfo(dynamicResolver, out code, out stackSize, out initLocals, out EHCount);

            var exceptions = GetDynamicILInfoExceptions(dynamicResolver);

            var oldLocalSignature = (byte[])t_DynamicResolver
                    .GetField("m_localSignature", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(dynamicResolver);

            var methodBody = new CecilMethodBodyBuilder(code, stackSize, dynamicMethod.InitLocals, oldLocalSignature, exceptions).GetCecilMethodBody();
            
            UnbindDynamicResolver(dynamicResolver);

            Debug.WriteLine("");
            Debug.WriteLine("Initial methodBody of DynamicMethod");
            Debug.WriteLine(methodBody);

            //            var methodContainsCycles = new CycleFinder(methodBody.Instructions.ToArray()).IsThereAnyCycles();
            //            if (methodContainsCycles != CycleFinderWithoutRecursion.HasCycle(methodBody.Instructions.ToArray()))
            //                throw new InvalidOperationException("BUGBUGBUG");

            var methodContainsCycles = CycleFinderWithoutRecursion.HasCycle(methodBody.Instructions.ToArray());
            Debug.WriteLine("Contains cycles: " + methodContainsCycles + "\n");

            //     if(methodBody.isTiny || !methodContainsCycles && methodBody.Instructions.Count < 50)
            //     {
            //         Debug.WriteLine(dynamicMethod + " too simple to be traced");
            //         return;
            //     }

            AddLocalVariables(methodBody, oldDynamicILInfo);
            
            var scope = t_DynamicILInfo.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(oldDynamicILInfo);

            DynamicILInfo newDynamicILInfo = (DynamicILInfo)t_DynamicMethod
                                                             .GetMethod("GetDynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic)
                                                             .Invoke(dynamicMethod, new[] { scope });
            
            int functionId;
            Zzz.AddMethod(dynamicMethod, out functionId);

            ModifyMethodBody(methodBody, newDynamicILInfo, functionId);
            
            methodBody.Seal();

            newDynamicILInfo.SetCode(methodBody.GetILAsByteArray(), Math.Max(stackSize, 3));

            if (methodBody.HasExceptionHandlers)
                newDynamicILInfo.SetExceptions(methodBody.GetExceptionsAsByteArray());

            newDynamicILInfo.SetLocalSignature(methodBody.GetLocalSignature());

            Debug.WriteLine("");
            Debug.WriteLine("Changed methodBody of DynamicMethod");
            Debug.WriteLine(methodBody);

        }


        private void ExtendFromILGenerator(ILGenerator ilGenerator)
        {
            var dynamicResolver = t_DynamicResolver
                    .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { t_DynamicILGenerator }, null)
                    .Invoke(new object[] { ilGenerator });

            byte[] code;
            int stackSize;
            int initLocals;
            int EHCount;

            GetCodeInfo(dynamicResolver, out code, out stackSize, out initLocals, out EHCount);

            var exceptions = GetILGeneratorExceptions(dynamicResolver, EHCount);

            var oldLocalSignature = (byte[])t_DynamicResolver
                    .GetField("m_localSignature", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(dynamicResolver);
            
            var methodBody = new CecilMethodBodyBuilder(code, stackSize, dynamicMethod.InitLocals, oldLocalSignature, exceptions).GetCecilMethodBody();

            UnbindDynamicResolver(dynamicResolver);

            Debug.WriteLine("");
            Debug.WriteLine("Initial methodBody of DynamicMethod");
            Debug.WriteLine(methodBody);

//            var methodContainsCycles = new CycleFinder(methodBody.Instructions.ToArray()).IsThereAnyCycles();
//            if (methodContainsCycles != CycleFinderWithoutRecursion.HasCycle(methodBody.Instructions.ToArray()))
//                throw new InvalidOperationException("BUGBUGBUG");

            var methodContainsCycles = CycleFinderWithoutRecursion.HasCycle(methodBody.Instructions.ToArray());
            Debug.WriteLine("Contains cycles: " + methodContainsCycles + "\n");

            //     if(methodBody.isTiny || !methodContainsCycles && methodBody.Instructions.Count < 50)
            //     {
            //         Debug.WriteLine(dynamicMethod + " too simple to be traced");
            //         return;
            //     }


            AddLocalVariables(methodBody, ilGenerator);

            var scope = t_DynamicILGenerator.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ilGenerator);

            DynamicILInfo newDynamicILInfo = (DynamicILInfo)t_DynamicMethod
                                                             .GetMethod("GetDynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic)
                                                             .Invoke(dynamicMethod, new[] { scope });

            int functionId;
            Zzz.AddMethod(dynamicMethod, out functionId);

            ModifyMethodBody(methodBody, newDynamicILInfo, functionId);


            methodBody.Seal();

            newDynamicILInfo.SetCode(methodBody.GetILAsByteArray(), Math.Max(stackSize, 3));

            if (methodBody.HasExceptionHandlers)
                newDynamicILInfo.SetExceptions(methodBody.GetExceptionsAsByteArray());

            newDynamicILInfo.SetLocalSignature(methodBody.GetLocalSignature());

            Debug.WriteLine("");
            Debug.WriteLine("Changed methodBody of DynamicMethod");
            Debug.WriteLine(methodBody);
        }


        private void GetCodeInfo(object dynamicResolver, out byte[] code, out int stackSize, out int initLocals, out int EHCount)
        {
            var getCodeInfo = t_DynamicResolver.GetMethod("GetCodeInfo", BindingFlags.Instance | BindingFlags.NonPublic);

            stackSize = 0;
            initLocals = 0;
            EHCount = 0;

            var parameters = new object[] { stackSize, initLocals, EHCount };

            code = (byte[])getCodeInfo.Invoke(dynamicResolver, parameters);

            stackSize = (int)parameters[0];
            initLocals = (int)parameters[1];
            EHCount = (int)parameters[2];
        }

        private byte[] GetDynamicILInfoExceptions(object dynamicResolver)
        {
            var getRawEHInfo = t_DynamicResolver.GetMethod("GetRawEHInfo", BindingFlags.Instance | BindingFlags.NonPublic);

            return (byte[])getRawEHInfo.Invoke(dynamicResolver, Empty<object>.Array);
        }
        

        private unsafe CORINFO_EH_CLAUSE[] GetILGeneratorExceptions(object dynamicResolver, int excCount)
        {
            var getEHInfo = t_DynamicResolver.GetMethod("GetEHInfo", BindingFlags.Instance | BindingFlags.NonPublic);

            var exceptions = new CORINFO_EH_CLAUSE[excCount];
            
            for (int i = 0; i < excCount; ++i)
                fixed (CORINFO_EH_CLAUSE* pointer = &exceptions[i])
                {
                    getEHInfo.Invoke(dynamicResolver, new object[] { i, (IntPtr)pointer });
                }

            return exceptions;
        }


        private void UnbindDynamicResolver(object dynamicResolver)
        {
            t_DynamicResolver.GetField("m_method", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dynamicResolver, null);
            typeof(DynamicMethod).GetField("m_resolver", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dynamicMethod, null);
        }


        private void AddLocalVariables(CecilMethodBody methodBody, DynamicILInfo dynamicILInfo)
        {
            var methodSignatureToken = (int)t_DynamicILInfo
                    .GetField("m_methodSignature", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(dynamicILInfo);

            var m_scope = t_DynamicILInfo.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicILInfo);

            var rawSignature = (byte[])t_DynamicScope
                    .GetProperty("Item", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(m_scope, new object[] { methodSignatureToken });

            var methodSignature = new SignatureReader(rawSignature).ReadAndParseMethodSignature();
            
            if (hasReturnType)
            {
                resultLocalIndex = methodBody.AddLocalVariable(methodSignature.ReturnTypeSignature).LocalIndex;
            }

            ticksLocalIndex = methodBody.AddLocalVariable(typeof(long)).LocalIndex;
        }

        private void AddLocalVariables(CecilMethodBody methodBody, ILGenerator ilGenerator)
        {
            var methodSignatureToken = (int)t_DynamicILGenerator
                    .GetField("m_methodSigToken", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(ilGenerator);

            var m_scope = t_DynamicILGenerator.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ilGenerator);

            var rawSignature = (byte[])t_DynamicScope
                    .GetProperty("Item", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(m_scope, new object[] { methodSignatureToken });
            
            var methodSignature = new SignatureReader(rawSignature).ReadAndParseMethodSignature();

            if (hasReturnType)
            {
                resultLocalIndex = methodBody.AddLocalVariable(methodSignature.ReturnTypeSignature).LocalIndex;
            }

            ticksLocalIndex = methodBody.AddLocalVariable(typeof(long)).LocalIndex;

        }
        

        //private void AddLocalVariables0(CecilMethodBody methodBody, ILGenerator ilGenerator)
        //{
        //    if (hasReturnType)
        //    {
        //        resultLocalIndex = ilGenerator.DeclareLocal(dynamicMethod.ReturnType).LocalIndex;
        //    }

        //    ticksLocalIndex = ilGenerator.DeclareLocal(typeof(long)).LocalIndex;

        //    var dynamicResolver = t_DynamicResolver
        //            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { t_DynamicILGenerator }, null)
        //            .Invoke(new object[] { ilGenerator });

        //    var newLocalSignature = (byte[])t_DynamicResolver
        //            .GetField("m_localSignature", BindingFlags.Instance | BindingFlags.NonPublic)
        //            .GetValue(dynamicResolver);

        //    UnbindDynamicResolver(dynamicResolver);

        //    //methodBody.SetLocalSignature(newLocalSignature);
        //    Console.WriteLine("using DeclareLocal " + string.Join(", ", newLocalSignature));
        //}

        private void ModifyMethodBody(CecilMethodBody methodBody, DynamicILInfo newDynamicILInfo, int functionId)
        {
            Zzz.ReplaceRetInstructions(methodBody.Instructions, hasReturnType, resultLocalIndex);

            var ticksReaderSignature = typeof(Zzz).Module.ResolveSignature(typeof(Zzz).GetMethod("TemplateForTicksSignature", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var ticksReaderToken = new MetadataToken((uint)newDynamicILInfo.GetTokenFor(ticksReaderSignature));

            var methodStartedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var methodStartedToken = new MetadataToken((uint)newDynamicILInfo.GetTokenFor(methodStartedSignature));

            var methodFinishedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var methodFinishedToken = new MetadataToken((uint)newDynamicILInfo.GetTokenFor(methodFinishedSignature));

            int startIndex = 0;

            methodBody.Instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress));
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, ticksReaderToken));
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Stloc, ticksLocalIndex));

            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldc_I4, (int)functionId)); // [ ourMethod, functionId ]
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodStartedAddress : (long)methodStartedAddress)); // [ ourMethod, functionId, funcAddr ]
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, methodStartedToken)); // []

            var tryStartInstruction = methodBody.Instructions[startIndex];

            Instruction tryEndInstruction;
            Instruction finallyStartInstruction;

            methodBody.Instructions.Insert(methodBody.Instructions.Count, finallyStartInstruction = tryEndInstruction = Instruction.Create(OpCodes.Ldc_I4, (int)functionId)); // [ functionId ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress)); // [ functionId, funcAddr ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Calli, ticksReaderToken)); // [ functionId, ticks ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Ldloc, ticksLocalIndex)); // [ functionId, ticks, startTicks ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Sub)); // [ functionId, elapsed ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodFinishedAddress : (long)methodFinishedAddress)); // [ functionId, elapsed, profilerOverhead , funcAddr ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Calli, methodFinishedToken)); // []

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
