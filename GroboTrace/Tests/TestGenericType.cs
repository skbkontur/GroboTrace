//using GroboTrace;
//
//using NUnit.Framework;
//
//namespace Tests
//{
//    public class TestGenericType : TestBase
//    {
//        [Test]
//        public void Test()
//        {
//            var i1 = Create<I1, C1>();
//            var i2 = i1.GetI2("zzz");
//            i2.Add("qxx");
//        }
//
//        public class C1 : I1
//        {
//            public I2<T> GetI2<T>(T obj) // where T : class
//            {
//                return new C2<T>(obj);
//            }
//        }
//
//        public class C2<T> : I2<T> // where T : class
//        {
//            public C2(T obj)
//            {
//            }
//
//            public void Add(T obj)
//            {
//            }
//        }
//
//        public class C1_Wrapper : I1
//        {
//            public C1_Wrapper(C1 impl)
//            {
//                this.impl = impl;
//            }
//
//            public I2<T> GetI2<T>(T obj)
//            {
//                var result = impl.GetI2(obj);
//                if(!(result is IClassWrapper))
//                    result = new I2_Wrapper<T>(result);
//                return result;
//            }
//
//            private readonly C1 impl;
//        }
//
//        public class I2_Wrapper<T> : I2<T>
//        {
//            public I2_Wrapper(I2<T> impl)
//            {
//                this.impl = impl;
//            }
//
//            public void Add(T obj)
//            {
//                impl.Add(obj);
//            }
//
//            private readonly I2<T> impl;
//        }
//
//        public interface I1
//        {
//            I2<T> GetI2<T>(T obj) /* where T : class*/;
//        }
//
//        public interface I2<T> // where T: class
//        {
//            void Add(T obj);
//        }
//    }
//}