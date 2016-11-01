using System.Collections.Generic;

namespace GroboTrace.Core
{
    internal abstract class MethodCallNodeEdges
    {
        public abstract int Count { get; }
        public abstract MethodCallNode Jump(int methodId);
        public abstract IEnumerable<int> MethodIds { get; }
        public abstract IEnumerable<MethodCallNode> Children { get; }
    }
}