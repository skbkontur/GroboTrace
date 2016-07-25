using System;

namespace GroboTrace.Mono.Cecil.Cil
{
    public class LocalInfo
    {
        public LocalInfo(int localIndex, byte[] signature)
        {
            LocalIndex = localIndex;
            Signature = signature;
        }

        public LocalInfo(int localIndex, Type localType, bool isPinned)
        {
            LocalIndex = localIndex;
            LocalType = localType;
            IsPinned = isPinned;
        }


        public readonly int LocalIndex;
        public readonly byte[] Signature;
        public readonly Type LocalType;
        public readonly bool IsPinned;
    }
}