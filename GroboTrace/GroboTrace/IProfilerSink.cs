using System;

namespace GroboTrace
{
    public interface IProfilerSink
    {
        void WhenCurrentDurationIsLongerThanPercentile99(TimeSpan currentDuration, TimeStatistics timeStatistics, string trace);
    }
}