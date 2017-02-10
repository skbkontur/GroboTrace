using System;
using System.Threading;

namespace GroboTrace.Core
{
    public static class TracingAnalyzer
    {
        private static MethodCallTree[] CreateMethodCallTreesMap()
        {
            var result = new MethodCallTree[100000];
            for(var i = 0; i < result.Length; ++i)
                result[i] = new MethodCallTree();
            return result;
        }

        public static void MethodStarted(int methodId)
        {
            GetMethodCallTreeForCurrentThread().StartMethod(methodId);
        }

        public static void MethodFinished(int methodId, long elapsed)
        {
            GetMethodCallTreeForCurrentThread().FinishMethod(methodId, elapsed);
        }

        public static void ClearStats()
        {
            GetMethodCallTreeForCurrentThread().ClearStats();
        }

        public static Stats GetStats()
        {
            var ticks = MethodBaseTracingInstaller.TicksReader();
            var methodCallTree = GetMethodCallTreeForCurrentThread();
            return new Stats
                {
                    ElapsedTicks = ticks - methodCallTree.startTicks,
                    Tree = methodCallTree.GetStatsAsTree(ticks),
                    List = methodCallTree.GetStatsAsList(ticks),
                };
        }

        private static MethodCallTree GetMethodCallTreeForCurrentThread()
        {
            var id = Thread.CurrentThread.ManagedThreadId;
            if(id >= callTreesMap.Length)
                throw new NotSupportedException($"Curent ThreadId is too large: {id}");
            return callTreesMap[id];
        }

        private static readonly MethodCallTree[] callTreesMap = CreateMethodCallTreesMap();
    }
}