using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace GroboTrace
{
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
            var ticks = Zzz.TicksReader();
            var localTree = GetTree();
            var thread = new Thread(GetStatsInternal, 100 * 1024 * 1024);
            var args = new GetStatsArgs
            {
                ticks = ticks,
                tree = localTree
            };
            thread.Start(args);
            thread.Join();
            return args.stats;
        }

        private class GetStatsArgs
        {
            public long ticks;
            public MethodCallTree tree;
            public Stats stats;
        }

        private static void GetStatsInternal(object p)
        {
            var args = (GetStatsArgs)p;
            var localTree = args.tree;
            var ticks = args.ticks;
            args.stats = new Stats
            {
                ElapsedTicks = localTree == null ? 0 : ticks - localTree.startTicks,
                Tree = localTree == null ? new MethodStatsNode() : localTree.GetStatsAsTree(ticks),
                List = localTree == null ? new List<MethodStats>() : localTree.GetStatsAsList(ticks)
            };
        }

        public static void ClearStats()
        {
            var localTree = GetTree();
            var thread = new Thread(ClearStatsInternal, 100 * 1024 * 1024);
            thread.Start(localTree);
            thread.Join();
        }

        private static void ClearStatsInternal(object p)
        {
            var localTree = (MethodCallTree)p;
            if (localTree != null)
                localTree.ClearStats();
        }
    }
}