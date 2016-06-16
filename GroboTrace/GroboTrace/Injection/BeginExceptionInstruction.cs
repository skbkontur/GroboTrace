namespace GroboTrace.Injection
{
    public class BeginExceptionInstruction : AbstractInstruction
    {
        public override int Size { get { return 0; } }

        public BeginExceptionInstruction(int offset)
        {
            Offset = offset;
        }

        public override int Offset { get; set; }
    }
}