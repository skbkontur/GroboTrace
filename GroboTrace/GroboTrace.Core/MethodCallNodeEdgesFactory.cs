using System;
using System.Collections.Generic;
using System.Linq;

namespace GroboTrace.Core
{
    internal static class MethodCallNodeEdgesFactory
    {
        static MethodCallNodeEdgesFactory()
        {
            creators = new Func<int[], MethodCallNode[], MethodCallNodeEdges>[threshold + 1];
            for(int i = 1; i <= threshold; ++i)
                creators[i] = UnrolledBinarySearchBuilder.Build(i);
        }

        private const int threshold = 50;

        public static MethodCallNodeEdges Create(IEnumerable<int> keys, IEnumerable<MethodCallNode> edges)
        {
            var keysArr = keys.ToArray();
            var edgesArr = edges.ToArray();
            int n = keysArr.Length;
            if(n <= threshold)
                return creators[n](keysArr, edgesArr);
            return new MCNE_PerfectHashtable(keysArr, edgesArr);
        }

        public static void Init()
        {
        }

        private static readonly Func<int[], MethodCallNode[], MethodCallNodeEdges>[] creators;
    }
}