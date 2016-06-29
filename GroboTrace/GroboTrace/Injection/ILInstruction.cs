using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace GroboTrace.Injection
{
    public class ILInstruction : AbstractInstruction
    {
        
        public override int Size
        {
            get
            {
                var size = Code.Size;
                switch (Code.OperandType)
                {
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    size += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    size += 8;
                    break;
                case OperandType.InlineVar:
                    size += 2;
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    size += 1;
                    break;
                case OperandType.InlineSwitch:
                    size += 4;
                    size += 4 * ((int[])Operand).Length;
                    break;
                }
                return size;
            }
        }

        public OpCode Code { get; set; }

        public object Operand { get; set; }

        public object OperandData { get; set; }

        public override int Offset { get; set; }

        /// <summary>
        ///     Returns a friendly string representation of this instruction
        /// </summary>
        /// <returns></returns>
        public string GetCode()
        {
            var result = "";
            result += GetExpandedOffset(Offset) + " : " + Code;
            if (Operand != null)
            {
                switch (Code.OperandType)
                {
                case OperandType.InlineField:
                    var fOperand = ((FieldInfo)Operand);
                    result += " " + Globals.ProcessSpecialTypes(fOperand.FieldType.ToString()) + " " +
                              Globals.ProcessSpecialTypes(fOperand.ReflectedType.ToString()) +
                              "::" + fOperand.Name + "";
                    break;
                case OperandType.InlineMethod:
                    try
                    {
                        var mOperand = (MethodInfo)Operand;
                        result += " ";
                        if (!mOperand.IsStatic) result += "instance ";
                        result += Globals.ProcessSpecialTypes(mOperand.ReturnType.ToString()) +
                                  " " + Globals.ProcessSpecialTypes(mOperand.ReflectedType.ToString()) +
                                  "::" + mOperand.Name + "()";
                    }
                    catch
                    {
                        try
                        {
                            var mOperand = (ConstructorInfo)Operand;
                            result += " ";
                            if (!mOperand.IsStatic) result += "instance ";
                            result += "void " +
                                      Globals.ProcessSpecialTypes(mOperand.ReflectedType.ToString()) +
                                      "::" + mOperand.Name + "()";
                        }
                        catch
                        {
                        }
                    }
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.InlineBrTarget:
                    result += " " + GetExpandedOffset((int)Operand);
                    break;
                case OperandType.InlineType:
                    result += " " + Globals.ProcessSpecialTypes(Operand.ToString());
                    break;
                case OperandType.InlineString:
                    if (Operand.ToString() == "\r\n") result += " \"\\r\\n\"";
                    else result += " \"" + Operand + "\"";
                    break;
                case OperandType.ShortInlineVar:
                    result += Operand.ToString();
                    break;
                case OperandType.InlineI:
                case OperandType.InlineI8:
                case OperandType.InlineR:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineR:
                    result += " " + Operand;
                    break;
                case OperandType.InlineTok:
                    if (Operand is Type)
                        result += ((Type)Operand).FullName;
                    else
                        result += "not supported";
                    break;
                case OperandType.InlineVar:
                    result += " " + Operand;
                    break;
                default:
                    result += "not supported";
                    break;
                }
            }
            return result;
        }

        /// <summary>
        ///     Add enough zeros to a number as to be represented on 4 characters
        /// </summary>
        /// <param name="offset">
        ///     The number that must be represented on 4 characters
        /// </param>
        /// <returns>
        /// </returns>
        private string GetExpandedOffset(long offset)
        {
            var result = offset.ToString();
            for (var i = 0; result.Length < 4; i++)
                result = "0" + result;
            return result;
        }
    }

    public static class Globals
    {
        static Globals()
        {
            LoadOpCodes();
        }

        public static void LoadOpCodes()
        {
            singleByteOpCodes = new OpCode[0x100];
            multiByteOpCodes = new OpCode[0x100];
            var infoArray1 = typeof(OpCodes).GetFields();
            for (var num1 = 0; num1 < infoArray1.Length; num1++)
            {
                var info1 = infoArray1[num1];
                if (info1.FieldType == typeof(OpCode))
                {
                    var code1 = (OpCode)info1.GetValue(null);
                    var num2 = (ushort)code1.Value;
                    if (num2 < 0x100)
                        singleByteOpCodes[(int)num2] = code1;
                    else
                    {
                        if ((num2 & 0xff00) != 0xfe00)
                            throw new Exception("Invalid OpCode.");
                        multiByteOpCodes[num2 & 0xff] = code1;
                    }
                }
            }
        }

        /// <summary>
        ///     Retrieve the friendly name of a type
        /// </summary>
        /// <param name="typeName">
        ///     The complete name to the type
        /// </param>
        /// <returns>
        ///     The simplified name of the type (i.e. "int" instead f System.Int32)
        /// </returns>
        public static string ProcessSpecialTypes(string typeName)
        {
            var result = typeName;
            switch (typeName)
            {
            case "System.string":
            case "System.String":
            case "String":
                result = "string";
                break;
            case "System.Int32":
            case "Int":
            case "Int32":
                result = "int";
                break;
            }
            return result;
        }

        public static Dictionary<int, object> Cache = new Dictionary<int, object>();

        public static OpCode[] multiByteOpCodes;
        public static OpCode[] singleByteOpCodes;
        public static Module[] modules = null;

        //public static string SpaceGenerator(int count)
        //{
        //    string result = "";
        //    for (int i = 0; i < count; i++) result += " ";
        //    return result;
        //}

        //public static string AddBeginSpaces(string source, int count)
        //{
        //    string[] elems = source.Split('\n');
        //    string result = "";
        //    for (int i = 0; i < elems.Length; i++)
        //    {
        //        result += SpaceGenerator(count) + elems[i] + "\n";
        //    }
        //    return result;
        //}
    }
}