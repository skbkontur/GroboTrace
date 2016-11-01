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
            edges = new MCNE_Empty();
        }

        public MethodCallNode StartMethod(int methodId)
        {
            var child = edges.Jump(methodId);
            if(child != null) return child;
            child = new MethodCallNode(this, methodId);
            edges = MethodCallNodeEdgesFactory.Create(edges.MethodIds.Concat(new[] {methodId}), edges.Children.Concat(new[] {child}));
            return child;
        }

        public MethodCallNode FinishMethod(int methodId, long elapsed)
        {
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

        public void ClearStats()
        {
            var queue = new Queue<MethodCallNode>();
            queue.Enqueue(this);
            while(queue.Count > 0)
            {
                var node = queue.Dequeue();
                node.Calls = 0;
                node.Ticks = 0;
                foreach(var child in node.edges.Children)
                {
                    if(child != null)
                        queue.Enqueue(child);
                }
            }
        }

        public int MethodId { get; set; }
        public int Calls { get; set; }
        public long Ticks { get; set; }

        public IEnumerable<MethodCallNode> Children { get { return edges.Children.Where(node => node.Calls > 0); } }

        private readonly MethodCallNode parent;
        private MethodCallNodeEdges edges;
    }
}