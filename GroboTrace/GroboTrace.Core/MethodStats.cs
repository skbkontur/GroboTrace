using System.Reflection;

namespace GroboTrace.Core
{
    public class MethodStats
    {
        public MethodBase Method { get; set; }
        public double Percent { get; set; }
        public long Ticks { get; set; }
        public int Calls { get; set; }
    }
}