namespace GroboTrace.Injection
{
    public abstract class AbstractInstruction
    {
        public abstract int Size { get; }
        public abstract int Offset { get; set; }
    }
}