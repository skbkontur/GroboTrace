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

namespace GroboTrace.MethodBodyParsing
{
    public sealed class ILProcessor
    {
        internal ILProcessor(MethodBody body)
        {
            this.Body = body;
            this.instructions = body.Instructions;
        }

        public MethodBody Body { get; }

        public Instruction Create(OpCode opcode)
        {
            return Instruction.Create(opcode);
        }

        public Instruction Create(OpCode opcode, MetadataToken token)
        {
            return Instruction.Create(opcode, token);
        }

        public Instruction Create(OpCode opcode, sbyte value)
        {
            return Instruction.Create(opcode, value);
        }

        public Instruction Create(OpCode opcode, byte value)
        {
            return Instruction.Create(opcode, value);
        }

        public Instruction Create(OpCode opcode, int value)
        {
            return Instruction.Create(opcode, value);
        }

        public Instruction Create(OpCode opcode, long value)
        {
            return Instruction.Create(opcode, value);
        }

        public Instruction Create(OpCode opcode, float value)
        {
            return Instruction.Create(opcode, value);
        }

        public Instruction Create(OpCode opcode, double value)
        {
            return Instruction.Create(opcode, value);
        }

        public Instruction Create(OpCode opcode, Instruction target)
        {
            return Instruction.Create(opcode, target);
        }

        public Instruction Create(OpCode opcode, Instruction[] targets)
        {
            return Instruction.Create(opcode, targets);
        }

        public void Emit(OpCode opcode)
        {
            Append(Create(opcode));
        }

        public void Emit(OpCode opcode, MetadataToken token)
        {
            Append(Create(opcode, token));
        }

        public void Emit(OpCode opcode, byte value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, sbyte value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, int value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, long value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, float value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, double value)
        {
            Append(Create(opcode, value));
        }

        public void Emit(OpCode opcode, Instruction target)
        {
            Append(Create(opcode, target));
        }

        public void Emit(OpCode opcode, Instruction[] targets)
        {
            Append(Create(opcode, targets));
        }

        public void InsertBefore(Instruction target, Instruction instruction)
        {
            if(target == null)
                throw new ArgumentNullException("target");
            if(instruction == null)
                throw new ArgumentNullException("instruction");

            var index = instructions.IndexOf(target);
            if(index == -1)
                throw new ArgumentOutOfRangeException("target");

            instructions.Insert(index, instruction);
        }

        public void InsertAfter(Instruction target, Instruction instruction)
        {
            if(target == null)
                throw new ArgumentNullException("target");
            if(instruction == null)
                throw new ArgumentNullException("instruction");

            var index = instructions.IndexOf(target);
            if(index == -1)
                throw new ArgumentOutOfRangeException("target");

            instructions.Insert(index + 1, instruction);
        }

        public void Append(Instruction instruction)
        {
            if(instruction == null)
                throw new ArgumentNullException("instruction");

            instructions.Add(instruction);
        }

        public void Replace(Instruction target, Instruction instruction)
        {
            if(target == null)
                throw new ArgumentNullException("target");
            if(instruction == null)
                throw new ArgumentNullException("instruction");

            InsertAfter(target, instruction);
            Remove(target);
        }

        public void Remove(Instruction instruction)
        {
            if(instruction == null)
                throw new ArgumentNullException("instruction");

            if(!instructions.Remove(instruction))
                throw new ArgumentOutOfRangeException("instruction");
        }

        private readonly Collection<Instruction> instructions;
    }
}