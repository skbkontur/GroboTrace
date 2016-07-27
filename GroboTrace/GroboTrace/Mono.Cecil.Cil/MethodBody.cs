using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

using GroboTrace.Mono.Cecil.Metadata;
using GroboTrace.Mono.Collections.Generic;

namespace GroboTrace.Mono.Cecil.Cil
{
    public sealed class MethodBody
    {
        public MethodBody()
        {
            Instructions = new InstructionCollection();
            ExceptionHandlers = new Collection<ExceptionHandler>();
            InitLocals = true;
        }

        public int TemporaryMaxStack { get; set; }

        public void TryCalculateMaxStackSize(Module module)
        {
            TemporaryMaxStack = new MaxStackSizeCalculator(this, module).TryComputeMaxStack();
        }

        public bool InitLocals { get; set; }

        public MetadataToken LocalVarToken { get; set; }

        public bool HasExceptionHandlers { get { return !ExceptionHandlers.IsNullOrEmpty(); } }
        
       

        public void SetLocalSignature(byte[] localSignature)
        {
            localVarSigBuilder = new LocalVarSigBuilder(localSignature);
        }

        public LocalInfo AddLocalVariable(byte[] signature)
        {
            if (localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.AddLocalVariable(signature);
        }

        public LocalInfo AddLocalVariable(Type localType, bool isPinned = false)
        {
            if (localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.AddLocalVariable(localType, isPinned);
        }

        public byte[] GetLocalSignature()
        {
            if (localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.GetSignature();
        }

        public int LocalVariablesCount()
        {
            if (localVarSigBuilder == null)
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
            if (!isSealed)
                throw new NotSupportedException("MethodBody has not been sealed");

            return new ILCodeBaker(Instructions).BakeILCode();
        }

        public byte[] GetExceptionsAsByteArray()
        {
            if (!isSealed)
                throw new NotSupportedException("MethodBody has not been sealed");

            return new ExceptionsBaker(ExceptionHandlers, Instructions).BakeExceptions();
        }


        public byte[] GetFullMethodBody(Module module, Func<byte[], MetadataToken> signatureTokenBuilder, int maxStackSize)
        {
            if (!isSealed)
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
            foreach (var instruction in Instructions)
            {
                result.AppendLine(instruction.ToString());
            }

            result.AppendLine();

            result.AppendLine("Exception handlers:");
            foreach (var exceptionHandler in ExceptionHandlers)
            {
                result.AppendLine(exceptionHandler.ToString());
            }

            return result.ToString();
        }

        public bool isTiny;
        private bool isSealed;

        public readonly Collection<Instruction> Instructions;
        public readonly Collection<ExceptionHandler> ExceptionHandlers;
        
        private LocalVarSigBuilder localVarSigBuilder;
        
    }
}