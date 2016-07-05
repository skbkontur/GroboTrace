using System.Reflection;

namespace GroboTrace
{
    public class MethodStats
    {
        public MethodBase Method { get; set; }
        public double Percent { get; set; }
        public long Ticks { get; set; }
        public int Calls { get; set; }
    }
}