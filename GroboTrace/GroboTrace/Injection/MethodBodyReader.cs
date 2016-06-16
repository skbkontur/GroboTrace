using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Permissions;

using GrEmit;

namespace GroboTrace.Injection
{
    public class MethodBodyReader
    {
        /// <summary>
        ///     MethodBodyReader constructor
        /// </summary>
        /// <param name="mi">
        ///     The System.Reflection defined MethodInfo
        /// </param>
        public MethodBodyReader(MethodInfo mi)
        {
            this.mi = mi;
            var methodBody = mi.GetMethodBody();
            if(methodBody != null)
            {
                il = methodBody.GetILAsByteArray();
                ConstructInstructions(mi.Module);
            }
        }

        public MethodBodyReader(byte[] il, Module module)
        {
            this.il = il;
            ConstructInstructions(module);
        }

        /// <summary>
        ///     Constructs the array of ILInstructions according to the IL byte code.
        /// </summary>
        /// <param name="module"></param>
        private void ConstructInstructions(Module module)
        {
            var il = this.il;
            var position = 0;
            instructions = new List<AbstractInstruction>();
            while(position < il.Length)
            {
                var instruction = new ILInstruction();

                // get the operation code of the current instruction
                var code = OpCodes.Nop;
                ushort value = il[position++];
                if(value != 0xfe)
                    code = Globals.singleByteOpCodes[value];
                else
                {
                    value = il[position++];
                    code = Globals.multiByteOpCodes[value];
                }
                instruction.Code = code;
                instruction.Offset = position - 1;
                int metadataToken;
                // get the operand of the current operation
                switch(code.OperandType)
                {
                case OperandType.InlineBrTarget:
                    metadataToken = ReadInt32(ref position);
                    instruction.OperandData = metadataToken;
                    metadataToken += position;
                    instruction.Operand = metadataToken;
                    break;
                case OperandType.InlineField:
                    metadataToken = ReadInt32(ref position);
                    instruction.OperandData = metadataToken;
                    instruction.Operand = module.ResolveField(metadataToken);
                    break;
                case OperandType.InlineMethod:
                    metadataToken = ReadInt32(ref position);
                    instruction.OperandData = metadataToken;
                    instruction.Operand = module.ResolveMethod(metadataToken);
                    break;
                case OperandType.InlineSig:
                    metadataToken = ReadInt32(ref position);
                    instruction.OperandData = metadataToken;
                    instruction.Operand = module.ResolveSignature(metadataToken);
                    break;
                case OperandType.InlineTok:
                    metadataToken = ReadInt32(ref position);
                    instruction.OperandData = metadataToken;
                    try
                    {
                        instruction.Operand = module.ResolveType(metadataToken);
                    }
                    catch
                    {
                    }
                    // SSS : see what to do here
                    break;
                case OperandType.InlineType:
                    metadataToken = ReadInt32(ref position);
                    instruction.OperandData = metadataToken;
                    // now we call the ResolveType always using the generic attributes type in order
                    // to support decompilation of generic methods and classes

                    // thanks to the guys from code project who commented on this missing feature

                    instruction.Operand = module.ResolveType(metadataToken, mi.DeclaringType.GetGenericArguments(), mi.GetGenericArguments());
                    break;
                case OperandType.InlineI:
                    {
                        instruction.Operand = ReadInt32(ref position);
                        instruction.OperandData = instruction.Operand;
                        break;
                    }
                case OperandType.InlineI8:
                    {
                        instruction.Operand = ReadInt64(ref position);
                        instruction.OperandData = instruction.Operand;
                        break;
                    }
                case OperandType.InlineNone:
                    {
                        instruction.Operand = null;
                        instruction.OperandData = instruction.Operand;
                        break;
                    }
                case OperandType.InlineR:
                    {
                        instruction.Operand = ReadDouble(ref position);
                        instruction.OperandData = instruction.Operand;
                        break;
                    }
                case OperandType.InlineString:
                    {
                        metadataToken = ReadInt32(ref position);
                        instruction.OperandData = metadataToken;
                        instruction.Operand = module.ResolveString(metadataToken);
                        break;
                    }
                case OperandType.InlineSwitch:
                    {
                        var count = ReadInt32(ref position);
                        var casesAddresses = new int[count];
                        for(var i = 0; i < count; i++)
                            casesAddresses[i] = ReadInt32(ref position);
                        instruction.OperandData = casesAddresses;
                        var cases = new int[count];
                        for(var i = 0; i < count; i++)
                            cases[i] = position + casesAddresses[i];
                        instruction.Operand = cases;
                        break;
                    }
                case OperandType.InlineVar:
                    {
                        var index = ReadUInt16(ref position);
                        instruction.OperandData = index;
                        instruction.Operand = index;
                        break;
                    }
                case OperandType.ShortInlineBrTarget:
                    {
                        var sByte = ReadSByte(ref position);
                        instruction.OperandData = sByte;
                        instruction.Operand = sByte + position;
                        break;
                    }
                case OperandType.ShortInlineI:
                    {
                        instruction.Operand = ReadSByte(ref position);
                        instruction.OperandData = instruction.Operand;
                        break;
                    }
                case OperandType.ShortInlineR:
                    {
                        instruction.Operand = ReadSingle(ref position);
                        instruction.OperandData = instruction.Operand;
                        break;
                    }
                case OperandType.ShortInlineVar:
                    {
                        var index = ReadByte(ref position);
                        instruction.OperandData = index;
                        instruction.Operand = index;
                        break;
                    }
                default:
                    {
                        throw new Exception("Unknown operand type.");
                    }
                }
                instructions.Add(instruction);
            }
        }

        public object GetRefferencedOperand(Module module, int metadataToken)
        {
            var assemblyNames = module.Assembly.GetReferencedAssemblies();
            for(var i = 0; i < assemblyNames.Length; i++)
            {
                var modules = Assembly.Load(assemblyNames[i]).GetModules();
                for(var j = 0; j < modules.Length; j++)
                {
                    try
                    {
                        var t = modules[j].ResolveType(metadataToken);
                        return t;
                    }
                    catch
                    {
                    }
                }
            }
            return null;
            //System.Reflection.Assembly.Load(module.Assembly.GetReferencedAssemblies()[3]).GetModules()[0].ResolveType(metadataToken)
        }

        /// <summary>
        ///     Gets the IL code of the method
        /// </summary>
        /// <returns></returns>
        public string GetBodyCode()
        {
            var result = "";
            if(instructions == null) return result;
            foreach(AbstractInstruction abstractInstruction in instructions)
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

            if(opcode == OpCodes.Ldloc)
            {
                var idx = (ushort)operand;
                switch(idx)
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
            if(index == instructions.Count)
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
            foreach(AbstractInstruction abstractInstruction in instructions)
            {
                ILInstruction instruction = abstractInstruction as ILInstruction;
                if (instruction == null)
                    continue;

                if(instruction.Code.OperandType == OperandType.InlineBrTarget || instruction.Code.OperandType == OperandType.ShortInlineBrTarget)
                {
                    var target = (int)instruction.Operand;
                    if(target >= threshold)
                        instruction.Operand = target + size;
                }
                else if(instruction.Code.OperandType == OperandType.InlineSwitch)
                {
                    var targets = (int[])instruction.Operand;
                    for(var i = 0; i < targets.Length; ++i)
                    {
                        if(targets[i] >= threshold)
                            targets[i] += size;
                    }
                }
            }
            for(var i = index + 1; i < instructions.Count; ++i)
                instructions[i].Offset += size;
        }


        public void InsertBranchAt(int index, OpCode opCode, int targetIndex)
        {
            int targetOffset;
            if(targetIndex < instructions.Count)
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
                    if(target > threshold)
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

        public void EmitToStupidUsingGroboIL(DynamicMethod dynamicMethod, IList<LocalInfo> localVariables)
        {
            using(var il = new GroboIL(dynamicMethod))
            {
                il.VerificationKind = TypesAssignabilityVerificationKind.LowLevelOnly; // todo изменить
                var locals = new Dictionary<int, GroboIL.Local>();
                foreach(var localVariable in localVariables.OrderBy(x => x.LocalIndex))
                    locals.Add(localVariable.LocalIndex, il.DeclareLocal(localVariable.LocalType, localVariable.IsPinned));
                var labels = new Dictionary<int, GroboIL.Label>();
                foreach(AbstractInstruction abstractInstruction in instructions)
                {
                    ILInstruction instruction = abstractInstruction as ILInstruction;
                    if (instruction == null)
                        continue;

                    if(instruction.Code.OperandType == OperandType.InlineBrTarget
                       || instruction.Code.OperandType == OperandType.ShortInlineBrTarget)
                    {
                        var target = (int)instruction.Operand;
                        if(!labels.ContainsKey(target))
                            labels.Add(target, il.DefineLabel(target.ToString(), false));
                    }
                    else if(instruction.Code.OperandType == OperandType.InlineSwitch)
                    {
                        var targets = (int[])instruction.Operand;
                        foreach(var target in targets)
                        {
                            if(!labels.ContainsKey(target))
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
                    if(abstractInstruction is BeginExceptionInstruction)
                    {
                        il.BeginExceptionBlock();
                        continue;
                    }

                    if(abstractInstruction is BeginCatchInstruction)
                    {
                        il.BeginCatchBlock(((BeginCatchInstruction)abstractInstruction).ExceptionType);
                        continue;
                    }

                    if(abstractInstruction is BeginFinallyInstruction)
                    {
                        il.BeginFinallyBlock();
                        continue;
                    }

                    if(abstractInstruction is EndExceptionInstruction)
                    {
                        il.EndExceptionBlock();
                        continue;
                    }

                    var instruction = abstractInstruction as ILInstruction;

                    GroboIL.Label label;
                    if(labels.TryGetValue(instruction.Offset, out label))
                        il.MarkLabel(label);

                    switch((int)(ushort)instruction.Code.Value)
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
                        if(perhapsMethodInfo != null)
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
                        ;
                        break;
                    case 0x87: // Conv_Ovf_U2_Un
                        il.Conv_Ovf<ushort>(true);
                        ;
                        break;
                    case 0x88: // Conv_Ovf_U4_Un
                        il.Conv_Ovf<uint>(true);
                        break;
                    case 0x89: // Conv_Ovf_U8_Un
                        il.Conv_Ovf<ulong>(true);
                        break;
                    case 0x8a: // Conv_Ovf_I_Un
                        il.Conv_Ovf<IntPtr>(true);
                        ;
                        break;
                    case 0x8b: // Conv_Ovf_U_Un
                        il.Conv_Ovf<UIntPtr>(true);
                        ;
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
                        if(perhapsFieldInfo != null)
                        {
                            il.Ldtoken(perhapsFieldInfo);
                            break;
                        }
                        perhapsMethodInfo = instruction.Operand as MethodInfo;
                        if(perhapsMethodInfo != null)
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
                        throw new NotSupportedException();
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

        //public void EmitToStupid(DynamicMethod dynamicMethod, IList<LocalInfo> localVariables)
        //{
        //    var il = dynamicMethod.GetILGenerator();
        //    var locals = new Dictionary<int, LocalBuilder>();
        //    foreach(var localVariable in localVariables.OrderBy(x => x.LocalIndex))
        //        locals.Add(localVariable.LocalIndex, il.DeclareLocal(localVariable.LocalType, localVariable.IsPinned));
        //    var labels = new Dictionary<int, Label>();
        //    foreach(var instruction in instructions)
        //    {
        //        if(instruction.Code.OperandType == OperandType.InlineBrTarget
        //           || instruction.Code.OperandType == OperandType.ShortInlineBrTarget)
        //        {
        //            var target = (int)instruction.Operand;
        //            if(!labels.ContainsKey(target))
        //                labels.Add(target, il.DefineLabel());
        //        }
        //        else if(instruction.Code.OperandType == OperandType.InlineSwitch)
        //        {
        //            var targets = (int[])instruction.Operand;
        //            foreach(var target in targets)
        //            {
        //                if(!labels.ContainsKey(target))
        //                    labels.Add(target, il.DefineLabel());
        //            }
        //        }
        //    }
        //    foreach(var instruction in instructions)
        //    {
        //        Label label;
        //        if(labels.TryGetValue(instruction.Offset, out label))
        //            il.MarkLabel(label);
        //        switch(instruction.Code.OperandType)
        //        {
        //        case OperandType.InlineNone:
        //            il.Emit(instruction.Code);
        //            break;
        //        case OperandType.InlineBrTarget:
        //        case OperandType.ShortInlineBrTarget:
        //            // todo
        //            il.Emit(instruction.Code, labels[(int)instruction.Operand]);
        //            break;
        //        case OperandType.InlineField:
        //            il.Emit(instruction.Code, (FieldInfo)instruction.Operand);
        //            break;
        //        case OperandType.InlineI:
        //            il.Emit(instruction.Code, (int)instruction.Operand);
        //            break;
        //        case OperandType.InlineI8:
        //            il.Emit(instruction.Code, (long)instruction.Operand);
        //            break;
        //        case OperandType.InlineMethod:
        //            var method = instruction.Operand as MethodInfo;
        //            if(method != null)
        //                il.Emit(instruction.Code, method);
        //            else
        //                il.Emit(instruction.Code, (ConstructorInfo)instruction.Operand);
        //            break;
        //        case OperandType.InlineR:
        //            il.Emit(instruction.Code, (double)instruction.Operand);
        //            break;
        //        case OperandType.InlineSig:
        //            throw new InvalidOperationException();
        //        case OperandType.InlineString:
        //            il.Emit(instruction.Code, (string)instruction.Operand);
        //            break;
        //        case OperandType.InlineTok:
        //            {
        //                if(instruction.Operand is FieldInfo)
        //                    il.Emit(instruction.Code, (FieldInfo)instruction.Operand);
        //                else if(instruction.Operand is MethodInfo)
        //                    il.Emit(instruction.Code, (MethodInfo)instruction.Operand);
        //                else if(instruction.Operand is Type)
        //                    il.Emit(instruction.Code, (Type)instruction.Operand);
        //                else throw new InvalidOperationException();
        //            }
        //            break;
        //        case OperandType.InlineType:
        //            il.Emit(instruction.Code, (Type)instruction.Operand);
        //            break;
        //        case OperandType.ShortInlineI:
        //            il.Emit(instruction.Code, (sbyte)instruction.OperandData);
        //            break;
        //        case OperandType.InlineVar:
        //            il.Emit(instruction.Code, (ushort)instruction.OperandData);
        //            break;
        //        case OperandType.ShortInlineVar:
        //            il.Emit(instruction.Code, (byte)instruction.OperandData);
        //            break;
        //        case OperandType.ShortInlineR:
        //            il.Emit(instruction.Code, (float)instruction.OperandData);
        //            break;
        //        case OperandType.InlineSwitch:
        //            il.Emit(instruction.Code, ((int[])instruction.Operand).Select(target => labels[target]).ToArray());
        //            break;
        //        }
        //    }
        //}

        //public unsafe void EmitTo(DynamicMethod dynamicMethod, int maxStackSize, bool initLocals, byte[] localSignature)
        //{
        //    var dynamicIlInfo = dynamicMethod.GetDynamicILInfo();
        //    var lastInstruction = instructions.Last();
        //    var result = new byte[lastInstruction.Offset + lastInstruction.Size];
        //    fixed(byte* r = &result[0])
        //    {
        //        var p = r;
        //        foreach(var instruction in instructions)
        //        {
        //            if(instruction.Code.Size > 1)
        //                *p++ = (byte)(instruction.Code.Value >> 8);
        //            *p++ = (byte)(instruction.Code.Value & 0xFF);
        //            switch(instruction.Code.OperandType)
        //            {
        //            case OperandType.InlineBrTarget:
        //                {
        //                    var diff = instruction.Offset - (int)instruction.Operand;
        //                    *(int*)p = diff;
        //                    p += 4;
        //                }
        //                break;
        //            case OperandType.InlineField:
        //                {
        //                    var field = (FieldInfo)instruction.Operand;
        //                    var metadataToken = dynamicIlInfo.GetTokenFor(field.FieldHandle);
        //                    *(int*)p = metadataToken;
        //                    p += 4;
        //                }
        //                break;
        //            case OperandType.InlineI:
        //                {
        //                    var value = (int)instruction.Operand;
        //                    *(int*)p = value;
        //                    p += 4;
        //                }
        //                break;
        //            case OperandType.InlineI8:
        //                {
        //                    var value = (long)instruction.Operand;
        //                    *(long*)p = value;
        //                    p += 8;
        //                }
        //                break;
        //            case OperandType.InlineMethod:
        //                {
        //                    var method = (MethodBase)instruction.Operand;
        //                    var metadataToken = dynamicIlInfo.GetTokenFor(method.MethodHandle);
        //                    *(int*)p = metadataToken;
        //                    p += 4;
        //                }
        //                break;
        //            case OperandType.InlineR:
        //                {
        //                    var value = (double)instruction.Operand;
        //                    *(double*)p = value;
        //                    p += 8;
        //                }
        //                break;
        //            case OperandType.InlineSig:
        //                {
        //                    var signature = (byte[])instruction.Operand;
        //                    var metadataToken = dynamicIlInfo.GetTokenFor(signature);
        //                    *(int*)p = metadataToken;
        //                    p += 4;
        //                }
        //                break;
        //            case OperandType.InlineString:
        //                {
        //                    var str = (string)instruction.Operand;
        //                    var metadataToken = dynamicIlInfo.GetTokenFor(str);
        //                    *(int*)p = metadataToken;
        //                    p += 4;
        //                }
        //                break;
        //            case OperandType.InlineTok:
        //                {
        //                    int metadataToken;
        //                    if(instruction.Operand is FieldInfo)
        //                        metadataToken = dynamicIlInfo.GetTokenFor(((FieldInfo)instruction.Operand).FieldHandle);
        //                    else if(instruction.Operand is MethodBase)
        //                        metadataToken = dynamicIlInfo.GetTokenFor(((MethodBase)instruction.Operand).MethodHandle);
        //                    else if(instruction.Operand is Type)
        //                        metadataToken = dynamicIlInfo.GetTokenFor(((Type)instruction.Operand).TypeHandle);
        //                    else throw new InvalidOperationException();
        //                    *(int*)p = metadataToken;
        //                    p += 4;
        //                }
        //                break;
        //            case OperandType.InlineType:
        //                {
        //                    var metadataToken = dynamicIlInfo.GetTokenFor(((Type)instruction.Operand).TypeHandle);
        //                    *(int*)p = metadataToken;
        //                    p += 4;
        //                }
        //                break;
        //            case OperandType.ShortInlineI:
        //                {
        //                    var metadataToken = (sbyte)instruction.OperandData;
        //                    *(sbyte*)p = metadataToken;
        //                    p += 1;
        //                }
        //                break;
        //            case OperandType.InlineVar:
        //                {
        //                    var metadataToken = (short)instruction.OperandData;
        //                    *(short*)p = metadataToken;
        //                    p += 2;
        //                }
        //                break;
        //            case OperandType.ShortInlineR:
        //                {
        //                    var metadataToken = (float)instruction.OperandData;
        //                    *(float*)p = metadataToken;
        //                    p += 2;
        //                }
        //                break;
        //            case OperandType.ShortInlineVar:
        //                {
        //                    var metadataToken = (byte)instruction.OperandData;
        //                    *(byte*)p = metadataToken;
        //                    p += 1;
        //                }
        //                break;
        //            case OperandType.ShortInlineBrTarget:
        //                {
        //                    var diff = instruction.Offset - (int)instruction.Operand;
        //                    if(diff >= 128 || diff < -128)
        //                        throw new InvalidOperationException();
        //                    *(sbyte*)p = (sbyte)diff;
        //                    p += 1;
        //                }
        //                break;
        //            case OperandType.InlineSwitch:
        //                throw new NotImplementedException();
        //            }
        //        }
        //    }
        //    var code = new MethodBodyReader(result, dynamicMethod.Module).GetBodyCode();
        //    Console.WriteLine(code);
        //    dynamicIlInfo.SetCode(result, maxStackSize);
        //    dynamicIlInfo.SetLocalSignature(localSignature);
        //    dynamicMethod.InitLocals = initLocals;
        //}

        public List<AbstractInstruction> instructions;
        protected byte[] il;
        private readonly MethodInfo mi;

        #region il read methods

        private int ReadInt16(ref int position)
        {
            position += 2;
            return BitConverter.ToInt16(il, position - 2);
        }

        private ushort ReadUInt16(ref int position)
        {
            position += 2;
            return BitConverter.ToUInt16(il, position - 2);
        }

        private int ReadInt32(ref int position)
        {
            position += 4;
            return BitConverter.ToInt32(il, position - 4);
        }

        private long ReadInt64(ref int position)
        {
            position += 8;
            return BitConverter.ToInt64(il, position - 8);
        }

        private double ReadDouble(ref int position)
        {
            position += 8;
            return BitConverter.ToDouble(il, position - 8);
        }

        private sbyte ReadSByte(ref int position)
        {
            position += 1;
            return (sbyte)il[position - 1];
        }

        private byte ReadByte(ref int position)
        {
            position += 1;
            return il[position - 1];
        }

        private float ReadSingle(ref int position)
        {
            position += 4;
            return BitConverter.ToSingle(il, position - 4);
        }

        #endregion
    }
}