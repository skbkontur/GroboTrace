using System.Collections.Generic;

namespace GroboTrace.Core
{
    public class Stats
    {
        public long ElapsedTicks { get; set; }
        public MethodStatsNode Tree { get; set; }
        public List<MethodStats> List { get; set; }
    }
}