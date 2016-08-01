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
using System.Reflection;

namespace GroboTrace.MethodBodyParsing
{
    internal sealed class MethodBodyBaker : ByteBuffer
    {
        public MethodBodyBaker(Module module, Func<byte[], MetadataToken> signatureTokenBuilder, MethodBody body, int maxStack)
            : base(0)
        {
            this.module = module;
            this.signatureTokenBuilder = signatureTokenBuilder;
            this.body = body;
            this.maxStack = maxStack;
        }

        public byte[] BakeMethodBody()
        {
            //body.Instructions.SimplifyMacros();
            //body.Instructions.OptimizeMacros();

            WriteMethodBody();

            var temp = new byte[length];
            Array.Copy(buffer, temp, length);
            return temp;
        }

        private void WriteMethodBody()
        {
            var ilCode = body.GetILAsByteArray();
            codeSize = ilCode.Length;

            var exceptions = body.GetExceptionsAsByteArray();

            //body.TryCalculateMaxStackSize(module);
            
            if (RequiresFatHeader())
                WriteFatHeader();
            else
                WriteByte((byte)(0x2 | (codeSize << 2))); // tiny

            WriteBytes(ilCode);

            //WriteInstructions();

            //WriteBytes(4);

            if (body.HasExceptionHandlers)
            {
                Align(4);
                WriteBytes(exceptions);
            }

//            Align(4);
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
            WriteInt16((short)maxStack);
            WriteInt32(codeSize);
            body.LocalVarToken = GetVariablesSignature();
            WriteMetadataToken(body.LocalVarToken);
        }

        private MetadataToken GetVariablesSignature()
        {
            if (body.LocalVariablesCount() == 0)
                return MetadataToken.Zero;
            var signature = body.GetLocalSignature();
            var metadataToken = signatureTokenBuilder(signature);
            //Debug.WriteLine(".NET: got metadata token for signature : {0}", metadataToken.ToInt32());
            return metadataToken;
        }

        
        private bool RequiresFatHeader()
        {
            return codeSize >= 64
                   || body.InitLocals
                   || body.LocalVariablesCount() > 0
                   || body.HasExceptionHandlers
                   || body.TemporaryMaxStack > 8;
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
        private readonly int maxStack;
        private int codeSize;
    }
}