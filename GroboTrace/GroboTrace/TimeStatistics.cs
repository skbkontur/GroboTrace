using System;

namespace GroboTrace
{
    public class TimeStatistics
    {
        public TimeStatistics(string timeStatisticsKey)
        {
            TimeStatisticsKey = timeStatisticsKey;
        }

        public bool AddTime(double milliseconds)
        {
            lock (locker)
            {
                var d = Math.Max(milliseconds * 10, 1);
                var bin = Math.Min((int)Math.Round(Math.Log10(d) * 30), counts.Length - 1);
                counts[bin]++;
                TotalCount++;
                MaxTime = Math.Max(MaxTime, milliseconds);
                Quantile95Time = GetQuantile95();
                return milliseconds > Quantile95Time || Math.Abs(milliseconds - MaxTime) < 1e-3;
            }
        }

        public override string ToString()
        {
            return $"{TimeStatisticsKey}: Requests={TotalCount} Quantile95={Quantile95Time:F3} ms Maximum={MaxTime:F3} ms";
        }

        private double GetQuantile95()
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

        public int TotalCount { get; private set; }
        public double MaxTime { get; private set; }
        public double Quantile95Time { get; private set; }
        public string TimeStatisticsKey { get; }
        private readonly int[] counts = new int[250];
        private readonly object locker = new object();
    }
}