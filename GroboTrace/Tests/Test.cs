using System;
using System.Diagnostics;

using GroboContainer.Core;
using GroboContainer.Impl;

using GroboTrace;

using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class Test
    {
        [SetUp]
        public void SetUp()
        {
            container = new Container(new ContainerConfiguration(GetType().Module.Assembly), new TracingWrapper());
        }

        [Test]
        public void TestImplIsCorrectlyCalled()
        {
            var instance = container.Get<I1>();
            int z;
            Assert.AreEqual("3", instance.F(1, out z));
            Assert.AreEqual(2, z);
            instance.X = 5;
            Assert.AreEqual(5, instance.X);
        }

        [Test]
        public void TestUnWrap()
        {
            var instance = container.Get<I1>();
            var c = (C1)((IClassWrapper)instance).UnWrap();
            int z;
            Assert.AreEqual("3", instance.F(1, out z));
            Assert.AreEqual(2, z);
            instance.X = 5;
            Assert.AreEqual(5, instance.X);
        }

        [Test]
        public void TestPerformance()
        {
            var instance = container.Get<I1>();
            const int iter = 100000000;
            var stopwatch = Stopwatch.StartNew();
            for(int i = 0; i < iter; ++i)
                instance.DoNothing();
            var elapsed = stopwatch.Elapsed;
            Console.WriteLine(string.Format("{0} millions operations per second", iter * 1.0 / elapsed.TotalSeconds / 1000000.0));
        }

        public class C1 : I1
        {
            public string F(int x, out int z)
            {
                z = x + 1;
                return (x + 2).ToString();
            }

            public void DoNothing()
            {
            }

            public int X { get; set; }
        }

        public interface I1
        {
            string F(int x, out int z);
            void DoNothing();
            int X { get; set; }
        }

        private Container container;
    }
}