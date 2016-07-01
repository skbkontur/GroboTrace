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
using System.Reflection;

using GroboTrace.Mono.Cecil.Metadata;
using GroboTrace.Mono.Cecil.PE;
using GroboTrace.Mono.Collections.Generic;

namespace GroboTrace.Mono.Cecil.Cil
{
    internal sealed unsafe class CodeReader : RawByteBuffer
    {
        public CodeReader(byte* data, Module module)
            : base(data)
        {
            this.module = module;
        }

        private int Offset { get { return position - start; } }

        public MethodBody ReadMethodBody()
        {
            body = new MethodBody();

            ReadMethodBodyInternal();

            return body;
        }

        private void ReadMethodBodyInternal()
        {
            position = 0;

            var flags = ReadByte();
            switch(flags & 0x3)
            {
            case 0x2: // tiny
                body.code_size = flags >> 2;
                body.MaxStackSize = 8;
                ReadCode();
                break;
            case 0x3: // fat
                base.position--;
                ReadFatMethod();
                break;
            default:
                throw new InvalidOperationException();
            }
        }

        private void ReadFatMethod()
        {
            var flags = ReadUInt16();
            body.max_stack_size = ReadUInt16();
            body.code_size = (int)ReadUInt32();
            body.local_var_token = new MetadataToken(ReadUInt32());
            body.init_locals = (flags & 0x10) != 0;

            if(body.local_var_token.RID != 0)
                ReadVariables(body.local_var_token);

            ReadCode();

            if((flags & 0x8) != 0)
                ReadSection();
        }

        public void ReadVariables(MetadataToken local_var_token)
        {
            var signature = module.ResolveSignature(local_var_token.ToInt32());
            var reader = new ByteBuffer(signature);

            const byte local_sig = 0x7;

            if(reader.ReadByte() != local_sig)
                throw new NotSupportedException();

            var count = reader.ReadCompressedUInt32();
            body.variablesCount = count;
            if(count == 0)
                return;

            body.variablesSignature = new byte[signature.Length - reader.position];
            Array.Copy(signature, reader.position, body.variablesSignature, 0, body.variablesSignature.Length);
        }

        private void ReadCode()
        {
            start = position;
            var code_size = body.code_size;

            if(code_size < 0 /* || buffer.Length <= (uint) (code_size + position)*/)
                code_size = 0;

            var end = start + code_size;
            var instructions = body.instructions = new InstructionCollection((code_size + 1) / 2);

            while(position < end)
            {
                var offset = base.position - start;
                var opcode = ReadOpCode();
                var current = new Instruction(offset, opcode);

                if(opcode.OperandType != OperandType.InlineNone)
                    current.operand = ReadOperand(current);

                instructions.Add(current);
            }

            ResolveBranches(instructions);
        }

        private OpCode ReadOpCode()
        {
            var il_opcode = ReadByte();
            return il_opcode != 0xfe
                       ? OpCodes.OneByteOpCode[il_opcode]
                       : OpCodes.TwoBytesOpCode[ReadByte()];
        }

        private object ReadOperand(Instruction instruction)
        {
            switch(instruction.opcode.OperandType)
            {
            case OperandType.InlineSwitch:
                var length = ReadInt32();
                var base_offset = Offset + (4 * length);
                var branches = new int[length];
                for(int i = 0; i < length; i++)
                    branches[i] = base_offset + ReadInt32();
                return branches;
            case OperandType.ShortInlineBrTarget:
                return ReadSByte() + Offset;
            case OperandType.InlineBrTarget:
                return ReadInt32() + Offset;
            case OperandType.ShortInlineI:
                if(instruction.opcode == OpCodes.Ldc_I4_S)
                    return ReadSByte();

                return ReadByte();
            case OperandType.InlineI:
                return ReadInt32();
            case OperandType.ShortInlineR:
                return ReadSingle();
            case OperandType.InlineR:
                return ReadDouble();
            case OperandType.InlineI8:
                return ReadInt64();
            case OperandType.ShortInlineVar:
                return (int)ReadByte();
            case OperandType.InlineVar:
                return (int)ReadUInt16();
            case OperandType.ShortInlineArg:
                return (int)ReadByte();
            case OperandType.InlineArg:
                return (int)ReadUInt16();
            case OperandType.InlineSig:
                return ReadToken();
            case OperandType.InlineString:
                return ReadToken();
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.InlineMethod:
            case OperandType.InlineField:
                return ReadToken();
            default:
                throw new NotSupportedException();
            }
        }

        private void ResolveBranches(Collection<Instruction> instructions)
        {
            var items = instructions.items;
            var size = instructions.size;

            for(int i = 0; i < size; i++)
            {
                var instruction = items[i];
                switch(instruction.opcode.OperandType)
                {
                case OperandType.ShortInlineBrTarget:
                case OperandType.InlineBrTarget:
                    instruction.operand = GetInstruction((int)instruction.operand);
                    break;
                case OperandType.InlineSwitch:
                    var offsets = (int[])instruction.operand;
                    var branches = new Instruction[offsets.Length];
                    for(int j = 0; j < offsets.Length; j++)
                        branches[j] = GetInstruction(offsets[j]);

                    instruction.operand = branches;
                    break;
                }
            }
        }

        private Instruction GetInstruction(int offset)
        {
            return GetInstruction(body.Instructions, offset);
        }

        private static Instruction GetInstruction(Collection<Instruction> instructions, int offset)
        {
            var size = instructions.size;
            var items = instructions.items;
            if(offset < 0 || offset > items[size - 1].offset)
                return null;

            int min = 0;
            int max = size - 1;
            while(min <= max)
            {
                int mid = min + ((max - min) / 2);
                var instruction = items[mid];
                var instruction_offset = instruction.offset;

                if(offset == instruction_offset)
                    return instruction;

                if(offset < instruction_offset)
                    max = mid - 1;
                else
                    min = mid + 1;
            }

            return null;
        }

        private void ReadSection()
        {
            Align(4);

            const byte fat_format = 0x40;
            const byte more_sects = 0x80;

            var flags = ReadByte();
            if((flags & fat_format) == 0)
                ReadSmallSection();
            else
                ReadFatSection();

            if((flags & more_sects) != 0)
                ReadSection();
        }

        private void ReadSmallSection()
        {
            var count = ReadByte() / 12;
            Advance(2);

            ReadExceptionHandlers(
                count,
                () => (int)ReadUInt16(),
                () => (int)ReadByte());
        }

        private void ReadFatSection()
        {
            position--;
            var count = (ReadInt32() >> 8) / 24;

            ReadExceptionHandlers(
                count,
                ReadInt32,
                ReadInt32);
        }

        // inline ?
        private void ReadExceptionHandlers(int count, Func<int> read_entry, Func<int> read_length)
        {
            for(int i = 0; i < count; i++)
            {
                var handler = new ExceptionHandler(
                    (ExceptionHandlerType)(read_entry() & 0x7));

                handler.TryStart = GetInstruction(read_entry());
                handler.TryEnd = GetInstruction(handler.TryStart.Offset + read_length());

                handler.HandlerStart = GetInstruction(read_entry());
                handler.HandlerEnd = GetInstruction(handler.HandlerStart.Offset + read_length());

                ReadExceptionHandlerSpecific(handler);

                this.body.ExceptionHandlers.Add(handler);
            }
        }

        private void ReadExceptionHandlerSpecific(ExceptionHandler handler)
        {
            switch(handler.HandlerType)
            {
            case ExceptionHandlerType.Catch:
                handler.CatchType = ReadToken();
                break;
            case ExceptionHandlerType.Filter:
                handler.FilterStart = GetInstruction(ReadInt32());
                break;
            default:
                Advance(4);
                break;
            }
        }

        private void Align(int align)
        {
            align--;
            Advance(((position + align) & ~align) - position);
        }

        public MetadataToken ReadToken()
        {
            return new MetadataToken(ReadUInt32());
        }

        private readonly Module module;

        private int start;

        private MethodBody body;
    }
}