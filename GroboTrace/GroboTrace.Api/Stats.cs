using System.Collections.Generic;

namespace GroboTrace.Api
{
    public class Stats
    {
        public long ElapsedTicks { get; set; }
        public MethodStatsNode Tree { get; set; }
        public List<MethodStats> List { get; set; }
    }
}