using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using GroboTrace.Mono.Cecil.Metadata;
using GroboTrace.Mono.Cecil.PE;
using GroboTrace.Mono.Collections.Generic;

using ReflectionMethodBody = System.Reflection.MethodBody;

namespace GroboTrace.Mono.Cecil.Cil
{

    // todo: refactor this
    internal class ReflectionMethodBodyConverter : ByteBuffer
    {
        public ReflectionMethodBodyConverter(byte[] code, int stackSize, bool initLocals, CORINFO_EH_CLAUSE[] exceptionClauses)
            :base(code)
        {
            maxStackSize = stackSize;
            this.initLocals = initLocals;
            this.exceptionClauses = exceptionClauses;
        }

        private int Offset { get { return position - 0; } }

        public MethodBody GetCecilMethodBody()
        {
            body = new MethodBody();

            ReadMethodBodyInternal();

            return body;
        }

        private void ReadMethodBodyInternal()
        {
            position = 0;

            body.code_size = buffer.Length;
            body.max_stack_size = maxStackSize;
            body.init_locals = initLocals;

            ReadCode();
            ReadExceptions();
        }

        private void ReadCode()
        {
            var end = body.code_size;
            var instructions = body.instructions = new InstructionCollection((body.code_size + 1) / 2);

            while (position < end)
            {
                var offset = position;
                var opcode = ReadOpCode();
                var current = new Instruction(offset, opcode);

                if (opcode.OperandType != OperandType.InlineNone)
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

        public MetadataToken ReadToken()
        {
            return new MetadataToken(ReadUInt32());
        }

        private void ResolveBranches(Collection<Instruction> instructions)
        {
            var items = instructions.items;
            var size = instructions.size;

            for (int i = 0; i < size; i++)
            {
                var instruction = items[i];
                switch (instruction.opcode.OperandType)
                {
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.InlineBrTarget:
                        instruction.operand = GetInstruction((int)instruction.operand);
                        break;
                    case OperandType.InlineSwitch:
                        var offsets = (int[])instruction.operand;
                        var branches = new Instruction[offsets.Length];
                        for (int j = 0; j < offsets.Length; j++)
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
            if (offset < 0 || offset > items[size - 1].offset)
                return null;

            int min = 0;
            int max = size - 1;
            while (min <= max)
            {
                int mid = min + ((max - min) / 2);
                var instruction = items[mid];
                var instruction_offset = instruction.offset;

                if (offset == instruction_offset)
                    return instruction;

                if (offset < instruction_offset)
                    max = mid - 1;
                else
                    min = mid + 1;
            }

            return null;
        }

        private void ReadExceptions()
        {
            foreach (var exceptionClause in exceptionClauses)
            {
                var handler = new ExceptionHandler((ExceptionHandlerType)exceptionClause.Flags);

                handler.TryStart = GetInstruction(exceptionClause.TryOffset);
                handler.TryEnd = GetInstruction(handler.TryStart.Offset + exceptionClause.TryLength);

                handler.HandlerStart = GetInstruction(exceptionClause.HandlerOffset);
                handler.HandlerEnd = GetInstruction(handler.HandlerStart.Offset + exceptionClause.HandlerLength);

                switch (handler.HandlerType)
                {
                    case ExceptionHandlerType.Catch:
                        handler.CatchType = new MetadataToken((uint)exceptionClause.ClassTokenOrFilterOffset);
                        break;
                    case ExceptionHandlerType.Filter:
                        handler.FilterStart = GetInstruction(exceptionClause.ClassTokenOrFilterOffset);
                        break;
                }

                body.ExceptionHandlers.Add(handler);

            }


        }

        private int maxStackSize;
        private bool initLocals;
        private CORINFO_EH_CLAUSE[] exceptionClauses;

        private MethodBody body;
    }
}
