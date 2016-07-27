using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using GroboTrace.Mono.Cecil.PE;

namespace GroboTrace.Mono.Cecil.Cil
{
    public class LocalVarSigBuilder
    {
        public LocalVarSigBuilder()
        {
            localVariables = new LocalInfoCollection();
        }
        
        public LocalVarSigBuilder(byte[] oldLocalSignature)
        {      
            localVariables = new SignatureReader(oldLocalSignature).ReadLocalVarSig();
        }

        public LocalInfo AddLocalVariable(byte[] signature)
        {
            var localInfo = new LocalInfo(signature);
            localVariables.Add(localInfo);
            return localInfo;
        }

        public LocalInfo AddLocalVariable(Type localType, bool isPinned = false)
        {
            var localInfo = new LocalInfo(localType, isPinned);
            localVariables.Add(localInfo);
            return localInfo;
        }

        public byte[] GetSignature()
        {
            var writer = new ByteBuffer();
            writer.WriteByte(0x7);
            writer.WriteCompressedUInt32((uint)Count);

            foreach (var localInfo in localVariables)
            {
                writer.WriteBytes(localInfo.Signature ?? BakeLocal(localInfo));
            }

            writer.position = 0;
            return writer.ReadBytes(writer.length);
        }

        private byte[] BakeLocal(LocalInfo localInfo)
        {
            if (localInfo.LocalType == null)
                throw new ArgumentException();

            var sigHelper = SignatureHelper.GetLocalVarSigHelper();
            sigHelper.AddArgument(localInfo.LocalType, localInfo.IsPinned);

            var withHeader = sigHelper.GetSignature();

            byte[] result = new byte[withHeader.Length - 1 - 1]; // first byte is 0x7 (LOCAL_SIG) and second is 0x1 (Count)

            Array.Copy(withHeader, 2, result, 0, result.Length);

            return result;
        }

        private readonly LocalInfoCollection localVariables;

        public int Count => localVariables.Count;
    }
}