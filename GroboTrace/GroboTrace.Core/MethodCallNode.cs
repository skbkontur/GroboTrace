using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

using GrEmit;

namespace GroboTrace.Core
{
    internal abstract class MethodCallNodeEdges
    {
        public abstract int Count { get; }
        public abstract MethodCallNode Jump(int methodId);
        public abstract IEnumerable<int> MethodIds { get; }
        public abstract IEnumerable<MethodCallNode> Children { get; }
    }

    internal static class UnrolledBinarySearchBuilder
    {
        public static Func<int[], MethodCallNode[], MethodCallNodeEdges> Build(int n)
        {
            var typeBuilder = module.DefineType("MCNE_UnrolledBinarySearch_" + n, TypeAttributes.Public | TypeAttributes.Class, typeof(MethodCallNodeEdges));
            var context = new Context
                {
                    keys = new FieldInfo[n],
                    values = new FieldInfo[n],
                };
            for(int i = 0; i < n; ++i)
            {
                context.keys[i] = typeBuilder.DefineField("key_" + i, typeof(int), FieldAttributes.Private);
                context.values[i] = typeBuilder.DefineField("value_" + i, typeof(MethodCallNode), FieldAttributes.Private);
            }

            BuildConstructor(typeBuilder, n, context);
            BuildCount(typeBuilder, n);
            BuildMethodIds(typeBuilder, n, context);
            BuildChildren(typeBuilder, n, context);
            BuildJump(typeBuilder, n, context);

            var type = typeBuilder.CreateType();
            var constructor = type.GetConstructor(new[] {typeof(int[]), typeof(MethodCallNode[])});
            var dynamicMethod = new DynamicMethod(Guid.NewGuid().ToString(), typeof(MethodCallNodeEdges), new[] {typeof(int[]), typeof(MethodCallNode[])}, typeof(string), true);
            using(var il = new GroboIL(dynamicMethod))
            {
                il.Ldarg(0);
                il.Ldarg(1);
                il.Newobj(constructor);
                il.Ret();
            }
            var creator = (Func<int[], MethodCallNode[], MethodCallNodeEdges>)dynamicMethod.CreateDelegate(typeof(Func<int[], MethodCallNode[], MethodCallNodeEdges>));
            return (keys, values) =>
                {
                    var indexes = new int[n];
                    for(int i = 0; i < n; ++i)
                        indexes[i] = i;
                    Array.Sort(indexes, (i, j) => keys[i].CompareTo(keys[j]));
                    return creator(indexes.Select(i => keys[i]).ToArray(), indexes.Select(i => values[i]).ToArray());
                };
        }

        private static void BuildJump(TypeBuilder typeBuilder, int n, Context context)
        {
            var method = typeBuilder.DefineMethod("Jump", MethodAttributes.Public | MethodAttributes.Virtual, CallingConventions.HasThis, typeof(MethodCallNode), new[] {typeof(int)});
            using(var il = new GroboIL(method))
            {
                var retNullLabel = il.DefineLabel("retNull");
                var emittingContext = new MethodJumpEmittingContext
                    {
                        context = context,
                        il = il,
                        retNullLabel = retNullLabel
                    };
                DoBinarySearch(emittingContext, 0, n - 1);
                il.MarkLabel(retNullLabel);
                il.Ldnull();
                il.Ret();
            }
            typeBuilder.DefineMethodOverride(method, typeof(MethodCallNodeEdges).GetMethod(method.Name, BindingFlags.Public | BindingFlags.Instance));
        }

        private static void DoBinarySearch(MethodJumpEmittingContext context, int l, int r)
        {
            var il = context.il;
            if(l > r)
            {
                il.Br(context.retNullLabel);
                return;
            }
            if(r - l + 1 <= 3)
            {
                // just bunch of ifs
                for(; l <= r; l++)
                {
                    il.Ldarg(1); // stack: [key]
                    il.Ldarg(0); // stack: [key, this]
                    il.Ldfld(context.context.keys[l]); // stack: [key, this.keys[l]]
                    var nextLabel = il.DefineLabel("next");
                    il.Bne_Un(nextLabel); // if(key != this.keys[l]) goto next; stack: []
                    il.Ldarg(0); // stack: [this]
                    il.Ldfld(context.context.values[l]); // stack: [this.values[l]]
                    il.Ret();
                    il.MarkLabel(nextLabel);
                }
                il.Br(context.retNullLabel);
            }
            else
            {
                int m = (l + r) / 2;

                il.Ldarg(1); // stack: [key]
                il.Ldarg(0); // stack: [key, this]
                il.Ldfld(context.context.keys[m]); // stack: [key, this.keys[m]]
                var nextLabel = il.DefineLabel("next");
                il.Bne_Un(nextLabel); // if(key != this.keys[m]) goto next; stack: []
                il.Ldarg(0); // stack: [this]
                il.Ldfld(context.context.values[m]); // stack: [this.values[m]]
                il.Ret();

                il.MarkLabel(nextLabel);
                il.Ldarg(1); // stack: [key]
                il.Ldarg(0); // stack: [key, this]
                il.Ldfld(context.context.keys[m]); // stack: [key, this.keys[m]]
                var goLeftLabel = il.DefineLabel("goLeft");
                il.Blt(goLeftLabel, false); // if(key < this.keys[m]]) goto goLeft; stack: []
                DoBinarySearch(context, m + 1, r);

                il.MarkLabel(goLeftLabel);
                DoBinarySearch(context, l, m - 1);
            }
        }

        private static void BuildCount(TypeBuilder typeBuilder, int n)
        {
            var property = typeBuilder.DefineProperty("Count", PropertyAttributes.None, typeof(int), Type.EmptyTypes);
            var getter = typeBuilder.DefineMethod("get_Count", MethodAttributes.Public | MethodAttributes.Virtual, CallingConventions.HasThis, typeof(int), Type.EmptyTypes);
            using(var il = new GroboIL(getter))
            {
                il.Ldc_I4(n);
                il.Ret();
            }
            property.SetGetMethod(getter);
            typeBuilder.DefineMethodOverride(getter, typeof(MethodCallNodeEdges).GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance).GetGetMethod());
        }

        private static void BuildMethodIds(TypeBuilder typeBuilder, int n, Context context)
        {
            var property = typeBuilder.DefineProperty("MethodIds", PropertyAttributes.None, typeof(IEnumerable<int>), Type.EmptyTypes);
            var getter = typeBuilder.DefineMethod("get_MethodIds", MethodAttributes.Public | MethodAttributes.Virtual, CallingConventions.HasThis, typeof(IEnumerable<int>), Type.EmptyTypes);
            using(var il = new GroboIL(getter))
            {
                il.Ldc_I4(n); // stack: [n]
                il.Newarr(typeof(int)); // stack: [new int[n] -> keys]
                for(int i = 0; i < n; ++i)
                {
                    il.Dup(); // stack: [keys, keys]
                    il.Ldc_I4(i); // stack: [keys, keys, i]
                    il.Ldarg(0); // stack: [keys, keys, i, this]
                    il.Ldfld(context.keys[i]); // stack: [keys, keys, i, this.keys_{i}]
                    il.Stelem(typeof(int)); // keys[i] = this.keys_{i}; stack: [keys]
                }
                il.Castclass(typeof(IEnumerable<int>));
                il.Ret();
            }
            property.SetGetMethod(getter);
            typeBuilder.DefineMethodOverride(getter, typeof(MethodCallNodeEdges).GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance).GetGetMethod());
        }

        private static void BuildChildren(TypeBuilder typeBuilder, int n, Context context)
        {
            var property = typeBuilder.DefineProperty("Children", PropertyAttributes.None, typeof(IEnumerable<MethodCallNode>), Type.EmptyTypes);
            var getter = typeBuilder.DefineMethod("get_Children", MethodAttributes.Public | MethodAttributes.Virtual, CallingConventions.HasThis, typeof(IEnumerable<MethodCallNode>), Type.EmptyTypes);
            using(var il = new GroboIL(getter))
            {
                il.Ldc_I4(n); // stack: [n]
                il.Newarr(typeof(MethodCallNode)); // stack: [new int[n] -> values]
                for(int i = 0; i < n; ++i)
                {
                    il.Dup(); // stack: [values, values]
                    il.Ldc_I4(i); // stack: [values, values, i]
                    il.Ldarg(0); // stack: [values, values, i, this]
                    il.Ldfld(context.values[i]); // stack: [values, values, i, this.values_{i}]
                    il.Stelem(typeof(MethodCallNode)); // values[i] = this.values_{i}; stack: [values]
                }
                il.Castclass(typeof(IEnumerable<MethodCallNode>));
                il.Ret();
            }
            property.SetGetMethod(getter);
            typeBuilder.DefineMethodOverride(getter, typeof(MethodCallNodeEdges).GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance).GetGetMethod());
        }

        private static void BuildConstructor(TypeBuilder typeBuilder, int n, Context context)
        {
            var constructor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new[] {typeof(int[]), typeof(MethodCallNode[])});
            using(var il = new GroboIL(constructor))
            {
                for(int i = 0; i < n; ++i)
                {
                    il.Ldarg(0); // stack: [this]
                    il.Ldarg(1); // stack: [this, keys]
                    il.Ldc_I4(i); // stack: [this, keys, i]
                    il.Ldelem(typeof(int)); // stack: [this, keys[i]]
                    il.Stfld(context.keys[i]); // this.key_{i} = keys[i]; stack: []
                    il.Ldarg(0); // stack: [this]
                    il.Ldarg(2); // stack: [this, values]
                    il.Ldc_I4(i); // stack: [this, values, i]
                    il.Ldelem(typeof(MethodCallNode)); // stack: [this, values[i]]
                    il.Stfld(context.values[i]); // this.value_{i} = values[i]; stack: []
                }
                il.Ret();
            }
        }

        private static readonly AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("4fd22332-6b3e-4a88-b3ba-4830ab4e71eb"), AssemblyBuilderAccess.Run);
        private static readonly ModuleBuilder module = assembly.DefineDynamicModule(Guid.NewGuid().ToString());

        private class MethodJumpEmittingContext
        {
            public Context context;
            public GroboIL il;
            public GroboIL.Label retNullLabel;
        }

        private class Context
        {
            public FieldInfo[] keys;
            public FieldInfo[] values;
        }
    }

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

    internal class MCNE_PerfectHashtable : MethodCallNodeEdges
    {
        public MCNE_PerfectHashtable(int[] keys, MethodCallNode[] values)
        {
            Count = keys.Length;
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

        public override int Count { get; }

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
            //return this;
            var child = edges.Jump(methodId);
            if(child != null) return child;
            child = new MethodCallNode(this, methodId);
            edges = MethodCallNodeEdgesFactory.Create(edges.MethodIds.Concat(new[] {methodId}), edges.Children.Concat(new[] {child}));
            return child;
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