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
            newLocals = new List<LocalInfo>();
            relicLocals = new byte[0];
            relicCount = 0;
        }

        public LocalVarSigBuilder(byte[] oldLocalSignature)
        {      
            var reader = new ByteBuffer(oldLocalSignature);

            const byte local_sig = 0x7;

            if (reader.ReadByte() != local_sig)
                throw new NotSupportedException();

            relicCount = (int)reader.ReadCompressedUInt32();

            relicLocals = new byte[oldLocalSignature.Length - reader.position];
            Array.Copy(oldLocalSignature, reader.position, relicLocals, 0, relicLocals.Length);

            newLocals = new List<LocalInfo>();
        }
        

        public LocalInfo AddLocalVariable(byte[] signature)
        {
            var localInfo = new LocalInfo(Count, signature);
            newLocals.Add(localInfo);
            return localInfo;
        }

        public LocalInfo AddLocalVariable(Type localType, bool isPinned = false)
        {
            var localInfo = new LocalInfo(Count, localType, isPinned);
            newLocals.Add(localInfo);
            return localInfo;
        }

        public byte[] GetSignature()
        {
            var writer = new ByteBuffer { position = 0 };
            writer.WriteByte(0x7);
            writer.WriteCompressedUInt32((uint)Count);
            writer.WriteBytes(relicLocals);
            
            foreach (var localInfo in newLocals)
            {
                writer.WriteBytes(localInfo.Signature ?? BakeLocal(localInfo));
            }

            writer.position = 0;
            var result = writer.ReadBytes(writer.length);
            return result;
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

        private List<LocalInfo> newLocals;
        private byte[] relicLocals;
        private int relicCount;

        public int Count
        {
            get { return relicCount + newLocals.Count; }
        }
    }
}