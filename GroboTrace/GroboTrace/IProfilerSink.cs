using System;

namespace GroboTrace
{
    public interface IProfilerSink
    {
        void WhenCurrentDurationIsLongerThanPercentile95(TimeSpan currentDuration, TimeStatistics timeStatistics, string trace);
    }
}