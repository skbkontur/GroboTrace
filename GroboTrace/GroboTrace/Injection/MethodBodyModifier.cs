using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using GrEmit;

namespace GroboTrace.Injection
{
    public class LocalInfo
    {
        public int LocalIndex;
        public Type LocalType;
        public bool IsPinned;

        public LocalInfo(int localIndex, Type localType, bool isPinned)
        {
            LocalIndex = localIndex;
            LocalType = localType;
            IsPinned = isPinned;
        }
    }

    public class MethodBodyModifier
    {
        private MethodInfo methodInfo;
        private List<AbstractInstruction> instructions;

        private List<LocalInfo> extendedLocalVariables;

        public int startTicksLocalIndex;
        public int methodLocalIndex;
        public int resultLocalIndex;

        public MethodBodyModifier(MethodInfo methodInfo)
        {
            this.methodInfo = methodInfo;
            ILBytesReader ilBytesReader = new ILBytesReader(methodInfo);
            instructions = ilBytesReader.GetInstructionsList();
        }


        public void InsertExceptionInstructionsAccordingToClauses()
        {
            HashSet<int> tryOffsetSet = new HashSet<int>();
            MethodBody body = methodInfo.GetMethodBody();

            foreach (var clause in body.ExceptionHandlingClauses)
            {
                tryOffsetSet.Add(clause.TryOffset);
            }

            foreach (var tryOffset in tryOffsetSet)
            {
                for (int index = 0; index < instructions.Count; ++index)
                    if (instructions[index].Offset == tryOffset)
                    {
                        instructions.Insert(index, new BeginExceptionInstruction(tryOffset));
                        break;
                    }
                int endExceptionBlockOffset = body.ExceptionHandlingClauses
                                                  .Where(clause => clause.TryOffset == tryOffset)
                                                  .Max(clause => clause.HandlerOffset + clause.HandlerLength);

                for (int index = 0; index < instructions.Count; ++index)
                    if (instructions[index].Offset == endExceptionBlockOffset)
                    {
                        instructions.Insert(index, new EndExceptionInstruction(endExceptionBlockOffset));
                        break;
                    }
            }

            foreach (var clause in body.ExceptionHandlingClauses)
            {
                //Catch
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause)
                {
                    for (int index = 0; index < instructions.Count; ++index)
                        if (instructions[index].Offset == clause.HandlerOffset)
                        {
                            instructions.Insert(index, new BeginCatchInstruction(clause.HandlerOffset, clause.CatchType));
                            break;
                        }
                }

                //Finally
                if (clause.Flags == ExceptionHandlingClauseOptions.Finally)
                {
                    for (int index = 0; index < instructions.Count; ++index)
                        if (instructions[index].Offset == clause.HandlerOffset)
                        {
                            instructions.Insert(index, new BeginFinallyInstruction(clause.HandlerOffset));
                            break;
                        }
                }
            }

        }

        public void ExtendLocalVariables()
        {
            MethodBody body = methodInfo.GetMethodBody();
            extendedLocalVariables = body.LocalVariables
                .Select(localVariable => new LocalInfo(localVariable.LocalIndex, localVariable.LocalType, localVariable.IsPinned))
                .ToList();

            startTicksLocalIndex = body.LocalVariables.Count;
            LocalInfo startTicksLocal = new LocalInfo(startTicksLocalIndex, typeof(long), false);
            extendedLocalVariables.Add(startTicksLocal);

            methodLocalIndex = startTicksLocalIndex + 1;
            LocalInfo methodLocal = new LocalInfo(methodLocalIndex, typeof(MethodInfo), false);
            extendedLocalVariables.Add(methodLocal);

            if (methodInfo.ReturnType != typeof(void))
            {
                resultLocalIndex = methodLocalIndex + 1;
                LocalInfo resultLocal = new LocalInfo(resultLocalIndex, methodInfo.ReturnType, false);
                extendedLocalVariables.Add(resultLocal);
            }
        }

        public void ReplaceAllRetInstructions()
        {
            InsertAt(instructions.Count, OpCodes.Nop, null); // to refer to

            var position = 0;

            while (position < instructions.Count)
            {
                if (!(instructions[position] is ILInstruction))
                {
                    position++;
                    continue;
                }

                if (((ILInstruction)instructions[position]).Code == OpCodes.Ret)
                {
                    RemoveAt(position);
                    if (methodInfo.ReturnType != typeof(void))
                    {
                        InsertAt(position, OpCodes.Stloc, (ushort)resultLocalIndex); // stloc receives unsigned 16-bit
                        InsertBranchAt(position + 1, OpCodes.Br, instructions.Count - 1);
                        position += 2;
                    }
                    else
                    {
                        InsertBranchAt(position, OpCodes.Br, instructions.Count - 1);
                        ++position;
                    }
                }
                else
                {
                    ++position;
                }
            }
        }

        public void InsertHeader(int ourMethodIndex, long hashkey)
        {
            int startIndex = 0;

            InsertAt(startIndex++, OpCodes.Ldsfld, typeof(MethodWrapper).GetField("methods", BindingFlags.Static | BindingFlags.Public)); // [ methods[] ]
            InsertAt(startIndex++, OpCodes.Ldc_I4, ourMethodIndex); // [methods[], ourMethodIndex]
            InsertAt(startIndex++, OpCodes.Ldelem_Ref, null); // [ ourMethod ]
            InsertAt(startIndex++, OpCodes.Dup, null); // [ourMethod, ourMethod]
            InsertAt(startIndex++, OpCodes.Stloc, (ushort)methodLocalIndex); // [ourMethod]
            InsertAt(startIndex++, OpCodes.Ldc_I8, hashkey); // [ ourMethod, hashkey ]
            InsertAt(startIndex++, OpCodes.Call, typeof(TracingAnalyzer).GetMethod("MethodStarted")); // []

            // todo use calli
            InsertAt(startIndex++, OpCodes.Ldsfld, typeof(MethodWrapper).GetField("ticksReader", BindingFlags.Static | BindingFlags.NonPublic)); // [ticksReader]
            InsertAt(startIndex++, OpCodes.Callvirt, typeof(Func<long>).GetMethod("Invoke")); // [ticks]
            InsertAt(startIndex++, OpCodes.Stloc, (ushort)startTicksLocalIndex); // []

            instructions.Insert(startIndex, new BeginExceptionInstruction(instructions[startIndex].Offset));
            startIndex++;

            //var nopIdx = parsedBody.instructions.Count - 1;
        }

        public void InsertFooter(long hashkey)
        {
            var lastInstruction = instructions.Last();

            instructions.Insert(instructions.Count,
                        new BeginFinallyInstruction(lastInstruction.Offset + lastInstruction.Size));

            InsertAt(instructions.Count, OpCodes.Ldloc, (ushort)methodLocalIndex); // [ ourMethod ]
            InsertAt(instructions.Count, OpCodes.Ldc_I8, hashkey); // [ ourMethod, hashkey ]
            InsertAt(instructions.Count, OpCodes.Ldsfld, typeof(MethodWrapper).GetField("ticksReader", BindingFlags.Static | BindingFlags.NonPublic)); // [ outMethod, hashkey ][ticksReader]
            InsertAt(instructions.Count, OpCodes.Callvirt, typeof(Func<long>).GetMethod("Invoke")); // [ outMethod, hashkey ][ticks]
            InsertAt(instructions.Count, OpCodes.Ldloc, (ushort)startTicksLocalIndex); // [ outMethod, hashkey, ticks, startTicks]
            InsertAt(instructions.Count, OpCodes.Sub, null); // [ outMethod, hashkey, elapsed]

            InsertAt(instructions.Count, OpCodes.Call, typeof(TracingAnalyzer).GetMethod("MethodFinished")); //[]

            lastInstruction = instructions.Last();

            instructions.Insert(instructions.Count,
                                           new EndExceptionInstruction(lastInstruction.Offset + lastInstruction.Size));

            if (methodInfo.ReturnType != typeof(void))
            {
                InsertAt(instructions.Count, OpCodes.Ldloc, (ushort)resultLocalIndex); // ldloc receives unsigned 16-bit 
            }

            InsertAt(instructions.Count, OpCodes.Ret, null);

        }





        /// <summary>
        ///     Gets the IL code of the method
        /// </summary>
        /// <returns></returns>
        public string GetBodyCode()
        {
            var result = "";
            if (instructions == null) return result;
            foreach (AbstractInstruction abstractInstruction in instructions)
            {
                ILInstruction ilInstruction = abstractInstruction as ILInstruction;
                if (ilInstruction != null)
                    result += ilInstruction.GetCode() + "\n";
            }
            return result;
        }

        public void InsertAt(int index, OpCode opcode, object operand)
        {
            if (index > instructions.Count)
                throw new ArgumentOutOfRangeException("TODO");

            if (opcode == OpCodes.Ldloc)
            {
                var idx = (ushort)operand;
                switch (idx)
                {
                case 0:
                    opcode = OpCodes.Ldloc_0;
                    operand = null;
                    break;
                case 1:
                    opcode = OpCodes.Ldloc_1;
                    operand = null;
                    break;
                case 2:
                    opcode = OpCodes.Ldloc_2;
                    operand = null;
                    break;
                case 3:
                    opcode = OpCodes.Ldloc_3;
                    operand = null;
                    break;
                }
            }

            if (opcode == OpCodes.Stloc)
            {
                var idx = (ushort)operand;
                switch (idx)
                {
                case 0:
                    opcode = OpCodes.Stloc_0;
                    operand = null;
                    break;
                case 1:
                    opcode = OpCodes.Stloc_1;
                    operand = null;
                    break;
                case 2:
                    opcode = OpCodes.Stloc_2;
                    operand = null;
                    break;
                case 3:
                    opcode = OpCodes.Stloc_3;
                    operand = null;
                    break;
                }
            }

            ILInstruction newInstruction;
            int threshold;
            if (index == instructions.Count)
            {
                newInstruction = new ILInstruction
                    {
                        Code = opcode,
                        Operand = operand,
                        OperandData = operand,
                        Offset = instructions[instructions.Count - 1].Offset + instructions[instructions.Count - 1].Size
                    };
                instructions.Add(newInstruction);
                threshold = newInstruction.Offset;
            }
            else
            {
                threshold = instructions[index].Offset;
                newInstruction = new ILInstruction {Code = opcode, Operand = operand, OperandData = operand, Offset = threshold};
                instructions.Insert(index, newInstruction);
            }
            var size = newInstruction.Size;
            foreach (AbstractInstruction abstractInstruction in instructions)
            {
                ILInstruction instruction = abstractInstruction as ILInstruction;
                if (instruction == null)
                    continue;

                if (instruction.Code.OperandType == OperandType.InlineBrTarget || instruction.Code.OperandType == OperandType.ShortInlineBrTarget)
                {
                    var target = (int)instruction.Operand;
                    if (target >= threshold)
                        instruction.Operand = target + size;
                }
                else if (instruction.Code.OperandType == OperandType.InlineSwitch)
                {
                    var targets = (int[])instruction.Operand;
                    for (var i = 0; i < targets.Length; ++i)
                    {
                        if (targets[i] >= threshold)
                            targets[i] += size;
                    }
                }
            }
            for (var i = index + 1; i < instructions.Count; ++i)
                instructions[i].Offset += size;
        }

        public void InsertBranchAt(int index, OpCode opCode, int targetIndex)
        {
            int targetOffset;
            if (targetIndex < instructions.Count)
                targetOffset = instructions[targetIndex].Offset;
            else
                targetOffset = instructions[instructions.Count - 1].Offset + instructions[instructions.Count - 1].Size;
//            var instr = new ILInstruction
//            {
//                Code = opCode
//            };
//            if(targetIndex >= index)
//                targetOffset += instr.Size;
            InsertAt(index, opCode, targetOffset);
        }

        public void RemoveAt(int index)
        {
            // todo
            var toBeDeleted = instructions[index];
            var size = toBeDeleted.Size;
            var threshold = toBeDeleted.Offset;

            for (var i = index + 1; i < instructions.Count; ++i)
                instructions[i].Offset -= size;

            foreach (AbstractInstruction abstractInstruction in instructions)
            {
                ILInstruction instruction = abstractInstruction as ILInstruction;
                if (instruction == null)
                    continue;

                if (instruction.Code.OperandType == OperandType.InlineBrTarget || instruction.Code.OperandType == OperandType.ShortInlineBrTarget)
                {
                    var target = (int)instruction.Operand;
                    if (target > threshold)
                        instruction.Operand = target - size;
                }
                else if (instruction.Code.OperandType == OperandType.InlineSwitch)
                {
                    var targets = (int[])instruction.Operand;
                    for (var i = 0; i < targets.Length; ++i)
                    {
                        if (targets[i] > threshold)
                            targets[i] -= size;
                    }
                }
            }

            instructions.RemoveAt(index);
        }

        public DynamicMethod GetExtendedMethod()
        {
            var parameterTypes = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray();
            var owner = methodInfo.ReflectedType ?? methodInfo.DeclaringType ?? typeof(string);
            var newMethod = new DynamicMethod(methodInfo.Name + "_" + Guid.NewGuid(), methodInfo.ReturnType, parameterTypes, owner, true);

            EmitToStupidUsingGroboIL(newMethod, extendedLocalVariables);

            return newMethod;
        }

        public ConcurrentBag<Delegate> delegates = new ConcurrentBag<Delegate>();


        public void EmitToStupidUsingGroboIL(DynamicMethod dynamicMethod, IList<LocalInfo> localVariables)
        {
            using (var il = new GroboIL(dynamicMethod))
            {
                il.VerificationKind = TypesAssignabilityVerificationKind.LowLevelOnly; // todo изменить
                var locals = new Dictionary<int, GroboIL.Local>();
                foreach (var localVariable in localVariables.OrderBy(x => x.LocalIndex))
                    locals.Add(localVariable.LocalIndex, il.DeclareLocal(localVariable.LocalType, localVariable.IsPinned));
                var labels = new Dictionary<int, GroboIL.Label>();
                foreach (AbstractInstruction abstractInstruction in instructions)
                {
                    ILInstruction instruction = abstractInstruction as ILInstruction;
                    if (instruction == null)
                        continue;

                    if (instruction.Code.OperandType == OperandType.InlineBrTarget
                        || instruction.Code.OperandType == OperandType.ShortInlineBrTarget)
                    {
                        var target = (int)instruction.Operand;
                        if (!labels.ContainsKey(target))
                            labels.Add(target, il.DefineLabel(target.ToString(), false));
                    }
                    else if (instruction.Code.OperandType == OperandType.InlineSwitch)
                    {
                        var targets = (int[])instruction.Operand;
                        foreach (var target in targets)
                        {
                            if (!labels.ContainsKey(target))
                                labels.Add(target, il.DefineLabel(target.ToString(), false));
                        }
                    }
                }

                Type constrained = null;
                bool tailcall = false;
                bool isVolatile = false;
                int? unaligned = null;
                bool asReadonly = false;

                for (int position = 0; position < instructions.Count; position++)
                {
                    AbstractInstruction abstractInstruction = instructions[position];
                    if (abstractInstruction is BeginExceptionInstruction)
                    {
                        il.BeginExceptionBlock();
                        continue;
                    }

                    if (abstractInstruction is BeginCatchInstruction)
                    {
                        il.BeginCatchBlock(((BeginCatchInstruction)abstractInstruction).ExceptionType);
                        continue;
                    }

                    if (abstractInstruction is BeginFinallyInstruction)
                    {
                        il.BeginFinallyBlock();
                        continue;
                    }

                    if (abstractInstruction is EndExceptionInstruction)
                    {
                        il.EndExceptionBlock();
                        continue;
                    }

                    var instruction = abstractInstruction as ILInstruction;

                    GroboIL.Label label;
                    if (labels.TryGetValue(instruction.Offset, out label))
                        il.MarkLabel(label);

                    switch ((int)(ushort)instruction.Code.Value)
                    {
                    case 0x00: // Nop
                        il.Nop();
                        break;
                    case 0x01: // Break
                        il.Break();
                        break;
                    case 0x02: // Ldarg_0
                        il.Ldarg(0);
                        break;
                    case 0x03: // Ldarg_1
                        il.Ldarg(1);
                        break;
                    case 0x04: // Ldarg_2
                        il.Ldarg(2);
                        break;
                    case 0x05: // Ldarg_3
                        il.Ldarg(3);
                        break;
                    case 0x06: // Ldloc_0
                        il.Ldloc(locals[0]);
                        break;
                    case 0x07: // Ldloc_1
                        il.Ldloc(locals[1]);
                        break;
                    case 0x08: // Ldloc_2
                        il.Ldloc(locals[2]);
                        break;
                    case 0x09: // Ldloc_3
                        il.Ldloc(locals[3]);
                        break;
                    case 0x0a: // Stloc_0
                        il.Stloc(locals[0]);
                        break;
                    case 0x0b: // Stloc_1
                        il.Stloc(locals[1]);
                        break;
                    case 0x0c: // Stloc_2
                        il.Stloc(locals[2]);
                        break;
                    case 0x0d: // Stloc_3
                        il.Stloc(locals[3]);
                        break;
                    case 0x0e: // Ldarg_S
                        il.Ldarg((byte)instruction.Operand);
                        break;
                    case 0x0f: // Ldarga_S
                        il.Ldarga((byte)instruction.Operand);
                        break;
                    case 0x10: // Starg_S
                        il.Starg((byte)instruction.Operand);
                        break;
                    case 0x11: // Ldloc_S
                        il.Ldloc(locals[(byte)instruction.Operand]);
                        break;
                    case 0x12: // Ldloca_S
                        il.Ldloca(locals[(byte)instruction.Operand]);
                        break;
                    case 0x13: // Stloc_S
                        il.Stloc(locals[(byte)instruction.Operand]);
                        break;
                    case 0x14: // Ldnull
                        il.Ldnull();
                        break;
                    case 0x15: // Ldc_I4_M1
                        il.Ldc_I4(-1);
                        break;
                    case 0x16: // Ldc_I4_0
                        il.Ldc_I4(0);
                        break;
                    case 0x17: // Ldc_I4_1
                        il.Ldc_I4(1);
                        break;
                    case 0x18: // Ldc_I4_2
                        il.Ldc_I4(2);
                        break;
                    case 0x19: // Ldc_I4_3
                        il.Ldc_I4(3);
                        break;
                    case 0x1a: // Ldc_I4_4
                        il.Ldc_I4(4);
                        break;
                    case 0x1b: // Ldc_I4_5
                        il.Ldc_I4(5);
                        break;
                    case 0x1c: // Ldc_I4_6
                        il.Ldc_I4(6);
                        break;
                    case 0x1d: // Ldc_I4_7
                        il.Ldc_I4(7);
                        break;
                    case 0x1e: // Ldc_I4_8
                        il.Ldc_I4(8);
                        break;
                    case 0x1f: // Ldc_I4_S
                        il.Ldc_I4((sbyte)instruction.Operand);
                        break;
                    case 0x20: // Ldc_I4
                        il.Ldc_I4((int)instruction.Operand);
                        break;
                    case 0x21: // Ldc_I8
                        il.Ldc_I8((long)instruction.Operand);
                        break;
                    case 0x22: // Ldc_R4
                        il.Ldc_R4((float)instruction.Operand);
                        break;
                    case 0x23: // Ldc_R8
                        il.Ldc_R8((double)instruction.Operand);
                        break;
                    case 0x25: // Dup
                        il.Dup();
                        break;
                    case 0x26: // Pop
                        il.Pop();
                        break;
                    case 0x27: // Jmp
                        il.Jmp((MethodInfo)instruction.Operand);
                        break;
                    case 0x28: // Call
                        var perhapsMethodInfo = instruction.Operand as MethodInfo;
                        if (perhapsMethodInfo != null)
                            il.Callnonvirt(perhapsMethodInfo, tailcall); // todo optionalParameterTypes ??? 
                        else
                            il.Call((ConstructorInfo)instruction.Operand);
                        break;
                    case 0x29: // Calli
                        throw new NotImplementedException();
                        break;
                    case 0x2a: // Ret
                        il.Ret();
                        break;
                    case 0x2b: // Br_S
                        il.Br(labels[(int)instruction.Operand]);
                        break;
                    case 0x2c: // Brfalse_S
                        il.Brfalse(labels[(int)instruction.Operand]);
                        break;
                    case 0x2d: // Brtrue_S
                        il.Brtrue(labels[(int)instruction.Operand]);
                        break;
                    case 0x2e: // Beq_S
                        il.Beq(labels[(int)instruction.Operand]);
                        break;
                    case 0x2f: // Bge_S
                        il.Bge(labels[(int)instruction.Operand], false);
                        break;
                    case 0x30: // Bgt_S
                        il.Bgt(labels[(int)instruction.Operand], false);
                        break;
                    case 0x31: // Ble_S
                        il.Ble(labels[(int)instruction.Operand], false);
                        break;
                    case 0x32: // Blt_S
                        il.Blt(labels[(int)instruction.Operand], false);
                        break;
                    case 0x33: // Bne_Un_S
                        il.Bne_Un(labels[(int)instruction.Operand]);
                        break;
                    case 0x34: // Bge_Un_S
                        il.Bge(labels[(int)instruction.Operand], true);
                        break;
                    case 0x35: // Bgt_Un_S
                        il.Bgt(labels[(int)instruction.Operand], true);
                        break;
                    case 0x36: // Ble_Un_S
                        il.Ble(labels[(int)instruction.Operand], true);
                        break;
                    case 0x37: // Blt_Un_S
                        il.Blt(labels[(int)instruction.Operand], true);
                        break;
                    case 0x38: // Br
                        il.Br(labels[(int)instruction.Operand]);
                        break;
                    case 0x39: // Brfalse
                        il.Brfalse(labels[(int)instruction.Operand]);
                        break;
                    case 0x3a: // Brtrue
                        il.Brtrue(labels[(int)instruction.Operand]);
                        break;
                    case 0x3b: // Beq
                        il.Beq(labels[(int)instruction.Operand]);
                        break;
                    case 0x3c: // Bge
                        il.Bge(labels[(int)instruction.Operand], false);
                        break;
                    case 0x3d: // Bgt
                        il.Bgt(labels[(int)instruction.Operand], false);
                        break;
                    case 0x3e: // Ble
                        il.Ble(labels[(int)instruction.Operand], false);
                        break;
                    case 0x3f: // Blt
                        il.Blt(labels[(int)instruction.Operand], false);
                        break;
                    case 0x40: // Bne_Un
                        il.Bne_Un(labels[(int)instruction.Operand]);
                        break;
                    case 0x41: // Bge_Un
                        il.Bge(labels[(int)instruction.Operand], true);
                        break;
                    case 0x42: // Bgt_Un
                        il.Bgt(labels[(int)instruction.Operand], true);
                        break;
                    case 0x43: // Ble_Un
                        il.Ble(labels[(int)instruction.Operand], true);
                        break;
                    case 0x44: // Blt_Un
                        il.Blt(labels[(int)instruction.Operand], true);
                        break;
                    case 0x45: // Switch
                        il.Switch(); // todo labels ???
                        break;
                    case 0x46: // Ldind_I1
                        il.Ldind(typeof(sbyte), isVolatile, unaligned); // todo оставшиеся параметры???
                        break;
                    case 0x47: // Ldind_U1
                        il.Ldind(typeof(byte), isVolatile, unaligned);
                        break;
                    case 0x48: // Ldind_I2
                        il.Ldind(typeof(short), isVolatile, unaligned);
                        break;
                    case 0x49: // Ldind_U2
                        il.Ldind(typeof(ushort), isVolatile, unaligned);
                        break;
                    case 0x4a: // Ldind_I4
                        il.Ldind(typeof(int), isVolatile, unaligned);
                        break;
                    case 0x4b: // Ldind_U4
                        il.Ldind(typeof(uint), isVolatile, unaligned);
                        break;
                    case 0x4c: // Ldind_I8
                        il.Ldind(typeof(long), isVolatile, unaligned);
                        break;
                    case 0x4d: // Ldind_I
                        il.Ldind(typeof(IntPtr), isVolatile, unaligned);
                        break;
                    case 0x4e: // Ldind_R4
                        il.Ldind(typeof(float), isVolatile, unaligned);
                        break;
                    case 0x4f: // Ldind_R8
                        il.Ldind(typeof(double), isVolatile, unaligned);
                        break;
                    case 0x50: // Ldind_Ref
                        il.Ldind(typeof(object), isVolatile, unaligned);
                        break;
                    case 0x51: // Stind_Ref
                        il.Stind(typeof(object), isVolatile, unaligned);
                        break;
                    case 0x52: // Stind_I1
                        il.Stind(typeof(sbyte), isVolatile, unaligned);
                        break;
                    case 0x53: // Stind_I2
                        il.Stind(typeof(short), isVolatile, unaligned);
                        break;
                    case 0x54: // Stind_I4
                        il.Stind(typeof(int), isVolatile, unaligned);
                        break;
                    case 0x55: // Stind_I8
                        il.Stind(typeof(long), isVolatile, unaligned);
                        break;
                    case 0x56: // Stind_R4
                        il.Stind(typeof(float), isVolatile, unaligned);
                        break;
                    case 0x57: // Stind_R8
                        il.Stind(typeof(double), isVolatile, unaligned);
                        break;
                    case 0x58: // Add
                        il.Add();
                        break;
                    case 0x59: // Sub
                        il.Sub();
                        break;
                    case 0x5a: // Mul
                        il.Mul();
                        break;
                    case 0x5b: // Div
                        il.Div(false);
                        break;
                    case 0x5c: // Div_Un
                        il.Div(true);
                        break;
                    case 0x5d: // Rem
                        il.Rem(false);
                        break;
                    case 0x5e: // Rem_Un
                        il.Rem(true);
                        break;
                    case 0x5f: // And
                        il.And();
                        break;
                    case 0x60: // Or
                        il.Or();
                        break;
                    case 0x61: // Xor
                        il.Xor();
                        break;
                    case 0x62: // Shl
                        il.Shl();
                        break;
                    case 0x63: // Shr
                        il.Shr(false);
                        break;
                    case 0x64: // Shr_Un
                        il.Shr(true);
                        break;
                    case 0x65: // Neg
                        il.Neg();
                        break;
                    case 0x66: // Not
                        il.Not();
                        break;
                    case 0x67: // Conv_I1
                        il.Conv<sbyte>();
                        break;
                    case 0x68: // Conv_I2
                        il.Conv<short>();
                        break;
                    case 0x69: // Conv_I4
                        il.Conv<int>();
                        break;
                    case 0x6a: // Conv_I8
                        il.Conv<long>();
                        break;
                    case 0x6b: // Conv_R4
                        il.Conv<float>();
                        break;
                    case 0x6c: // Conv_R8
                        il.Conv<double>();
                        break;
                    case 0x6d: // Conv_U4
                        il.Conv<uint>();
                        break;
                    case 0x6e: // Conv_U8
                        il.Conv<ulong>();
                        break;
                    case 0x6f: // Callvirt
                        il.Call((MethodInfo)instruction.Operand, constrained, tailcall, null, true); // todo optionalParameterTypes ???
                        break;
                    case 0x70: // Cpobj
                        il.Cpobj((Type)instruction.Operand);
                        break;
                    case 0x71: // Ldobj
                        il.Ldobj((Type)instruction.Operand, isVolatile, unaligned);
                        break;
                    case 0x72: // Ldstr
                        il.Ldstr((String)instruction.Operand);
                        break;
                    case 0x73: // Newobj
                        il.Newobj((ConstructorInfo)instruction.Operand);
                        break;
                    case 0x74: // Castclass
                        il.Castclass((Type)instruction.Operand);
                        break;
                    case 0x75: // Isinst
                        il.Isinst((Type)instruction.Operand);
                        break;
                    case 0x76: // Conv_R_Un
                        il.Conv_R_Un();
                        break;
                    case 0x79: // Unbox
                        throw new NotImplementedException();
                        break;
                    case 0x7a: // Throw
                        il.Throw();
                        break;
                    case 0x7b: // Ldfld
                        il.Ldfld((FieldInfo)instruction.Operand, isVolatile, unaligned);
                        break;
                    case 0x7c: // Ldflda
                        il.Ldflda((FieldInfo)instruction.Operand);
                        break;
                    case 0x7d: // Stfld
                        il.Stfld((FieldInfo)instruction.Operand, isVolatile, unaligned);
                        break;
                    case 0x7e: // Ldsfld
                        il.Ldfld((FieldInfo)instruction.Operand, isVolatile, unaligned);
                        break;
                    case 0x7f: // Ldsflda
                        il.Ldflda((FieldInfo)instruction.Operand);
                        break;
                    case 0x80: // Stsfld
                        il.Stfld((FieldInfo)instruction.Operand, isVolatile, unaligned);
                        break;
                    case 0x81: // Stobj
                        il.Stobj((Type)instruction.Operand, isVolatile, unaligned);
                        break;
                    case 0x82: // Conv_Ovf_I1_Un
                        il.Conv_Ovf<sbyte>(true);
                        break;
                    case 0x83: // Conv_Ovf_I2_Un
                        il.Conv_Ovf<short>(true);
                        break;
                    case 0x84: // Conv_Ovf_I4_Un
                        il.Conv_Ovf<int>(true);
                        break;
                    case 0x85: // Conv_Ovf_I8_Un
                        il.Conv_Ovf<long>(true);
                        break;
                    case 0x86: // Conv_Ovf_U1_Un
                        il.Conv_Ovf<byte>(true);
                        break;
                    case 0x87: // Conv_Ovf_U2_Un
                        il.Conv_Ovf<ushort>(true);
                        break;
                    case 0x88: // Conv_Ovf_U4_Un
                        il.Conv_Ovf<uint>(true);
                        break;
                    case 0x89: // Conv_Ovf_U8_Un
                        il.Conv_Ovf<ulong>(true);
                        break;
                    case 0x8a: // Conv_Ovf_I_Un
                        il.Conv_Ovf<IntPtr>(true);
                        break;
                    case 0x8b: // Conv_Ovf_U_Un
                        il.Conv_Ovf<UIntPtr>(true);
                        break;
                    case 0x8c: // Box
                        il.Box((Type)instruction.Operand);
                        break;
                    case 0x8d: // Newarr
                        il.Newarr((Type)instruction.Operand);
                        break;
                    case 0x8e: // Ldlen
                        il.Ldlen();
                        break;
                    case 0x8f: // Ldelema
                        il.Ldelema((Type)instruction.Operand, asReadonly);
                        break;
                    case 0x90: // Ldelem_I1
                        il.Ldelem(typeof(sbyte));
                        break;
                    case 0x91: // Ldelem_U1
                        il.Ldelem(typeof(byte));
                        break;
                    case 0x92: // Ldelem_I2
                        il.Ldelem(typeof(short));
                        break;
                    case 0x93: // Ldelem_U2
                        il.Ldelem(typeof(ushort));
                        break;
                    case 0x94: // Ldelem_I4
                        il.Ldelem(typeof(int));
                        break;
                    case 0x95: // Ldelem_U4
                        il.Ldelem(typeof(uint));
                        break;
                    case 0x96: // Ldelem_I8
                        il.Ldelem(typeof(long));
                        break;
                    case 0x97: // Ldelem_I
                        il.Ldelem(typeof(IntPtr));
                        break;
                    case 0x98: // Ldelem_R4
                        il.Ldelem(typeof(float));
                        break;
                    case 0x99: // Ldelem_R8
                        il.Ldelem(typeof(double));
                        break;
                    case 0x9a: // Ldelem_Ref
                        il.Ldelem(typeof(object));
                        break;
                    case 0x9b: // Stelem_I
                        il.Stelem(typeof(IntPtr));
                        break;
                    case 0x9c: // Stelem_I1
                        il.Stelem(typeof(sbyte));
                        break;
                    case 0x9d: // Stelem_I2
                        il.Stelem(typeof(short));
                        break;
                    case 0x9e: // Stelem_I4
                        il.Stelem(typeof(int));
                        break;
                    case 0x9f: // Stelem_I8
                        il.Stelem(typeof(long));
                        break;
                    case 0xa0: // Stelem_R4
                        il.Stelem(typeof(float));
                        break;
                    case 0xa1: // Stelem_R8
                        il.Stelem(typeof(double));
                        break;
                    case 0xa2: // Stelem_Ref
                        il.Stelem(typeof(object));
                        break;
                    case 0xa3: // Ldelem
                        il.Ldelem((Type)instruction.Operand);
                        break;
                    case 0xa4: // Stelem
                        il.Stelem((Type)instruction.Operand);
                        break;
                    case 0xa5: // Unbox_Any
                        il.Unbox_Any((Type)instruction.Operand);
                        break;
                    case 0xb3: // Conv_Ovf_I1
                        il.Conv_Ovf<sbyte>(false);
                        break;
                    case 0xb4: // Conv_Ovf_U1
                        il.Conv_Ovf<byte>(false);
                        break;
                    case 0xb5: // Conv_Ovf_I2
                        il.Conv_Ovf<short>(false);
                        break;
                    case 0xb6: // Conv_Ovf_U2
                        il.Conv_Ovf<ushort>(false);
                        break;
                    case 0xb7: // Conv_Ovf_I4
                        il.Conv_Ovf<int>(false);
                        break;
                    case 0xb8: // Conv_Ovf_U4
                        il.Conv_Ovf<uint>(false);
                        break;
                    case 0xb9: // Conv_Ovf_I8
                        il.Conv_Ovf<long>(false);
                        break;
                    case 0xba: // Conv_Ovf_U8
                        il.Conv_Ovf<ulong>(false);
                        break;
                    case 0xc2: // Refanyval
                        throw new NotSupportedException();
                        break;
                    case 0xc3: // Ckfinite
                        il.Ckfinite();
                        break;
                    case 0xc6: // Mkrefany
                        throw new NotSupportedException();
                        break;
                    case 0xd0: // Ldtoken
                        var perhapsFieldInfo = instruction.Operand as FieldInfo;
                        if (perhapsFieldInfo != null)
                        {
                            il.Ldtoken(perhapsFieldInfo);
                            break;
                        }
                        perhapsMethodInfo = instruction.Operand as MethodInfo;
                        if (perhapsMethodInfo != null)
                        {
                            il.Ldtoken(perhapsMethodInfo);
                            break;
                        }
                        il.Ldtoken((Type)instruction.Operand);
                        break;
                    case 0xd1: // Conv_U2
                        il.Conv<ushort>();
                        break;
                    case 0xd2: // Conv_U1
                        il.Conv<byte>();
                        break;
                    case 0xd3: // Conv_I
                        il.Conv<IntPtr>();
                        break;
                    case 0xd4: // Conv_Ovf_I
                        il.Conv_Ovf<IntPtr>(false);
                        break;
                    case 0xd5: // Conv_Ovf_U
                        il.Conv_Ovf<UIntPtr>(false);
                        break;
                    case 0xd6: // Add_Ovf
                        il.Add_Ovf(false);
                        break;
                    case 0xd7: // Add_Ovf_Un
                        il.Add_Ovf(true);
                        break;
                    case 0xd8: // Mul_Ovf
                        il.Mul_Ovf(false);
                        break;
                    case 0xd9: // Mul_Ovf_Un
                        il.Mul_Ovf(true);
                        break;
                    case 0xda: // Sub_Ovf
                        il.Sub_Ovf(false);
                        break;
                    case 0xdb: // Sub_Ovf_Un
                        il.Sub_Ovf(true);
                        break;
                    case 0xdc: // Endfinally
                        //throw new NotImplementedException();
                        break;
                    case 0xdd: // Leave
                        var nextInstruction = instructions[position + 1]; // надеюсь что после leave еще есть интсрукции
                        if (nextInstruction is BeginCatchInstruction
                            || nextInstruction is BeginFinallyInstruction)
                            break;

                        il.Leave(labels[(int)instruction.Operand]);
                        break;
                    case 0xde: // Leave_S
                        nextInstruction = instructions[position + 1]; // надеюсь что после leave еще есть интсрукции
                        if (nextInstruction is BeginCatchInstruction
                            || nextInstruction is BeginFinallyInstruction)
                            break;

                        il.Leave(labels[(int)instruction.Operand]);
                        break;
                    case 0xdf: // Stind_I
                        il.Stind(typeof(IntPtr), isVolatile, unaligned);
                        break;
                    case 0xe0: // Conv_U
                        il.Conv<UIntPtr>();
                        break;
                    case 0xf8: // Prefix7
                        throw new NotSupportedException();
                        break;
                    case 0xf9: // Prefix6
                        throw new NotSupportedException();
                        break;
                    case 0xfa: // Prefix5
                        throw new NotSupportedException();
                        break;
                    case 0xfb: // Prefix4
                        throw new NotSupportedException();
                        break;
                    case 0xfc: // Prefix3
                        throw new NotSupportedException();
                        break;
                    case 0xfd: // Prefix2
                        throw new NotSupportedException();
                        break;
                    case 0xfe: // Prefix1
                        throw new NotSupportedException();
                        break;
                    case 0xff: // Prefixref
                        throw new NotSupportedException();
                        break;
                    case 0xfe00: // Arglist
                        il.Arglist();
                        break;
                    case 0xfe01: // Ceq
                        il.Ceq();
                        break;
                    case 0xfe02: // Cgt
                        il.Cgt(false);
                        break;
                    case 0xfe03: // Cgt_Un
                        il.Cgt(true);
                        break;
                    case 0xfe04: // Clt
                        il.Clt(false);
                        break;
                    case 0xfe05: // Clt_Un
                        il.Clt(true);
                        break;
                    case 0xfe06: // Ldftn
                        il.Ldftn((MethodInfo)instruction.Operand);
                        break;
                    case 0xfe07: // Ldvirtftn
                        il.Ldvirtftn((MethodInfo)instruction.Operand);
                        break;
                    case 0xfe09: // Ldarg
                        il.Ldarg((ushort)instruction.Operand);
                        break;
                    case 0xfe0a: // Ldarga
                        il.Ldarga((ushort)instruction.Operand);
                        break;
                    case 0xfe0b: // Starg
                        il.Starg((ushort)instruction.Operand);
                        break;
                    case 0xfe0c: // Ldloc
                        il.Ldloc(locals[(ushort)instruction.Operand]);
                        break;
                    case 0xfe0d: // Ldloca
                        il.Ldloca(locals[(ushort)instruction.Operand]);
                        break;
                    case 0xfe0e: // Stloc
                        il.Stloc(locals[(ushort)instruction.Operand]);
                        break;
                    case 0xfe0f: // Localloc
                        throw new NotSupportedException();
                        break;
                    case 0xfe11: // Endfilter
                        throw new NotSupportedException();
                        break;
                    case 0xfe12: // Unaligned_
                        unaligned = (Byte)instruction.Operand;
                        continue;
                        break;
                    case 0xfe13: // Volatile_
                        isVolatile = true;
                        continue;
                        break;
                    case 0xfe14: // Tail_
                        tailcall = true;
                        continue;
                        break;
                    case 0xfe15: // Initobj
                        il.Initobj((Type)instruction.Operand);
                        break;
                    case 0xfe16: // Constrained_
                        constrained = (Type)instruction.Operand;
                        continue;
                        break;
                    case 0xfe17: // Cpblk
                        il.Cpblk(isVolatile, unaligned);
                        break;
                    case 0xfe18: // Initblk
                        il.Initblk(isVolatile, unaligned);
                        break;
                    case 0xfe1a: // Rethrow
                        il.Rethrow();
                        break;
                    case 0xfe1c: // Sizeof
                        throw new NotSupportedException();
                        break;
                    case 0xfe1d: // Refanytype
                        throw new NotSupportedException();
                        break;
                    case 0xfe1e: // Readonly_
                        asReadonly = true;
                        continue;
                        break;
                    }

                    constrained = null;
                    tailcall = false;
                    isVolatile = false;
                    unaligned = null;
                    asReadonly = false;
                }

                Console.WriteLine("zzzz");
            }
        }



        
        
    }
}