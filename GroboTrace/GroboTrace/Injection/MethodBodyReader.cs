using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

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
            instructions = new List<ILInstruction>();
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
                        instruction.Operand = (int)index;
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
                        instruction.Operand = (int)index;
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
            if(instructions != null)
            {
                for(var i = 0; i < instructions.Count; i++)
                    result += instructions[i].GetCode() + "\n";
            }
            return result;
        }

        public void InsertAt(int index, OpCode opcode, object operand)
        {
            var threshold = instructions[index].Offset;
            var newInstruction = new ILInstruction {Code = opcode, Operand = operand, Offset = threshold};
            var size = newInstruction.Size;
            instructions.Insert(index, newInstruction);
            foreach(var instruction in instructions)
            {
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

        public void EmitToStupid(DynamicMethod dynamicMethod, IList<LocalVariableInfo> localVariables)
        {
            var il = dynamicMethod.GetILGenerator();
            var locals = new Dictionary<int, LocalBuilder>();
            foreach(var localVariable in localVariables.OrderBy(x => x.LocalIndex))
                locals.Add(localVariable.LocalIndex, il.DeclareLocal(localVariable.LocalType, localVariable.IsPinned));
            var labels = new Dictionary<int, Label>();
            foreach(var instruction in instructions)
            {
                if(instruction.Code.OperandType == OperandType.InlineBrTarget
                   || instruction.Code.OperandType == OperandType.ShortInlineBrTarget)
                {
                    var target = (int)instruction.Operand;
                    if(!labels.ContainsKey(target))
                        labels.Add(target, il.DefineLabel());
                }
                else if(instruction.Code.OperandType == OperandType.InlineSwitch)
                {
                    var targets = (int[])instruction.Operand;
                    foreach(var target in targets)
                    {
                        if(!labels.ContainsKey(target))
                            labels.Add(target, il.DefineLabel());
                    }
                }
            }
            foreach(var instruction in instructions)
            {
                Label label;
                if(labels.TryGetValue(instruction.Offset, out label))
                    il.MarkLabel(label);
                switch(instruction.Code.OperandType)
                {
                case OperandType.InlineNone:
                    il.Emit(instruction.Code);
                    break;
                case OperandType.InlineBrTarget:
                case OperandType.ShortInlineBrTarget:
                    il.Emit(instruction.Code, labels[(int)instruction.Operand]);
                    break;
                case OperandType.InlineField:
                    il.Emit(instruction.Code, (FieldInfo)instruction.Operand);
                    break;
                case OperandType.InlineI:
                    il.Emit(instruction.Code, (int)instruction.Operand);
                    break;
                case OperandType.InlineI8:
                    il.Emit(instruction.Code, (ulong)instruction.Operand);
                    break;
                case OperandType.InlineMethod:
                    il.Emit(instruction.Code, (MethodInfo)instruction.Operand);
                    break;
                case OperandType.InlineR:
                    il.Emit(instruction.Code, (double)instruction.Operand);
                    break;
                case OperandType.InlineSig:
                    throw new InvalidOperationException();
                case OperandType.InlineString:
                    il.Emit(instruction.Code, (string)instruction.Operand);
                    break;
                case OperandType.InlineTok:
                    {
                        if(instruction.Operand is FieldInfo)
                            il.Emit(instruction.Code, (FieldInfo)instruction.Operand);
                        else if(instruction.Operand is MethodInfo)
                            il.Emit(instruction.Code, (MethodInfo)instruction.Operand);
                        else if(instruction.Operand is Type)
                            il.Emit(instruction.Code, (Type)instruction.Operand);
                        else throw new InvalidOperationException();
                    }
                    break;
                case OperandType.InlineType:
                    il.Emit(instruction.Code, (Type)instruction.Operand);
                    break;
                case OperandType.ShortInlineI:
                    il.Emit(instruction.Code, (sbyte)instruction.OperandData);
                    break;
                case OperandType.InlineVar:
                    il.Emit(instruction.Code, (short)instruction.OperandData);
                    break;
                case OperandType.ShortInlineVar:
                    il.Emit(instruction.Code, (byte)instruction.OperandData);
                    break;
                case OperandType.ShortInlineR:
                    il.Emit(instruction.Code, (float)instruction.OperandData);
                    break;
                case OperandType.InlineSwitch:
                    il.Emit(instruction.Code, ((int[])instruction.Operand).Select(target => labels[target]).ToArray());
                    break;
                }
            }
        }

        public unsafe void EmitTo(DynamicMethod dynamicMethod, int maxStackSize, bool initLocals, byte[] localSignature)
        {
            var dynamicIlInfo = dynamicMethod.GetDynamicILInfo();
            var lastInstruction = instructions.Last();
            var result = new byte[lastInstruction.Offset + lastInstruction.Size];
            fixed(byte* r = &result[0])
            {
                var p = r;
                foreach(var instruction in instructions)
                {
                    if(instruction.Code.Size > 1)
                        *p++ = (byte)(instruction.Code.Value >> 8);
                    *p++ = (byte)(instruction.Code.Value & 0xFF);
                    switch(instruction.Code.OperandType)
                    {
                    case OperandType.InlineBrTarget:
                        {
                            var diff = instruction.Offset - (int)instruction.Operand;
                            *(int*)p = diff;
                            p += 4;
                        }
                        break;
                    case OperandType.InlineField:
                        {
                            var field = (FieldInfo)instruction.Operand;
                            var metadataToken = dynamicIlInfo.GetTokenFor(field.FieldHandle);
                            *(int*)p = metadataToken;
                            p += 4;
                        }
                        break;
                    case OperandType.InlineI:
                        {
                            var value = (int)instruction.Operand;
                            *(int*)p = value;
                            p += 4;
                        }
                        break;
                    case OperandType.InlineI8:
                        {
                            var value = (ulong)instruction.Operand;
                            *(ulong*)p = value;
                            p += 8;
                        }
                        break;
                    case OperandType.InlineMethod:
                        {
                            var method = (MethodBase)instruction.Operand;
                            var metadataToken = dynamicIlInfo.GetTokenFor(method.MethodHandle);
                            *(int*)p = metadataToken;
                            p += 4;
                        }
                        break;
                    case OperandType.InlineR:
                        {
                            var value = (double)instruction.Operand;
                            *(double*)p = value;
                            p += 8;
                        }
                        break;
                    case OperandType.InlineSig:
                        {
                            var signature = (byte[])instruction.Operand;
                            var metadataToken = dynamicIlInfo.GetTokenFor(signature);
                            *(int*)p = metadataToken;
                            p += 4;
                        }
                        break;
                    case OperandType.InlineString:
                        {
                            var str = (string)instruction.Operand;
                            var metadataToken = dynamicIlInfo.GetTokenFor(str);
                            *(int*)p = metadataToken;
                            p += 4;
                        }
                        break;
                    case OperandType.InlineTok:
                        {
                            int metadataToken;
                            if(instruction.Operand is FieldInfo)
                                metadataToken = dynamicIlInfo.GetTokenFor(((FieldInfo)instruction.Operand).FieldHandle);
                            else if(instruction.Operand is MethodBase)
                                metadataToken = dynamicIlInfo.GetTokenFor(((MethodBase)instruction.Operand).MethodHandle);
                            else if(instruction.Operand is Type)
                                metadataToken = dynamicIlInfo.GetTokenFor(((Type)instruction.Operand).TypeHandle);
                            else throw new InvalidOperationException();
                            *(int*)p = metadataToken;
                            p += 4;
                        }
                        break;
                    case OperandType.InlineType:
                        {
                            var metadataToken = dynamicIlInfo.GetTokenFor(((Type)instruction.Operand).TypeHandle);
                            *(int*)p = metadataToken;
                            p += 4;
                        }
                        break;
                    case OperandType.ShortInlineI:
                        {
                            var metadataToken = (sbyte)instruction.OperandData;
                            *(sbyte*)p = metadataToken;
                            p += 1;
                        }
                        break;
                    case OperandType.InlineVar:
                        {
                            var metadataToken = (short)instruction.OperandData;
                            *(short*)p = metadataToken;
                            p += 2;
                        }
                        break;
                    case OperandType.ShortInlineR:
                        {
                            var metadataToken = (float)instruction.OperandData;
                            *(float*)p = metadataToken;
                            p += 2;
                        }
                        break;
                    case OperandType.ShortInlineVar:
                        {
                            var metadataToken = (byte)instruction.OperandData;
                            *(byte*)p = metadataToken;
                            p += 1;
                        }
                        break;
                    case OperandType.ShortInlineBrTarget:
                        {
                            var diff = instruction.Offset - (int)instruction.Operand;
                            if(diff >= 128 || diff < -128)
                                throw new InvalidOperationException();
                            *(sbyte*)p = (sbyte)diff;
                            p += 1;
                        }
                        break;
                    case OperandType.InlineSwitch:
                        throw new NotImplementedException();
                    }
                }
            }
            var code = new MethodBodyReader(result, dynamicMethod.Module).GetBodyCode();
            Console.WriteLine(code);
            dynamicIlInfo.SetCode(result, maxStackSize);
            dynamicIlInfo.SetLocalSignature(localSignature);
            dynamicMethod.InitLocals = initLocals;
        }

        public List<ILInstruction> instructions;
        protected byte[] il;
        private readonly MethodInfo mi;

        #region il read methods

        private int ReadInt16(ref int position)
        {
            return il[position++] | (il[position++] << 8);
        }

        private ushort ReadUInt16(ref int position)
        {
            return (ushort)(il[position++] | (il[position++] << 8));
        }

        private int ReadInt32(ref int position)
        {
            return il[position++] | (il[position++] << 8) | (il[position++] << 0x10) | (il[position++] << 0x18);
        }

        private ulong ReadInt64(ref int position)
        {
            return (ulong)(il[position++] | (il[position++] << 8) | (il[position++] << 0x10) | (il[position++] << 0x18) | (il[position++] << 0x20) | (il[position++] << 0x28) | (il[position++] << 0x30) | (il[position++] << 0x38));
        }

        private double ReadDouble(ref int position)
        {
            return il[position++] | (il[position++] << 8) | (il[position++] << 0x10) | (il[position++] << 0x18) | (il[position++] << 0x20) | (il[position++] << 0x28) | (il[position++] << 0x30) | (il[position++] << 0x38);
        }

        private sbyte ReadSByte(ref int position)
        {
            return (sbyte)il[position++];
        }

        private byte ReadByte(ref int position)
        {
            return il[position++];
        }

        private float ReadSingle(ref int position)
        {
            return il[position++] | (il[position++] << 8) | (il[position++] << 0x10) | (il[position++] << 0x18);
        }

        #endregion
    }
}