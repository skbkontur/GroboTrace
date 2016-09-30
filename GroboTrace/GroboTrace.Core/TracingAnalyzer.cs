using System;
using System.Collections.Generic;
using System.Threading;

namespace GroboTrace.Core
{
    public class DontTraceAttribute : Attribute
    {
    }

    public static class TracingAnalyzer
    {
        private static MethodCallTree[] zzz = CreateZzz();
        private static MethodCallTree qxx = new MethodCallTree();

        private static MethodCallTree[] CreateZzz()
        {
            var result = new MethodCallTree[100000];
            for(int i = 0; i < result.Length; ++i)
                result[i] = new MethodCallTree();
            return result;
        }

        public static void MethodStarted(int methodId)
        {
//            qxx.StartMethod(methodId);
            var id = Thread.CurrentThread.ManagedThreadId;
            if(id < zzz.Length)
            {
                (zzz[id]).StartMethod(methodId);
            }
            else throw new NotSupportedException();
        }

        public static void MethodFinished(int methodId, long elapsed)
        {
//            qxx.FinishMethod(methodId, elapsed);
            var id = Thread.CurrentThread.ManagedThreadId;
            if (id < zzz.Length)
            {
                (zzz[id]).FinishMethod(methodId, elapsed);
            }
            else throw new NotSupportedException();
            //            tree.Value.FinishMethod(methodHandle, elapsed);
        }

        private static MethodCallTree GetTree()
        {
            var id = Thread.CurrentThread.ManagedThreadId;
            if (id < zzz.Length)
            {
                return zzz[id];
            }
            throw new NotSupportedException();
        }

        public static Stats GetStats()
        {
            var ticks = MethodBaseTracingInstaller.TicksReader();
            var localTree = GetTree();
            return new Stats
            {
                ElapsedTicks = localTree == null ? 0 : ticks - localTree.startTicks,
                Tree = localTree == null ? new MethodStatsNode() : localTree.GetStatsAsTree(ticks),
                List = localTree == null ? new List<MethodStats>() : localTree.GetStatsAsList(ticks)
            };
        }

        public static void ClearStats()
        {
            var localTree = GetTree();
            if (localTree != null)
                localTree.ClearStats();
        }
    }
}