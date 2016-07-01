using System;
using System.Runtime.InteropServices;

using GroboTrace.Mono.Cecil.Metadata;
using GroboTrace.Mono.Cecil.PE;

namespace GroboTrace.Mono.Cecil.Cil
{
    public class ParsedMethodSignature
    {
        public byte CallingConvention;
        public bool HasThis;
        public bool ExplicitThis;
        public byte[] ReturnTypeSignature;
        public int ParamCount;
    }

    internal sealed unsafe class MethodSignatureReader : RawByteBuffer
    {
        public MethodSignatureReader(byte* buffer)
            : base(buffer)
        {
        }

        public ParsedMethodSignature Read()
        {
            var calling_convention = ReadByte();

            const byte has_this = 0x20;
            const byte explicit_this = 0x40;
            bool hasThis = false;
            bool explicitThis = false;

            if ((calling_convention & has_this) != 0)
            {
                hasThis = true;
                calling_convention = (byte)(calling_convention & ~has_this);
            }

            if ((calling_convention & explicit_this) != 0)
            {
                explicitThis = true;
                calling_convention = (byte)(calling_convention & ~explicit_this);
            }

            if((calling_convention & 0x10) != 0)
            {
                // arity
                ReadCompressedUInt32();
            }

            // param_count
            var param_count = ReadCompressedUInt32();

            int start = position;
            ReadTypeSignature();
            int end = position;
            var returnTypeSignature = new byte[end - start];
            Marshal.Copy((IntPtr)(buffer + start), returnTypeSignature, 0, returnTypeSignature.Length);
            return new ParsedMethodSignature
                {
                    CallingConvention = calling_convention,
                    HasThis = hasThis,
                    ExplicitThis = explicitThis,
                    ParamCount = (int)param_count,
                    ReturnTypeSignature = returnTypeSignature
                };
        }

        private void ReadTypeSignature()
        {
            ReadTypeSignature((ElementType)ReadByte());
        }

        private void ReadTypeTokenSignature()
        {
            ReadCompressedUInt32();
        }

        private void ReadMethodSignature()
        {
            var calling_convention = ReadByte();

            if((calling_convention & 0x10) != 0)
            {
                // arity
                ReadCompressedUInt32();
            }

            var param_count = ReadCompressedUInt32();

            // return type
            ReadTypeSignature();

            if(param_count == 0)
                return;

            for(int i = 0; i < param_count; i++)
                ReadTypeSignature();
        }

        private void ReadTypeSignature(ElementType etype)
        {
            switch(etype)
            {
            case ElementType.ValueType:
                ReadTypeTokenSignature();
                break;
            case ElementType.Class:
                ReadTypeTokenSignature();
                break;
            case ElementType.Ptr:
                ReadTypeSignature();
                break;
            case ElementType.FnPtr:
                ReadMethodSignature();
                return;
            case ElementType.ByRef:
                ReadTypeSignature();
                break;
            case ElementType.Pinned:
                ReadTypeSignature();
                break;
            case ElementType.SzArray:
                ReadTypeSignature();
                break;
            case ElementType.Array:
                ReadArrayTypeSignature();
                break;
            case ElementType.CModOpt:
                ReadTypeTokenSignature();
                ReadTypeSignature();
                break;
            case ElementType.CModReqD:
                ReadTypeTokenSignature();
                ReadTypeSignature();
                break;
            case ElementType.Sentinel:
                ReadTypeSignature();
                break;
            case ElementType.Var:
                ReadCompressedUInt32();
                break;
            case ElementType.MVar:
                ReadCompressedUInt32();
                break;
            case ElementType.GenericInst:
                {
                    // attrs
                    ReadByte();
                    // element_type
                    ReadTypeTokenSignature();

                    ReadGenericInstanceSignature();
                    break;
                }
            default:
                ReadBuiltInType();
                break;
            }
        }

        private void ReadGenericInstanceSignature()
        {
            var arity = ReadCompressedUInt32();

            for(int i = 0; i < arity; i++)
                ReadTypeSignature();
        }

        private void ReadArrayTypeSignature()
        {
            // element_type
            ReadTypeSignature();

            // rank
            ReadCompressedUInt32();

            var sizes = ReadCompressedUInt32();
            for(int i = 0; i < sizes; i++)
                ReadCompressedUInt32();

            var low_bounds = ReadCompressedUInt32();
            for(int i = 0; i < low_bounds; i++)
                ReadCompressedInt32();
        }

        private void ReadBuiltInType()
        {
        }
    }
}