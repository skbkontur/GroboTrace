using System;
using System.Collections.Generic;

using GroboTrace;

using NUnit.Framework;

namespace Tests
{
    public class TestInterface: TestBase
    {
        [Test]
        public void TestImplIsCorrectlyCalled()
        {
            var type = TracingWrapper.Wrap(typeof(I1));
            var i1 = (I1)Activator.CreateInstance(type, new object[] {new C1()});
            int z;
            Assert.AreEqual("3", i1.F(1, out z));
            Assert.AreEqual(2, z);
            i1.X = 5;
            Assert.AreEqual(5, i1.X);
            var i2 = (I2)i1;
            i2.X = "zzz";
            Assert.AreEqual("zzz", i2.X);
        }

        [Test]
        public void TestReturnTypeIsInterface()
        {
            var i3 = Create<I3, C3>();
            var i2 = i3.GetI2("zzz");
            Assert.AreEqual("zzz", i2.X);
        }

        [Test]
        public void TestIEnumerable()
        {
            var type = TracingWrapper.Wrap(typeof(IEnumerable<int>));
            var enumerable = (IEnumerable<int>)Activator.CreateInstance(type, new[] {1, 2, 3});
            foreach(var item in enumerable)
                Console.WriteLine(item);
            var enumerator = enumerable.GetEnumerator();
            Console.WriteLine(enumerator.MoveNext());
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
            string I2.X { get; set; }
        }

        public interface I2
        {
            string X { get; set; }
        }

        public interface I1: I2
        {
            string F(int x, out int z);
            void DoNothing();
            int X { get; set; }
        } 

        public interface I3
        {
            I2 GetI2(string x);
        }

        public class C3 : I3
        {
            public I2 GetI2(string x)
            {
                var result = Create<I1, C1>();
                ((I2)result).X = x;
                return result;
            }
        }
    }
}