using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

using GroboTrace;

namespace Benchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<Program>(
                ManualConfig.Create(DefaultConfig.Instance)
//                            .With(Job.LegacyJitX86)
//                            .With(Job.LegacyJitX64)
                            .With(Job.RyuJitX64)
                );
        }

        [Benchmark]
        public long Ethalon()
        {
            var start = MethodBaseTracingInstaller.TicksReader();
            var end = MethodBaseTracingInstaller.TicksReader();
            return end - start;
        }

        [Benchmark]
        public void TracingAnalyzer()
        {
            action();
        }

        [Setup]
        public void Setup()
        {
            MethodBaseTracingInstaller.TicksReader();
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(void), Type.EmptyTypes, typeof(string), true);
            method.GetILGenerator().Emit(OpCodes.Ret);
            action = (Action)method.CreateDelegate(typeof(Action));
        }

        private Action action;
    }
}
