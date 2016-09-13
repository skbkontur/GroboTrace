using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace GroboTrace.MethodBodyParsing
{
    public class MethodBody
    {
        private MethodBody(byte[] methodSignature, bool resolveTokens)
        {
            Instructions = new InstructionCollection();
            ExceptionHandlers = new Collection<ExceptionHandler>();
            MethodSignature = methodSignature;
            InitLocals = true;
        }

        public static unsafe MethodBody Build(byte* rawMethodBody, Module module, MetadataToken methodSignatureToken, bool resolveTokens)
        {
            var methodSignature = module == null || methodSignatureToken == MetadataToken.Zero
                                         ? new byte[0]
                                         : module.ResolveSignature(methodSignatureToken.ToInt32());
            var body = new MethodBody(methodSignature, resolveTokens);
            new FullMethodBodyReader(rawMethodBody, module).Read(body);
            
            return body;
        }

        private static unsafe MethodBody Build(byte[] code, byte[] methodSignature, int stackSize, bool initLocals, byte[] localSignature, bool resolveTokens)
        {
            var body = new MethodBody(methodSignature, resolveTokens)
                {
                    TemporaryMaxStack = stackSize,
                    InitLocals = initLocals
                };

            body.SetLocalSignature(localSignature);

            fixed(byte* b = &code[0])
                new ILCodeReader(b, code.Length).Read(body);

            return body;
        }

        public static MethodBody Build(MethodBase method, bool resolveTokens)
        {
            var methodBody = method.GetMethodBody();
            var code = methodBody.GetILAsByteArray();
            var stackSize = methodBody.MaxStackSize;
            var initLocals = methodBody.InitLocals;
            var exceptionClauses = methodBody.ExceptionHandlingClauses;

            var localSignature = methodBody.LocalSignatureMetadataToken != 0
                                     ? method.Module.ResolveSignature(methodBody.LocalSignatureMetadataToken)
                                     : SignatureHelper.GetLocalVarSigHelper().GetSignature(); // null is invalid value

            var body = Build(code, method.Module.ResolveSignature(method.MetadataToken), stackSize, initLocals, localSignature, resolveTokens);
            body.ReadExceptions(exceptionClauses);
            return body;
        }

        static MethodBody()
        {
            assembly = typeof(DynamicMethod).Assembly;
            t_DynamicResolver = assembly.GetType("System.Reflection.Emit.DynamicResolver");
            t_DynamicILInfo = assembly.GetType("System.Reflection.Emit.DynamicILInfo");
            t_DynamicILGenerator = assembly.GetType("System.Reflection.Emit.DynamicILGenerator");
            t_DynamicMethod = assembly.GetType("System.Reflection.Emit.DynamicMethod");
            t_DynamicScope = assembly.GetType("System.Reflection.Emit.DynamicScope");
        }

        public static MethodBody Build(DynamicMethod dynamicMethod, bool resolveTokens)
        {
            var dynamicILInfo = t_DynamicMethod.GetField("m_DynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicMethod);
            if(dynamicILInfo != null)
                return Build(dynamicMethod, (DynamicILInfo)dynamicILInfo, resolveTokens);
            var ilGenerator = t_DynamicMethod.GetField("m_ilGenerator", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicMethod);
            if (ilGenerator != null)
                return Build(dynamicMethod, (ILGenerator)ilGenerator, resolveTokens);
            return new MethodBody(new byte[0], resolveTokens);
        }

        private static unsafe MethodBody Build(DynamicMethod dynamicMethod, DynamicILInfo dynamicILInfo, bool resolveTokens)
        {
            var dynamicResolver = t_DynamicResolver
                    .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { t_DynamicILInfo }, null)
                    .Invoke(new object[] { dynamicILInfo });

            byte[] code;
            int stackSize;
            int initLocals;
            int EHCount;

            GetCodeInfo(dynamicResolver, out code, out stackSize, out initLocals, out EHCount);

            var exceptions = GetDynamicILInfoExceptions(dynamicResolver);

            var oldLocalSignature = (byte[])t_DynamicResolver
                    .GetField("m_localSignature", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(dynamicResolver);

            var methodSignatureToken = (int)t_DynamicILInfo
                    .GetField("m_methodSignature", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(dynamicILInfo);

            var m_scope = t_DynamicILInfo.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamicILInfo);

            var rawSignature = (byte[])t_DynamicScope
                    .GetProperty("Item", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(m_scope, new object[] { methodSignatureToken });

            var body = Build(code, rawSignature, stackSize, dynamicMethod.InitLocals, oldLocalSignature, resolveTokens);
            fixed(byte* b = &exceptions[0])
                new ExceptionsInfoReader(b).Read(body);

            UnbindDynamicResolver(dynamicMethod, dynamicResolver);

            return body;
        }

        private static void GetCodeInfo(object dynamicResolver, out byte[] code, out int stackSize, out int initLocals, out int EHCount)
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

        private static byte[] GetDynamicILInfoExceptions(object dynamicResolver)
        {
            var getRawEHInfo = t_DynamicResolver.GetMethod("GetRawEHInfo", BindingFlags.Instance | BindingFlags.NonPublic);

            return (byte[])getRawEHInfo.Invoke(dynamicResolver, Empty<object>.Array);
        }

        private static void UnbindDynamicResolver(DynamicMethod dynamicMethod, object dynamicResolver)
        {
            t_DynamicResolver.GetField("m_method", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dynamicResolver, null);
            typeof(DynamicMethod).GetField("m_resolver", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dynamicMethod, null);
        }

        private static MethodBody Build(DynamicMethod dynamicMethod, ILGenerator ilGenerator, bool resolveTokens)
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

            var methodSignatureToken = (int)t_DynamicILGenerator
                    .GetField("m_methodSigToken", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(ilGenerator);

            var m_scope = t_DynamicILGenerator.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ilGenerator);

            var rawSignature = (byte[])t_DynamicScope
                    .GetProperty("Item", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(m_scope, new object[] { methodSignatureToken });

            var body = Build(code, rawSignature, stackSize, dynamicMethod.InitLocals, oldLocalSignature, resolveTokens);
            body.ReadExceptions(exceptions);

            UnbindDynamicResolver(dynamicMethod, dynamicResolver);

            return body;
        }

        private static unsafe CORINFO_EH_CLAUSE[] GetILGeneratorExceptions(object dynamicResolver, int excCount)
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

        private void ReadExceptions(IList<ExceptionHandlingClause> exceptionClauses)
        {
            foreach(var exceptionClause in exceptionClauses)
            {
                var handler = new ExceptionHandler((ExceptionHandlerType)exceptionClause.Flags);

                handler.TryStart = GetInstruction(exceptionClause.TryOffset);
                handler.TryEnd = GetInstruction(handler.TryStart.Offset + exceptionClause.TryLength);

                handler.HandlerStart = GetInstruction(exceptionClause.HandlerOffset);
                handler.HandlerEnd = GetInstruction(handler.HandlerStart.Offset + exceptionClause.HandlerLength);

                switch(handler.HandlerType)
                {
                case ExceptionHandlerType.Catch:
                    handler.CatchType = new MetadataToken((uint)exceptionClause.CatchType.MetadataToken);
                    break;
                case ExceptionHandlerType.Filter:
                    handler.FilterStart = GetInstruction(exceptionClause.FilterOffset);
                    break;
                }

                ExceptionHandlers.Add(handler);
            }
        }

        private void ReadExceptions(CORINFO_EH_CLAUSE[] exceptionClauses)
        {
            foreach(var exceptionClause in exceptionClauses)
            {
                var handler = new ExceptionHandler((ExceptionHandlerType)exceptionClause.Flags);

                handler.TryStart = GetInstruction(exceptionClause.TryOffset);
                handler.TryEnd = GetInstruction(handler.TryStart.Offset + exceptionClause.TryLength);

                handler.HandlerStart = GetInstruction(exceptionClause.HandlerOffset);
                handler.HandlerEnd = GetInstruction(handler.HandlerStart.Offset + exceptionClause.HandlerLength);

                switch(handler.HandlerType)
                {
                case ExceptionHandlerType.Catch:
                    handler.CatchType = new MetadataToken((uint)exceptionClause.ClassTokenOrFilterOffset);
                    break;
                case ExceptionHandlerType.Filter:
                    handler.FilterStart = GetInstruction(exceptionClause.ClassTokenOrFilterOffset);
                    break;
                }

                ExceptionHandlers.Add(handler);
            }
        }

        private Instruction GetInstruction(int offset)
        {
            return Instructions.GetInstruction(offset);
        }

        public int TemporaryMaxStack { get; set; }

        public void TryCalculateMaxStackSize(Module module)
        {
            TemporaryMaxStack = new MaxStackSizeCalculator(this, module).TryComputeMaxStack();
        }

        public bool InitLocals { get; set; }

        public MetadataToken LocalVarToken { get; set; }

        public byte[] MethodSignature { get; }

        public bool HasExceptionHandlers { get { return !ExceptionHandlers.IsNullOrEmpty(); } }

        public void SetLocalSignature(byte[] localSignature)
        {
            localVarSigBuilder = new LocalVarSigBuilder(localSignature);
        }

        public LocalInfo AddLocalVariable(byte[] signature)
        {
            if(localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.AddLocalVariable(signature);
        }

        public LocalInfo AddLocalVariable(Type localType, bool isPinned = false)
        {
            if(localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.AddLocalVariable(localType, isPinned);
        }

        public byte[] GetLocalSignature()
        {
            if(localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.GetSignature();
        }

        public int LocalVariablesCount()
        {
            if(localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.Count;
        }

        public void Seal()
        {
            Instructions.SimplifyMacros();
            Instructions.OptimizeMacros();

            isSealed = true;
        }

        public byte[] GetILAsByteArray()
        {
            if(!isSealed)
                throw new NotSupportedException("MethodBody has not been sealed");

            return new ILCodeBaker(Instructions).BakeILCode();
        }

        public byte[] GetExceptionsAsByteArray()
        {
            if(!isSealed)
                throw new NotSupportedException("MethodBody has not been sealed");

            return new ExceptionsBaker(ExceptionHandlers, Instructions).BakeExceptions();
        }

        public byte[] GetFullMethodBody(Module module, Func<byte[], MetadataToken> signatureTokenBuilder, int maxStackSize)
        {
            if(!isSealed)
                throw new NotSupportedException("MethodBody has not been sealed");

            return new MethodBodyBaker(module, signatureTokenBuilder, this, maxStackSize).BakeMethodBody();
        }

        public ILProcessor GetILProcessor()
        {
            return new ILProcessor(this);
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine("Instructions:");
            foreach(var instruction in Instructions)
                result.AppendLine(instruction.ToString());

            result.AppendLine();

            result.AppendLine("Exception handlers:");
            foreach(var exceptionHandler in ExceptionHandlers)
                result.AppendLine(exceptionHandler.ToString());

            return result.ToString();
        }

        private bool isSealed;

        public readonly InstructionCollection Instructions;
        public readonly Collection<ExceptionHandler> ExceptionHandlers;

        private LocalVarSigBuilder localVarSigBuilder;
        private static Assembly assembly;
        private static Type t_DynamicResolver;
        private static Type t_DynamicILInfo;
        private static Type t_DynamicILGenerator;
        private static Type t_DynamicMethod;
        private static Type t_DynamicScope;
    }
}