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
    internal class CecilMethodBodyBuilder : ByteBuffer
    {
        private CecilMethodBodyBuilder(byte[] code, int stackSize, bool initLocals, byte[] localSignature)
            :base(code)
        {
            body = new MethodBody();

            codeSize = code.Length;
            position = 0;
            
            body.TemporaryMaxStack = stackSize;
            body.InitLocals = initLocals;

            body.SetLocalSignature(localSignature);

            ReadCode();
        }


        public CecilMethodBodyBuilder(byte[] code, int stackSize, bool initLocals, byte[] localSignature, CORINFO_EH_CLAUSE[] exceptionClauses)
            : this(code, stackSize, initLocals, localSignature)
        {
            ReadExceptions(exceptionClauses);
        }


        public CecilMethodBodyBuilder(byte[] code, int stackSize, bool initLocals, byte[] localSignature, byte[] exceptions)
            : this(code, stackSize, initLocals, localSignature)
        {

            exceptionsBytes = new ByteBuffer(exceptions);

            if (exceptionsBytes.length > 0)
                ReadExceptionsFromBytes();

        }

        public CecilMethodBodyBuilder(byte[] code, int stackSize, bool initLocals, byte[] localSignature, IList<ExceptionHandlingClause> exceptionClauses)
            : this(code, stackSize, initLocals, localSignature)
        {
            ReadExceptions(exceptionClauses);
        }




        private int Offset { get { return position - 0; } }

        public MethodBody GetCecilMethodBody()
        {
            return body;
        }

        private void ReadCode()
        {
            var end = codeSize;
            var instructions = body.Instructions;

            while (position < end)
            {
                var offset = position;
                var opcode = ReadOpCode();
                var current = new Instruction(offset, opcode);

                if (opcode.OperandType != OperandType.InlineNone)
                    current.Operand = ReadOperand(current);

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
            switch(instruction.OpCode.OperandType)
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
                if(instruction.OpCode == OpCodes.Ldc_I4_S)
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
                return ReadToken(this);
            case OperandType.InlineString:
                return ReadToken(this);
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.InlineMethod:
            case OperandType.InlineField:
                return ReadToken(this);
            default:
                throw new NotSupportedException();
            }
        }

        public MetadataToken ReadToken(ByteBuffer buffer)
        {
            return new MetadataToken(buffer.ReadUInt32());
        }

        private void ResolveBranches(Collection<Instruction> instructions)
        {
            var items = instructions.items;
            var size = instructions.size;

            for (int i = 0; i < size; i++)
            {
                var instruction = items[i];
                switch (instruction.OpCode.OperandType)
                {
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.InlineBrTarget:
                        instruction.Operand = GetInstruction((int)instruction.Operand);
                        break;
                    case OperandType.InlineSwitch:
                        var offsets = (int[])instruction.Operand;
                        var branches = new Instruction[offsets.Length];
                        for (int j = 0; j < offsets.Length; j++)
                            branches[j] = GetInstruction(offsets[j]);

                        instruction.Operand = branches;
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
            if (offset < 0 || offset > items[size - 1].Offset)
                return null;

            int min = 0;
            int max = size - 1;
            while (min <= max)
            {
                int mid = min + ((max - min) / 2);
                var instruction = items[mid];
                var instruction_offset = instruction.Offset;

                if (offset == instruction_offset)
                    return instruction;

                if (offset < instruction_offset)
                    max = mid - 1;
                else
                    min = mid + 1;
            }

            return null;
        }

        private void ReadExceptions(CORINFO_EH_CLAUSE[] exceptionClauses)
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

        private void ReadExceptions(IList<ExceptionHandlingClause> exceptionClauses)
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
                        handler.CatchType = new MetadataToken((uint)exceptionClause.CatchType.MetadataToken);
                        break;
                    case ExceptionHandlerType.Filter:
                        handler.FilterStart = GetInstruction(exceptionClause.FilterOffset);
                        break;
                }

                body.ExceptionHandlers.Add(handler);

            }
        }



        private void ReadExceptionsFromBytes()
        {
            ReadSection();
        }

        private void ReadSection()
        { 
            const byte fat_format = 0x40;
            const byte more_sects = 0x80;

            var flags = exceptionsBytes.ReadByte();
            if ((flags & fat_format) == 0)
                ReadSmallSection();
            else
                ReadFatSection();

            if ((flags & more_sects) != 0)
                ReadSection();
        }

        private void ReadSmallSection()
        {
            var count = exceptionsBytes.ReadByte() / 12;
            exceptionsBytes.Advance(2);

            ReadExceptionHandlers(
                count,
                () => (int)exceptionsBytes.ReadUInt16(),
                () => (int)exceptionsBytes.ReadByte());
        }

        private void ReadFatSection()
        {
            exceptionsBytes.position--;
            var count = (exceptionsBytes.ReadInt32() >> 8) / 24;

            ReadExceptionHandlers(
                count,
                exceptionsBytes.ReadInt32,
                exceptionsBytes.ReadInt32);
        }

        // inline ?
        private void ReadExceptionHandlers(int count, Func<int> read_entry, Func<int> read_length)
        {
            for (int i = 0; i < count; i++)
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
            switch (handler.HandlerType)
            {
                case ExceptionHandlerType.Catch:
                    handler.CatchType = ReadToken(exceptionsBytes);
                    break;
                case ExceptionHandlerType.Filter:
                    handler.FilterStart = GetInstruction(exceptionsBytes.ReadInt32());
                    break;
                default:
                    exceptionsBytes.Advance(4);
                    break;
            }
        }


        private ByteBuffer exceptionsBytes;

        private MethodBody body;

        private int codeSize;
    }
}
