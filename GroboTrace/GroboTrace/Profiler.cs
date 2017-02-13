using System.Collections.Concurrent;

namespace GroboTrace
{
    public static class Profiler
    {
        public static ProfiledSection Profile(string groboTraceKey, IProfilerSink profilerSink)
        {
            var timeStatistics = GetTimeStatistics(groboTraceKey);
            return new ProfiledSection(profilerSink, timeStatistics);
        }

        public static TimeStatistics GetTimeStatistics(string groboTraceKey)
        {
            return timeStatisticsMap.GetOrAdd(groboTraceKey, x => new TimeStatistics(groboTraceKey));
        }

        private static readonly ConcurrentDictionary<string, TimeStatistics> timeStatisticsMap = new ConcurrentDictionary<string, TimeStatistics>();
    }
}