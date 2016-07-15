using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using GroboTrace.Mono.Cecil.Metadata;
using GroboTrace.Mono.Cecil.PE;
using GroboTrace.Mono.Collections.Generic;

namespace GroboTrace.Mono.Cecil.Cil
{
    internal class ReflectionMethodBodyBuilder
    {
        public ReflectionMethodBodyBuilder(MethodBody cecilMethodBody)
        {
          
            body = cecilMethodBody;
            
            codeBuffer = new ByteBuffer();
            exceptionsBuffer = new ByteBuffer();

            WriteResolvedMethodBody();
        }

        private void WriteResolvedMethodBody()
        {
            body.SimplifyMacros();
            body.OptimizeMacros();
            
            WriteInstructions();

            if (body.HasExceptionHandlers)
                WriteExceptionHandlers();
        }

        
    
        private void WriteInstructions()
        {
            var instructions = body.Instructions;
            var items = instructions.items;
            var size = instructions.size;

            for (int i = 0; i < size; i++)
            {
                var instruction = items[i];
                WriteOpCode(instruction.opcode);
                WriteOperand(instruction);
            }
        }


        private void WriteOpCode(OpCode opcode)
        {
            if (opcode.Size == 1)
                codeBuffer.WriteByte(opcode.Op2);
            else
            {
                codeBuffer.WriteByte(opcode.Op1);
                codeBuffer.WriteByte(opcode.Op2);
            }
        }

        private void WriteOperand(Instruction instruction)
        {
            var opcode = instruction.opcode;
            var operand_type = opcode.OperandType;
            if (operand_type == OperandType.InlineNone)
                return;

            var operand = instruction.operand;
            if (operand == null)
                throw new ArgumentException();

            switch (operand_type)
            {
                case OperandType.InlineSwitch:
                    {
                        var targets = (Instruction[])operand;
                        codeBuffer.WriteInt32(targets.Length);
                        var diff = instruction.Offset + opcode.Size + (4 * (targets.Length + 1));
                        for (int i = 0; i < targets.Length; i++)
                            codeBuffer.WriteInt32(GetTargetOffset(targets[i]) - diff);
                        break;
                    }
                case OperandType.ShortInlineBrTarget:
                    {
                        var target = (Instruction)operand;
                        codeBuffer.WriteSByte((sbyte)(GetTargetOffset(target) - (instruction.Offset + opcode.Size + 1)));
                        break;
                    }
                case OperandType.InlineBrTarget:
                    {
                        var target = (Instruction)operand;
                        codeBuffer.WriteInt32(GetTargetOffset(target) - (instruction.Offset + opcode.Size + 4));
                        break;
                    }
                case OperandType.ShortInlineVar:
                    codeBuffer.WriteByte((byte)(int)operand);
                    break;
                case OperandType.ShortInlineArg:
                    codeBuffer.WriteByte((byte)(int)operand);
                    break;
                case OperandType.InlineVar:
                    codeBuffer.WriteInt16((short)(int)operand);
                    break;
                case OperandType.InlineArg:
                    codeBuffer.WriteInt16((short)(int)operand);
                    break;
                case OperandType.InlineSig:
                    WriteMetadataToken(codeBuffer, (MetadataToken)operand);
                    break;
                case OperandType.ShortInlineI:
                    if (opcode == OpCodes.Ldc_I4_S)
                        codeBuffer.WriteSByte((sbyte)operand);
                    else
                        codeBuffer.WriteByte((byte)operand);
                    break;
                case OperandType.InlineI:
                    codeBuffer.WriteInt32((int)operand);
                    break;
                case OperandType.InlineI8:
                    codeBuffer.WriteInt64((long)operand);
                    break;
                case OperandType.ShortInlineR:
                    codeBuffer.WriteSingle((float)operand);
                    break;
                case OperandType.InlineR:
                    codeBuffer.WriteDouble((double)operand);
                    break;
                case OperandType.InlineString:
                    WriteMetadataToken(codeBuffer, (MetadataToken)operand);
                    break;
                case OperandType.InlineType:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                    WriteMetadataToken(codeBuffer, (MetadataToken)operand);
                    break;
                default:
                    throw new ArgumentException();
            }
        }

        private void WriteExceptionHandlers()
        {
            //Align(4);

            var handlers = body.ExceptionHandlers;

            if (handlers.Count < 0x15 && !RequiresFatSection(handlers))
                WriteSmallSection(handlers);
            else
                WriteFatSection(handlers);
        }

        private static bool RequiresFatSection(Collection<ExceptionHandler> handlers)
        {
            for (int i = 0; i < handlers.Count; i++)
            {
                var handler = handlers[i];

                if (IsFatRange(handler.TryStart, handler.TryEnd))
                    return true;

                if (IsFatRange(handler.HandlerStart, handler.HandlerEnd))
                    return true;

                if (handler.HandlerType == ExceptionHandlerType.Filter
                   && IsFatRange(handler.FilterStart, handler.HandlerStart))
                    return true;
            }

            return false;
        }

        private static bool IsFatRange(Instruction start, Instruction end)
        {
            if (start == null)
                throw new ArgumentException();

            if (end == null)
                return true;

            return end.Offset - start.Offset > 255 || start.Offset > 65535;
        }

        private void WriteSmallSection(Collection<ExceptionHandler> handlers)
        {
            const byte eh_table = 0x1;

            exceptionsBuffer.WriteByte(eh_table);
            exceptionsBuffer.WriteByte((byte)(handlers.Count * 12 + 4));
            exceptionsBuffer.WriteBytes(2);

            WriteExceptionHandlers(
                handlers,
                i => exceptionsBuffer.WriteUInt16((ushort)i),
                i => exceptionsBuffer.WriteByte((byte)i));
        }

        private void WriteFatSection(Collection<ExceptionHandler> handlers)
        {
            const byte eh_table = 0x1;
            const byte fat_format = 0x40;

            exceptionsBuffer.WriteByte(eh_table | fat_format);

            int size = handlers.Count * 24 + 4;
            exceptionsBuffer.WriteByte((byte)(size & 0xff));
            exceptionsBuffer.WriteByte((byte)((size >> 8) & 0xff));
            exceptionsBuffer.WriteByte((byte)((size >> 16) & 0xff));

            WriteExceptionHandlers(handlers, exceptionsBuffer.WriteInt32, exceptionsBuffer.WriteInt32);
        }

        private void WriteExceptionHandlers(Collection<ExceptionHandler> handlers, Action<int> write_entry, Action<int> write_length)
        {
            for (int i = 0; i < handlers.Count; i++)
            {
                var handler = handlers[i];

                write_entry((int)handler.HandlerType);

                write_entry(handler.TryStart.Offset);
                write_length(GetTargetOffset(handler.TryEnd) - handler.TryStart.Offset);

                write_entry(handler.HandlerStart.Offset);
                write_length(GetTargetOffset(handler.HandlerEnd) - handler.HandlerStart.Offset);

                WriteExceptionHandlerSpecific(handler);
            }
        }

        private void WriteExceptionHandlerSpecific(ExceptionHandler handler)
        {
            switch (handler.HandlerType)
            {
                case ExceptionHandlerType.Catch:
                    WriteMetadataToken(exceptionsBuffer, handler.CatchType);
                    break;
                case ExceptionHandlerType.Filter:
                    exceptionsBuffer.WriteInt32(handler.FilterStart.Offset);
                    break;
                default:
                    exceptionsBuffer.WriteInt32(0);
                    break;
            }
        }



        private int GetTargetOffset(Instruction instruction)
        {
            if (instruction == null)
            {
                var last = body.instructions[body.instructions.size - 1];
                return last.offset + last.GetSize();
            }

            return instruction.offset;
        }

        private void WriteMetadataToken(ByteBuffer buffer, MetadataToken token)
        {
            buffer.WriteUInt32(token.ToUInt32());
        }

        public byte[] GetCode()
        {
            var temp = new byte[codeBuffer.length];
            Array.Copy(codeBuffer.buffer, temp, codeBuffer.length);
            return temp;
            //return codeBuffer.buffer;
        }

        public bool HasExceptions()
        {
            return body.HasExceptionHandlers;
        }

        public byte[] GetExceptions()
        {
            var temp = new byte[exceptionsBuffer.length];
            Array.Copy(exceptionsBuffer.buffer, temp, exceptionsBuffer.length);
            return temp;
        }

        private ByteBuffer codeBuffer;
        private ByteBuffer exceptionsBuffer;

        private Module module;

        private MethodBody body;

    }
}
