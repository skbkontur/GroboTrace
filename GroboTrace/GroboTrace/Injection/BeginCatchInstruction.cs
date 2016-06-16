using System;

namespace GroboTrace.Injection
{
    public class BeginCatchInstruction : AbstractInstruction
    {
        public override int Size { get { return 0; } }

        public override int Offset { get; set; }

        public readonly Type ExceptionType;

        public BeginCatchInstruction(int offset, Type exceptionType)
        {
            ExceptionType = exceptionType;
            Offset = offset;
        }
    }
}