using System.Collections.Generic;
using System.Linq;

namespace GroboTrace.Core
{
    internal class MCNE_Empty : MethodCallNodeEdges
    {
        public override int Count { get { return 0; } }

        public override MethodCallNode Jump(int methodId)
        {
            return null;
        }

        public override IEnumerable<int> MethodIds { get { return Enumerable.Empty<int>(); } }
        public override IEnumerable<MethodCallNode> Children { get { return Enumerable.Empty<MethodCallNode>(); } }
    }
}