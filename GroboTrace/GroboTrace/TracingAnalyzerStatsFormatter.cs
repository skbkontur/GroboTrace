using System.Reflection;
using System.Text;
using GrEmit.Utils;

namespace GroboTrace
{
    [DontTrace]
    public static class TracingAnalyzerStatsFormatter
    {
        public static string Format(Stats stats, long elapsedMilliseconds)
        {
            var sb = new StringBuilder();
            Format(stats.Tree, elapsedMilliseconds, 0, sb);
            foreach (var item in stats.List)
                Format(item, elapsedMilliseconds, 0, sb);
            return sb.ToString();
        }

        private static void Format(MethodStats stats, long elapsedMilliseconds, int depth, StringBuilder result)
        {
            if (stats == null || stats.Percent < 1.0)
                return;
            var margin = new string(' ', depth * 4);
            result.Append($"{margin}{stats.Percent:F2}% {stats.Percent * elapsedMilliseconds / 100.0:F3} ms");
            result.Append(stats.Method != null ? $" {stats.Calls} calls {Format(stats.Method)}" : " ROOT");
            result.AppendLine();
        }

        private static void Format(MethodStatsNode node, long elapsedMilliseconds, int depth, StringBuilder result)
        {
            Format(node.MethodStats, elapsedMilliseconds, depth, result);
            if (node.Children == null)
                return;
            foreach (var child in node.Children)
                Format(child, elapsedMilliseconds, depth + 1, result);
        }

        private static string Format(MethodBase methodBase)
        {
            var methodInfo = methodBase as MethodInfo;
            return methodInfo != null ? Formatter.Format(methodInfo) : Formatter.Format((ConstructorInfo)methodBase);
        }
    }
}