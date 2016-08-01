using System;

namespace GroboTrace.MethodBodyParsing
{
    public class LocalInfo
    {
        public LocalInfo(byte[] signature)
        {
            Signature = signature;
        }

        public LocalInfo(Type localType, bool isPinned)
        {
            LocalType = localType;
            IsPinned = isPinned;
        }


        internal int LocalIndex = -1;
        public byte[] Signature;
        public Type LocalType;
        public bool IsPinned;
    }
}