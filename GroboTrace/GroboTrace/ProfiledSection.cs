using System;
using System.Diagnostics;

namespace GroboTrace
{
    public class ProfiledSection : IDisposable
    {
        public ProfiledSection(IProfilerSink profilerSink, TimeStatistics timeStatistics)
        {
            this.profilerSink = profilerSink;
            this.timeStatistics = timeStatistics;
            TracingAnalyzer.ClearStatsForCurrentThread();
            stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            stopwatch.Stop();
            if(timeStatistics.RegisterDuration(stopwatch.ElapsedMilliseconds))
            {
                var stats = TracingAnalyzer.GetStatsForCurrentThread();
                var trace = TracingAnalyzerStatsFormatter.Format(stats, stopwatch.ElapsedMilliseconds);
                profilerSink.WhenCurrentDurationIsLongerThanPercentile95(stopwatch.Elapsed, timeStatistics, trace);
            }
        }

        private readonly IProfilerSink profilerSink;
        private readonly TimeStatistics timeStatistics;
        private readonly Stopwatch stopwatch;
    }
}