using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Attributes;

using GrEmit;

namespace ConsoleApplication3
{
    public class Program
    {
        static void Main(string[] args)
        {
//            var test = new Test2();
//            test.rdtsc();
//            return;
            BenchmarkRunner.Run<Test2>(
                ManualConfig.Create(DefaultConfig.Instance)
                            .With(Job.LegacyJitX86)
                            .With(Job.LegacyJitX64)
                            .With(Job.RyuJitX64)
                );
//            BenchmarkRunner.Run<Test1>(
//                ManualConfig.Create(DefaultConfig.Instance)
//                            .With(Job.LegacyJitX86)
//                            .With(Job.LegacyJitX64)
//                            .With(Job.RyuJitX64)
//                            .With(Job.Mono)
//                );
        }
        
    }

    public class Test2
    {
        [Benchmark]
        public long Stopwatch()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int res = Zzz();
            var elapsed = stopwatch.Elapsed;
            return elapsed.Ticks + res;
        }

        [Benchmark]
        public long rdtsc()
        {
            var start = TicksReader();
            int res = Zzz();
            var end = TicksReader();
            return end - start + res;
        }

        [Benchmark(Baseline = true)]
        public long Empty()
        {
            int res = Zzz();
            return 0L + res;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int Zzz()
        {
            int sum = 0;
            for(int i = 0; i < x; ++i)
                sum += i;
            return sum;
        }

        [Setup]
        public void Setup()
        {
            x = int.Parse(str);
        }

        [Params("100")]
        public string str;

        private int x;

        public static readonly Func<long> TicksReader;
        public static readonly IntPtr ticksReaderAddress;


        static unsafe Test2()
        {
            var dynamicMethodPointerExtractor = EmitDynamicMethodPointerExtractor();
            var dynamicMethod = new DynamicMethod("GetTicks_" + Guid.NewGuid(), typeof(long), Type.EmptyTypes, typeof(string));
            using (var il = new GroboIL(dynamicMethod))
            {
                il.Ldc_I8(123456789123456789L);
                il.Ret();
            }
            TicksReader = (Func<long>)dynamicMethod.CreateDelegate(typeof(Func<long>));
            ticksReaderAddress = dynamicMethodPointerExtractor(dynamicMethod);
            var pointer = (byte*)ticksReaderAddress;
            byte[] code;
            if (IntPtr.Size == 8)
            {
                // x64
                code = new byte[]
                    {
                        0x0f, 0x31, // rdtsc
                        0x48, 0xc1, 0xe2, 0x20, // shl rdx, 32
                        0x48, 0x09, 0xd0, // or rax, rdx
                        0xc3, // ret
                    };
            }
            else
            {
                // x86
                code = new byte[]
                    {
                        0x0F, 0x31, // rdtsc
                        0xC3 // ret
                    };
            }
            fixed (byte* p = &code[0])
            {
                var pp = p;
                for (var i = 0; i < code.Length; ++i)
                    *pointer++ = *pp++;
            }
        }

        private static Func<DynamicMethod, IntPtr> EmitDynamicMethodPointerExtractor()
        {
            var method = new DynamicMethod("DynamicMethodPointerExtractor", typeof(IntPtr), new[] { typeof(DynamicMethod) }, typeof(string), true);
            using (var il = new GroboIL(method))
            {
                il.Ldarg(0); // stack: [dynamicMethod]
                var getMethodDescriptorMethod = typeof(DynamicMethod).GetMethod("GetMethodDescriptor", BindingFlags.Instance | BindingFlags.NonPublic);
                if (getMethodDescriptorMethod == null)
                    throw new MissingMethodException(typeof(DynamicMethod).Name, "GetMethodDescriptor");
                il.Call(getMethodDescriptorMethod); // stack: [dynamicMethod.GetMethodDescriptor()]
                var runtimeMethodHandle = il.DeclareLocal(typeof(RuntimeMethodHandle));
                il.Stloc(runtimeMethodHandle);
                il.Ldloc(runtimeMethodHandle);
                var prepareMethodMethod = typeof(RuntimeHelpers).GetMethod("PrepareMethod", new[] { typeof(RuntimeMethodHandle) });
                if (prepareMethodMethod == null)
                    throw new MissingMethodException(typeof(RuntimeHelpers).Name, "PrepareMethod");
                il.Call(prepareMethodMethod);
                var getFunctionPointerMethod = typeof(RuntimeMethodHandle).GetMethod("GetFunctionPointer", BindingFlags.Instance | BindingFlags.Public);
                if (getFunctionPointerMethod == null)
                    throw new MissingMethodException(typeof(RuntimeMethodHandle).Name, "GetFunctionPointer");
                il.Ldloca(runtimeMethodHandle);
                il.Call(getFunctionPointerMethod); // stack: [dynamicMethod.GetMethodDescriptor().GetFunctionPointer()]
                il.Ret();
            }
            return (Func<DynamicMethod, IntPtr>)method.CreateDelegate(typeof(Func<DynamicMethod, IntPtr>));
        }
    }
}
