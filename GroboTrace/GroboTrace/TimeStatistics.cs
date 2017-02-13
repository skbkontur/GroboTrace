using System;

namespace GroboTrace
{
    public class TimeStatistics
    {
        public TimeStatistics(string groboTraceKey)
        {
            GroboTraceKey = groboTraceKey;
        }

        public bool RegisterDuration(double durationMilliseconds)
        {
            lock(locker)
            {
                var d = Math.Max(durationMilliseconds * 10, 1);
                var bin = Math.Min((int)Math.Round(Math.Log10(d) * 30), counts.Length - 1);
                counts[bin]++;
                TotalCount++;
                MaxTime = Math.Max(MaxTime, durationMilliseconds);
                Percentile95Time = GetPercentile95Time();
                return durationMilliseconds > Percentile95Time || Math.Abs(durationMilliseconds - MaxTime) < 1e-3;
            }
        }

        public override string ToString()
        {
            return $"GroboTraceKey: {GroboTraceKey}, TotalCount: {TotalCount}, Percentile95Time: {Percentile95Time:F3} ms, MaxTime: {MaxTime:F3} ms";
        }

        private double GetPercentile95Time()
        {
            var index = (int)Math.Round(TotalCount * 0.95);
            var count = 0;
            for(var i = 0; i < counts.Length; i++)
            {
                count += counts[i];
                if(count >= index)
                    return (int)Math.Round(Math.Pow(10, (double)i / 30) / 10);
            }
            return MaxTime;
        }

        public string GroboTraceKey { get; }
        public int TotalCount { get; private set; }
        public double MaxTime { get; private set; }
        public double Percentile95Time { get; private set; }

        private readonly int[] counts = new int[250];
        private readonly object locker = new object();
    }
}