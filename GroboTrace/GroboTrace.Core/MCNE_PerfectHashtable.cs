using System.Collections.Generic;
using System.Linq;

namespace GroboTrace.Core
{
    internal class MCNE_PerfectHashtable : MethodCallNodeEdges
    {
        public MCNE_PerfectHashtable(int[] keys, MethodCallNode[] values)
        {
            count = keys.Length;
            var length = keys.Length - 1;
            while(true)
            {
                ++length;
                var was = new bool[length];
                bool ok = true;
                foreach(var key in keys)
                {
                    var index = key % length;
                    if(was[index])
                    {
                        ok = false;
                        break;
                    }
                    was[index] = true;
                }
                if(ok) break;
            }
            handles = new int[length];
            children = new MethodCallNode[length];
            for(int i = 0; i < keys.Length; ++i)
            {
                var index = keys[i] % length;
                handles[index] = keys[i];
                children[index] = values[i];
            }
        }

        private readonly int count;

        public override int Count { get { return count; } }

        public override MethodCallNode Jump(int methodId)
        {
            var index = methodId % handles.Length;
            return handles[index] == methodId ? children[index] : null;
        }

        public override IEnumerable<int> MethodIds { get { return handles.Where(key => key != 0); } }
        public override IEnumerable<MethodCallNode> Children { get { return children.Where(child => child != null); } }
        private readonly int[] handles;
        private readonly MethodCallNode[] children;
    }
}