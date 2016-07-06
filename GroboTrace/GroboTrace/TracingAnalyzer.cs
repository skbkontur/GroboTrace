using System;
using System.Collections.Generic;
using System.Reflection;

namespace GroboTrace
{
    public static class TracingAnalyzer
    {
        public static void MethodStarted(MethodBase method, long methodHandle)
        {
            if(tree == null)
                tree = new MethodCallTree();
            tree.StartMethod(methodHandle, method);
        }

        public static void MethodFinished(long methodHandle, long elapsed)
        {
            tree.FinishMethod(methodHandle, elapsed);
        }

        public static Stats GetStats()
        {
            var ticks = Zzz.TicksReader();
            return new Stats
                {
                    Tree = tree == null ? new MethodStatsNode() : tree.GetStatsAsTree(ticks),
                    List = tree == null ? new List<MethodStats>() : tree.GetStatsAsList(ticks)
                };
        }

        public static void ClearStats()
        {
            if(tree != null)
                tree.ClearStats();
        }

        [ThreadStatic]
        private static MethodCallTree tree;
    }
}