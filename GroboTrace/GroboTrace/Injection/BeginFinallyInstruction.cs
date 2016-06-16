namespace GroboTrace.Injection
{
    public class BeginFinallyInstruction : AbstractInstruction
    {
        public BeginFinallyInstruction(int offset)
        {
            Offset = offset;
        }

        public override int Size { get { return 0; } }

        public override int Offset { get; set; }
    }
}