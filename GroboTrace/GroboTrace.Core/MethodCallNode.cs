using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace GroboTrace.Core
{
    internal class MethodCallNode
    {
        public MethodCallNode(MethodCallNode parent, int methodId)
        {
            this.parent = parent;
            MethodId = methodId;
            handles = new int[1];
            children = new MethodCallNode[1];
        }

        public MethodCallNode StartMethod(int methodId)
        {
            //return this;
            var index = methodId % handles.Length;
            if(handles[index] == methodId)
                return children[index];
            if(handles[index] != 0)
            {
                // rebuild table
                index = Rebuild(methodId);
            }
            if(handles[index] == 0)
            {
                handles[index] = methodId;
                children[index] = Get(methodId); //new MethodCallNode(this, methodId);
            }
            return children[index];
        }

        public MethodCallNode FinishMethod(int methodId, long elapsed)
        {
            //return this;
//            if(methodId != this.MethodId)
//                throw new InvalidOperationException();
            ++Calls;
            Ticks += elapsed;
            return parent;
        }

        public MethodStatsNode GetStats(long totalTicks)
        {
            return new MethodStatsNode
                {
                    MethodStats = new MethodStats
                        {
                            Method = MethodBaseTracingInstaller.GetMethod(MethodId),
                            Calls = Calls,
                            Ticks = Ticks,
                            Percent = totalTicks == 0 ? 0.0 : Ticks * 100.0 / totalTicks
                        },
                    Children = Children.Select(child =>
                        {
                            var childStats = child.GetStats(totalTicks);
                            return childStats;
                        }).OrderByDescending(stats => stats.MethodStats.Ticks).
                                        ToArray()
                };
        }

        public void GetStats(Dictionary<MethodBase, MethodStats> statsDict)
        {
            var selfTicks = Ticks;
            foreach(var child in Children)
            {
                child.GetStats(statsDict);
                selfTicks -= child.Ticks;
            }
            var method = MethodBaseTracingInstaller.GetMethod(MethodId);
            method = method.IsGenericMethod ? ((MethodInfo)method).GetGenericMethodDefinition() : method;
            MethodStats stats;
            if(!statsDict.TryGetValue(method, out stats))
                statsDict.Add(method, new MethodStats {Calls = Calls, Method = method, Ticks = selfTicks});
            else
            {
                stats.Calls += Calls;
                stats.Ticks += selfTicks;
            }
        }

        private static readonly Queue<MethodCallNode>[] queues;

        static MethodCallNode()
        {
            queues = new Queue<MethodCallNode>[100000];
            //for(int i = 0; i < queues.Length; ++i)
            //    queues[i] = new Queue<MethodCallNode>();
        }

        private MethodCallNode Get(int methodId)
        {
            var id = Thread.CurrentThread.ManagedThreadId;
            if(id >= 100000) throw new InvalidOperationException();
            var queue = queues[id] ?? (queues[id] = new Queue<MethodCallNode>());
            if(queue.Count > 0)
            {
                var result = queue.Dequeue();
                result.parent = this;
                result.MethodId = methodId;
            }
            return new MethodCallNode(this, methodId);
        }

        private static void Release(MethodCallNode node)
        {
            var id = Thread.CurrentThread.ManagedThreadId;
            if (id >= 100000) throw new InvalidOperationException();
            var queue = queues[id] ?? (queues[id] = new Queue<MethodCallNode>());
            node.Calls = 0;
            node.Ticks = 0;
            node.parent = null;
            node.MethodId = 0;
            queue.Enqueue(node);
        }

        public void ClearStats()
        {
            var queue = new Queue<MethodCallNode>();
            queue.Enqueue(this);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if(node != this)
                    Release(node);
                foreach (var child in node.children)
                {
                    if (child != null)
                        queue.Enqueue(child);
                }
            }

            handles = new int[1];
            children = new MethodCallNode[1];
            Calls = 0;
            Ticks = 0;
        }

        public int MethodId { get; set; }
        public int Calls { get; set; }
        public long Ticks { get; set; }

        public IEnumerable<MethodCallNode> Children { get { return children.Where(node => node != null && node.Calls > 0); } }

        private int Rebuild(int newHandle)
        {
            var values = new List<int>();
            for(int i = 0; i < handles.Length; ++i)
            {
                if(handles[i] != 0)
                    values.Add(handles[i]);
            }
            values.Add(newHandle);
            var length = handles.Length;
            while(true)
            {
                ++length;
                var was = new bool[length];
                bool ok = true;
                for(int i = 0; i < values.Count; ++i)
                {
                    var index = values[i] % length;
                    if(was[index])
                    {
                        ok = false;
                        break;
                    }
                    was[index] = true;
                }
                if(ok) break;
            }
            var newHandles = new int[length];
            var newChildren = new MethodCallNode[length];
            for(int i = 0; i < handles.Length; ++i)
            {
                if(handles[i] != 0)
                {
                    var index = handles[i] % length;
                    newHandles[index] = handles[i];
                    newChildren[index] = children[i];
                }
            }
            handles = newHandles;
            children = newChildren;
            return newHandle % length;
        }

        private MethodCallNode parent;
        private MethodCallNode[] children;
        private int[] handles;
    }
}