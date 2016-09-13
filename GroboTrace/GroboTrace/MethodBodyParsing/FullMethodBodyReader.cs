using System;
using System.Reflection;

namespace GroboTrace.MethodBodyParsing
{
    internal sealed unsafe class FullMethodBodyReader : UnmanagedByteBuffer
    {
        public FullMethodBodyReader(byte* rawMethodBody, Module module)
            : base(rawMethodBody)
        {
            this.module = module;
        }

        public void Read(MethodBody body)
        {
            this.body = body;
            ReadMethodBodyInternal();
        }

        private void ReadMethodBodyInternal()
        {
            position = 0;

            var flags = ReadByte();
            switch(flags & 0x3)
            {
            case 0x2: // tiny
                codeSize = flags >> 2;
                body.TemporaryMaxStack = 8;
                ReadCode();
                break;
            case 0x3: // fat
                position--;
                ReadFatMethod();
                break;
            default:
                throw new InvalidOperationException();
            }
        }

        private void ReadFatMethod()
        {
            var flags = ReadUInt16();
            body.TemporaryMaxStack = ReadUInt16();
            codeSize = (int)ReadUInt32();
            body.LocalVarToken = new MetadataToken(ReadUInt32());
            body.InitLocals = (flags & 0x10) != 0;

            if(body.LocalVarToken.RID != 0)
                ReadVariables(body.LocalVarToken);

            ReadCode();

            if((flags & 0x8) != 0)
                ReadExceptions();
        }

        private void ReadExceptions()
        {
            Align(4);
            new ExceptionsInfoReader(buffer + position).Read(body);
        }

        private void ReadCode()
        {
            new ILCodeReader(buffer + position, codeSize).Read(body);
            position += codeSize;
        }

        private void ReadVariables(MetadataToken local_var_token)
        {
            var signature = module.ResolveSignature(local_var_token.ToInt32());
            body.SetLocalSignature(signature);
        }

        private void Align(int align)
        {
            align--;
            Advance(((position + align) & ~align) - position);
        }

        private readonly Module module;

        private MethodBody body;

        private int codeSize;
    }
}