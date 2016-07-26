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

    internal class InstructionCollection : Collection<Instruction>
    {
        internal InstructionCollection()
        {
        }

        internal InstructionCollection(int capacity)
            : base(capacity)
        {
        }

        protected override void OnAdd(Instruction item, int index)
        {
            if(index == 0)
                return;

            var previous = items[index - 1];
            previous.Next = item;
            item.Previous = previous;
        }

        protected override void OnInsert(Instruction item, int index)
        {
            if(size == 0)
                return;

            var current = items[index];
            if(current == null)
            {
                var last = items[index - 1];
                last.Next = item;
                item.Previous = last;
                return;
            }

            var previous = current.Previous;
            if(previous != null)
            {
                previous.Next = item;
                item.Previous = previous;
            }

            current.Previous = item;
            item.Next = current;
        }

        protected override void OnSet(Instruction item, int index)
        {
            var current = items[index];

            item.Previous = current.Previous;
            item.Next = current.Next;

            current.Previous = null;
            current.Next = null;
        }

        protected override void OnRemove(Instruction item, int index)
        {
            var previous = item.Previous;
            if(previous != null)
                previous.Next = item.Next;

            var next = item.Next;
            if(next != null)
                next.Previous = item.Previous;

            item.Previous = null;
            item.Next = null;
        }
    }
}