namespace GroboTrace.Injection
{
    public class EndExceptionInstruction : AbstractInstruction
    {
        public override int Size { get { return 0; } }

        public override int Offset { get; set; }

        public EndExceptionInstruction(int offset)
        {
            Offset = offset;
        }
    }
}