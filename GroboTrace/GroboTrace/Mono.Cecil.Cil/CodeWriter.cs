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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using GroboTrace.Mono.Cecil.Metadata;
using GroboTrace.Mono.Cecil.PE;
using GroboTrace.Mono.Collections.Generic;

using RVA = System.UInt32;

namespace GroboTrace.Mono.Cecil.Cil
{
    internal sealed class CodeWriter : ByteBuffer
    {
        public CodeWriter(Module module, Func<byte[], MetadataToken> signatureTokenBuilder, MethodBody body, uint typeGenericParameters, uint methodGenericParameters)
            : base(0)
        {
            this.typeGenericParameters = typeGenericParameters;
            this.methodGenericParameters = methodGenericParameters;
            this.module = module;
            this.signatureTokenBuilder = signatureTokenBuilder;
            this.body = body;
        }

        public void WriteMethodBody()
        {
            WriteResolvedMethodBody(body);

            Align(4);
        }

        private void WriteResolvedMethodBody(MethodBody body)
        {
            this.body = body;

            body.SimplifyMacros();
            body.OptimizeMacros();

            ComputeHeader();
            if(RequiresFatHeader())
                WriteFatHeader();
            else
                WriteByte((byte)(0x2 | (body.CodeSize << 2))); // tiny

            WriteInstructions();

            if(body.HasExceptionHandlers)
                WriteExceptionHandlers();
        }

        private void WriteFatHeader()
        {
            byte flags = 0x3; // fat
            if(body.InitLocals)
                flags |= 0x10; // init locals
            if(body.HasExceptionHandlers)
                flags |= 0x8; // more sections

            WriteByte(flags);
            WriteByte(0x30);
            WriteInt16((short)body.max_stack_size);
            WriteInt32(body.code_size);
            body.local_var_token = GetVariablesSignature();
            WriteMetadataToken(body.local_var_token);
        }

        private MetadataToken GetVariablesSignature()
        {
            if(!body.HasVariables)
                return MetadataToken.Zero;
            var writer = new ByteBuffer(body.VariablesSignature.Length + 1 + 4);
            writer.position = 0;
            writer.WriteByte(0x7);
            writer.WriteCompressedUInt32(body.variablesCount);
            writer.WriteBytes(body.VariablesSignature);
            writer.position = 0;
            var signature = writer.ReadBytes(writer.length);
            var metadataToken = signatureTokenBuilder(signature);
            Debug.WriteLine(".NET: got metadata token for signature : {0}", metadataToken.ToInt32());
            return metadataToken;
        }

        private void WriteInstructions()
        {
            var instructions = body.Instructions;
            var items = instructions.items;
            var size = instructions.size;

            for(int i = 0; i < size; i++)
            {
                var instruction = items[i];
                WriteOpCode(instruction.opcode);
                WriteOperand(instruction);
            }
        }

        private void WriteOpCode(OpCode opcode)
        {
            if(opcode.Size == 1)
                WriteByte(opcode.Op2);
            else
            {
                WriteByte(opcode.Op1);
                WriteByte(opcode.Op2);
            }
        }

        private void WriteOperand(Instruction instruction)
        {
            var opcode = instruction.opcode;
            var operand_type = opcode.OperandType;
            if(operand_type == OperandType.InlineNone)
                return;

            var operand = instruction.operand;
            if(operand == null)
                throw new ArgumentException();

            switch(operand_type)
            {
            case OperandType.InlineSwitch:
                {
                    var targets = (Instruction[])operand;
                    WriteInt32(targets.Length);
                    var diff = instruction.Offset + opcode.Size + (4 * (targets.Length + 1));
                    for(int i = 0; i < targets.Length; i++)
                        WriteInt32(GetTargetOffset(targets[i]) - diff);
                    break;
                }
            case OperandType.ShortInlineBrTarget:
                {
                    var target = (Instruction)operand;
                    WriteSByte((sbyte)(GetTargetOffset(target) - (instruction.Offset + opcode.Size + 1)));
                    break;
                }
            case OperandType.InlineBrTarget:
                {
                    var target = (Instruction)operand;
                    WriteInt32(GetTargetOffset(target) - (instruction.Offset + opcode.Size + 4));
                    break;
                }
            case OperandType.ShortInlineVar:
                WriteByte((byte)(int)operand);
                break;
            case OperandType.ShortInlineArg:
                WriteByte((byte)(int)operand);
                break;
            case OperandType.InlineVar:
                WriteInt16((short)(int)operand);
                break;
            case OperandType.InlineArg:
                WriteInt16((short)(int)operand);
                break;
            case OperandType.InlineSig:
                WriteMetadataToken((MetadataToken)operand);
                break;
            case OperandType.ShortInlineI:
                if(opcode == OpCodes.Ldc_I4_S)
                    WriteSByte((sbyte)operand);
                else
                    WriteByte((byte)operand);
                break;
            case OperandType.InlineI:
                WriteInt32((int)operand);
                break;
            case OperandType.InlineI8:
                WriteInt64((long)operand);
                break;
            case OperandType.ShortInlineR:
                WriteSingle((float)operand);
                break;
            case OperandType.InlineR:
                WriteDouble((double)operand);
                break;
            case OperandType.InlineString:
                WriteMetadataToken((MetadataToken)operand);
                break;
            case OperandType.InlineType:
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineTok:
                WriteMetadataToken((MetadataToken)operand);
                break;
            default:
                throw new ArgumentException();
            }
        }

        private int GetTargetOffset(Instruction instruction)
        {
            if(instruction == null)
            {
                var last = body.instructions[body.instructions.size - 1];
                return last.offset + last.GetSize();
            }

            return instruction.offset;
        }

        private bool RequiresFatHeader()
        {
            return body.CodeSize >= 64
                   || body.InitLocals
                   || body.HasVariables
                   || body.HasExceptionHandlers
                   || body.MaxStackSize > 8;
        }

        private void ComputeHeader()
        {
            int offset = 0;
            var instructions = body.instructions;
            var items = instructions.items;
            var count = instructions.size;
            var stack_size = 0;
            var max_stack = 0;
            Dictionary<Instruction, int> stack_sizes = null;

            if(body.HasExceptionHandlers)
                ComputeExceptionHandlerStackSize(ref stack_sizes);

            for(int i = 0; i < count; i++)
            {
                var instruction = items[i];
                instruction.offset = offset;
                offset += instruction.GetSize();

                ComputeStackSize(instruction, ref stack_sizes, ref stack_size, ref max_stack);
            }

            body.code_size = offset;
            body.max_stack_size = max_stack;
        }

        private void ComputeExceptionHandlerStackSize(ref Dictionary<Instruction, int> stack_sizes)
        {
            var exception_handlers = body.ExceptionHandlers;

            for(int i = 0; i < exception_handlers.Count; i++)
            {
                var exception_handler = exception_handlers[i];

                switch(exception_handler.HandlerType)
                {
                case ExceptionHandlerType.Catch:
                    AddExceptionStackSize(exception_handler.HandlerStart, ref stack_sizes);
                    break;
                case ExceptionHandlerType.Filter:
                    AddExceptionStackSize(exception_handler.FilterStart, ref stack_sizes);
                    AddExceptionStackSize(exception_handler.HandlerStart, ref stack_sizes);
                    break;
                }
            }
        }

        private static void AddExceptionStackSize(Instruction handler_start, ref Dictionary<Instruction, int> stack_sizes)
        {
            if(handler_start == null)
                return;

            if(stack_sizes == null)
                stack_sizes = new Dictionary<Instruction, int>();

            stack_sizes[handler_start] = 1;
        }

        private void ComputeStackSize(Instruction instruction, ref Dictionary<Instruction, int> stack_sizes, ref int stack_size, ref int max_stack)
        {
            int computed_size;
            if(stack_sizes != null && stack_sizes.TryGetValue(instruction, out computed_size))
                stack_size = computed_size;

            max_stack = Math.Max(max_stack, stack_size);
            ComputeStackDelta(instruction, ref stack_size);
            max_stack = Math.Max(max_stack, stack_size);

            CopyBranchStackSize(instruction, ref stack_sizes, stack_size);
            ComputeStackSize(instruction, ref stack_size);
        }

        private static void CopyBranchStackSize(Instruction instruction, ref Dictionary<Instruction, int> stack_sizes, int stack_size)
        {
            if(stack_size == 0)
                return;

            switch(instruction.opcode.OperandType)
            {
            case OperandType.ShortInlineBrTarget:
            case OperandType.InlineBrTarget:
                CopyBranchStackSize(ref stack_sizes, (Instruction)instruction.operand, stack_size);
                break;
            case OperandType.InlineSwitch:
                var targets = (Instruction[])instruction.operand;
                for(int i = 0; i < targets.Length; i++)
                    CopyBranchStackSize(ref stack_sizes, targets[i], stack_size);
                break;
            }
        }

        private static void CopyBranchStackSize(ref Dictionary<Instruction, int> stack_sizes, Instruction target, int stack_size)
        {
            if(stack_sizes == null)
                stack_sizes = new Dictionary<Instruction, int>();

            int branch_stack_size = stack_size;

            int computed_size;
            if(stack_sizes.TryGetValue(target, out computed_size))
                branch_stack_size = Math.Max(branch_stack_size, computed_size);

            stack_sizes[target] = branch_stack_size;
        }

        private static void ComputeStackSize(Instruction instruction, ref int stack_size)
        {
            switch(instruction.opcode.FlowControl)
            {
            case FlowControl.Branch:
            case FlowControl.Break:
            case FlowControl.Throw:
            case FlowControl.Return:
                stack_size = 0;
                break;
            }
        }

        private void ComputeStackDelta(Instruction instruction, ref int stack_size)
        {
            switch(instruction.opcode.FlowControl)
            {
            case FlowControl.Call:
                {
                    var token = (MetadataToken)instruction.operand;
                    bool hasThis = false;
                    int parametersCount = 0;
                    bool hasReturnType = false;
                    
                    if (instruction.opcode.Code == Code.Calli)
                    {
                        var signature = module.ResolveSignature(token.ToInt32());
                        var parsedSignature = new MethodSignatureReader(signature).Read();
                        hasThis = parsedSignature.HasThis && !parsedSignature.ExplicitThis;
                        parametersCount = parsedSignature.ParamCount;
                        hasReturnType = parsedSignature.HasReturnType;
                    }
                    else
                    {
                        var methodBase = module.ResolveMethod(token.ToInt32(), Enumerable.Repeat(Zzz.__canon, (int)typeGenericParameters).ToArray(), Enumerable.Repeat(Zzz.__canon, (int)methodGenericParameters).ToArray());
                        hasThis = methodBase.CallingConvention.HasFlag(CallingConventions.HasThis)
                                  && !methodBase.CallingConvention.HasFlag(CallingConventions.ExplicitThis);
                        parametersCount = methodBase.GetParameters().Length;
                        var methodInfo = methodBase as MethodInfo;
                        hasReturnType = methodInfo != null && methodInfo.ReturnType != typeof(void);
                    }


                    // pop 'this' argument
                    if (hasThis && instruction.opcode.Code != Code.Newobj)
                    stack_size--;
                    // pop normal arguments
                    stack_size -= parametersCount;
                    // pop function pointer
                    if(instruction.opcode.Code == Code.Calli)
                        stack_size--;
                    // push return value
                    if (hasReturnType || instruction.opcode.Code == Code.Newobj)
                        stack_size++;
                    break;
                }
            default:
                ComputePopDelta(instruction.opcode.StackBehaviourPop, ref stack_size);
                ComputePushDelta(instruction.opcode.StackBehaviourPush, ref stack_size);
                break;
            }
        }

        private static void ComputePopDelta(StackBehaviour pop_behavior, ref int stack_size)
        {
            switch(pop_behavior)
            {
            case StackBehaviour.Popi:
            case StackBehaviour.Popref:
            case StackBehaviour.Pop1:
                stack_size--;
                break;
            case StackBehaviour.Pop1_pop1:
            case StackBehaviour.Popi_pop1:
            case StackBehaviour.Popi_popi:
            case StackBehaviour.Popi_popi8:
            case StackBehaviour.Popi_popr4:
            case StackBehaviour.Popi_popr8:
            case StackBehaviour.Popref_pop1:
            case StackBehaviour.Popref_popi:
                stack_size -= 2;
                break;
            case StackBehaviour.Popi_popi_popi:
            case StackBehaviour.Popref_popi_popi:
            case StackBehaviour.Popref_popi_popi8:
            case StackBehaviour.Popref_popi_popr4:
            case StackBehaviour.Popref_popi_popr8:
            case StackBehaviour.Popref_popi_popref:
                stack_size -= 3;
                break;
            case StackBehaviour.PopAll:
                stack_size = 0;
                break;
            }
        }

        private static void ComputePushDelta(StackBehaviour push_behaviour, ref int stack_size)
        {
            switch(push_behaviour)
            {
            case StackBehaviour.Push1:
            case StackBehaviour.Pushi:
            case StackBehaviour.Pushi8:
            case StackBehaviour.Pushr4:
            case StackBehaviour.Pushr8:
            case StackBehaviour.Pushref:
                stack_size++;
                break;
            case StackBehaviour.Push1_push1:
                stack_size += 2;
                break;
            }
        }

        private void WriteExceptionHandlers()
        {
            Align(4);

            var handlers = body.ExceptionHandlers;

            if(handlers.Count < 0x15 && !RequiresFatSection(handlers))
                WriteSmallSection(handlers);
            else
                WriteFatSection(handlers);
        }

        private static bool RequiresFatSection(Collection<ExceptionHandler> handlers)
        {
            for(int i = 0; i < handlers.Count; i++)
            {
                var handler = handlers[i];

                if(IsFatRange(handler.TryStart, handler.TryEnd))
                    return true;

                if(IsFatRange(handler.HandlerStart, handler.HandlerEnd))
                    return true;

                if(handler.HandlerType == ExceptionHandlerType.Filter
                   && IsFatRange(handler.FilterStart, handler.HandlerStart))
                    return true;
            }

            return false;
        }

        private static bool IsFatRange(Instruction start, Instruction end)
        {
            if(start == null)
                throw new ArgumentException();

            if(end == null)
                return true;

            return end.Offset - start.Offset > 255 || start.Offset > 65535;
        }

        private void WriteSmallSection(Collection<ExceptionHandler> handlers)
        {
            const byte eh_table = 0x1;

            WriteByte(eh_table);
            WriteByte((byte)(handlers.Count * 12 + 4));
            WriteBytes(2);

            WriteExceptionHandlers(
                handlers,
                i => WriteUInt16((ushort)i),
                i => WriteByte((byte)i));
        }

        private void WriteFatSection(Collection<ExceptionHandler> handlers)
        {
            const byte eh_table = 0x1;
            const byte fat_format = 0x40;

            WriteByte(eh_table | fat_format);

            int size = handlers.Count * 24 + 4;
            WriteByte((byte)(size & 0xff));
            WriteByte((byte)((size >> 8) & 0xff));
            WriteByte((byte)((size >> 16) & 0xff));

            WriteExceptionHandlers(handlers, WriteInt32, WriteInt32);
        }

        private void WriteExceptionHandlers(Collection<ExceptionHandler> handlers, Action<int> write_entry, Action<int> write_length)
        {
            for(int i = 0; i < handlers.Count; i++)
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
            switch(handler.HandlerType)
            {
            case ExceptionHandlerType.Catch:
                WriteMetadataToken(handler.CatchType);
                break;
            case ExceptionHandlerType.Filter:
                WriteInt32(handler.FilterStart.Offset);
                break;
            default:
                WriteInt32(0);
                break;
            }
        }

        private void WriteMetadataToken(MetadataToken token)
        {
            WriteUInt32(token.ToUInt32());
        }

        private void Align(int align)
        {
            align--;
            WriteBytes(((position + align) & ~align) - position);
        }

        private readonly Module module;
        private readonly Func<byte[], MetadataToken> signatureTokenBuilder;

        private MethodBody body;
        private uint typeGenericParameters;
        private uint methodGenericParameters;
    }
}