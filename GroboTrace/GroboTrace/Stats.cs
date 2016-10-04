using System.Collections.Generic;

namespace GroboTrace
{
    public class Stats
    {
        public int TotalNodes { get; set; }
        public int TotalChildren { get; set; }
        public int TotalMethods { get; set; }
        public long ElapsedTicks { get; set; }
        public MethodStatsNode Tree { get; set; }
        public List<MethodStats> List { get; set; }
    }
}