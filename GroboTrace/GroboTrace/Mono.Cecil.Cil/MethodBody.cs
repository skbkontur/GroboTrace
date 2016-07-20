//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using System;
using System.Text;

using GroboTrace.Mono.Cecil.Metadata;
using GroboTrace.Mono.Collections.Generic;

namespace GroboTrace.Mono.Cecil.Cil
{
    public sealed class MethodBody
    {
        public int MaxStackSize { get; set; }

        public int CodeSize { get { return code_size; } } // todo: refactor: подсчет codesiz'а

        public bool InitLocals { get; set; }

        public MetadataToken LocalVarToken { get; set; }

        public Collection<Instruction> Instructions { get { return instructions ?? (instructions = new InstructionCollection()); } }

        public bool HasExceptionHandlers { get { return !exceptions.IsNullOrEmpty(); } }

        public Collection<ExceptionHandler> ExceptionHandlers { get { return exceptions ?? (exceptions = new Collection<ExceptionHandler>()); } }

        public bool HasVariables { get { return variablesCount > 0; } }

        public byte[] VariablesSignature { get { return variablesSignature ?? (variablesSignature = new byte[0]); } set { variablesSignature = value; } }

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

        //internal int max_stack_size;
        internal int code_size;
        //internal bool init_locals;
        //internal MetadataToken local_var_token;

        public bool isTiny;

        internal Collection<Instruction> instructions;
        internal Collection<ExceptionHandler> exceptions;
        private byte[] variablesSignature;
        internal uint variablesCount;
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