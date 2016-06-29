using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace GroboTrace.Injection
{
    public class ILBytesReader
    {
        private readonly byte[] ilBytes;
        private readonly MethodInfo methodInfo;
        private readonly Module module;
        private List<AbstractInstruction> instructions;

        public ILBytesReader(MethodInfo methodInfo)
        {
            this.methodInfo = methodInfo;
            ilBytes = this.methodInfo.GetMethodBody().GetILAsByteArray();
            module = this.methodInfo.Module;
        }

        public List<AbstractInstruction> GetInstructionsList()
        {
            if (instructions != null) return instructions;
            ConstructInstructions();
            return instructions;
        }

        /// <summary>
        ///     Constructs the list of ILInstructions according to the IL byte code.
        /// </summary>
        private void ConstructInstructions()
        {
            var position = 0;
            instructions = new List<AbstractInstruction>();
            while (position < ilBytes.Length)
            {
                var instruction = new ILInstruction();

                // get the operation code of the current instruction
                var code = OpCodes.Nop;
                ushort value = ilBytes[position++];
                if (value != 0xfe)
                    code = Globals.singleByteOpCodes[value];
                else
                {
                    value = ilBytes[position++];
                    code = Globals.multiByteOpCodes[value];
                }
                instruction.Code = code;
                instruction.Offset = position - 1;
                int metadataToken;
                // get the operand of the current operation
                switch (code.OperandType)
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

                    instruction.Operand = module.ResolveType(metadataToken, methodInfo.DeclaringType.GetGenericArguments(), methodInfo.GetGenericArguments());
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
                        for (var i = 0; i < count; i++)
                            casesAddresses[i] = ReadInt32(ref position);
                        instruction.OperandData = casesAddresses;
                        var cases = new int[count];
                        for (var i = 0; i < count; i++)
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

        // todo разобраться с этим
        public object GetRefferencedOperand(Module module, int metadataToken)
        {
            var assemblyNames = module.Assembly.GetReferencedAssemblies();
            for (var i = 0; i < assemblyNames.Length; i++)
            {
                var modules = Assembly.Load(assemblyNames[i]).GetModules();
                for (var j = 0; j < modules.Length; j++)
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
        }

        private int ReadInt16(ref int position)
        {
            position += 2;
            return BitConverter.ToInt16(ilBytes, position - 2);
        }

        private ushort ReadUInt16(ref int position)
        {
            position += 2;
            return BitConverter.ToUInt16(ilBytes, position - 2);
        }

        private int ReadInt32(ref int position)
        {
            position += 4;
            return BitConverter.ToInt32(ilBytes, position - 4);
        }

        private long ReadInt64(ref int position)
        {
            position += 8;
            return BitConverter.ToInt64(ilBytes, position - 8);
        }

        private double ReadDouble(ref int position)
        {
            position += 8;
            return BitConverter.ToDouble(ilBytes, position - 8);
        }

        private sbyte ReadSByte(ref int position)
        {
            position += 1;
            return (sbyte)ilBytes[position - 1];
        }

        private byte ReadByte(ref int position)
        {
            position += 1;
            return ilBytes[position - 1];
        }

        private float ReadSingle(ref int position)
        {
            position += 4;
            return BitConverter.ToSingle(ilBytes, position - 4);
        }

    }
}