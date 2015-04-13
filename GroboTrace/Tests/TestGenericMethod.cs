using System.Collections.Generic;

using NUnit.Framework;

namespace Tests
{
    public class TestGenericMethod : TestBase
    {
        [Test]
        public void Test1()
        {
            var instance = Create<I2, C2>();
            var arg1 = new C1<int>();
            instance.F(arg1, -1);
            Assert.AreEqual(1, arg1.Count);
            Assert.AreEqual(-1, arg1[0]);
        }

        [Test]
        public void Test2()
        {
            var instance = Create<I3<int, string>, C3<int, string>>();
        }

        public class C1<T> : I1<T>
        {
            public void Add(T arg)
            {
                list.Add(arg);
            }

            public T this[int index] { get { return list[index]; } set { list[index] = value; } }

            public int Count { get { return list.Count; } }
            private readonly List<T> list = new List<T>();
        }

        public class C2 : I2
        {
            public void F<T1, T2>(T1 arg1, T2 arg2) where T1 : I1<T2>
            {
                arg1.Add(arg2);
            }
        }

        public interface I1<T>
        {
            void Add(T arg);
        }

        public interface I2
        {
            void F<T1, T2>(T1 arg1, T2 arg2)
                where T1 : I1<T2>;
        }

        public interface I3<T1, T2>
        {
        }

        public class C3<T1, T2> : C1<T1[]>, I3<T1, T2>
        {
        }


    }
}