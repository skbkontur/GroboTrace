//using GroboTrace;
//
//using NUnit.Framework;
//
//namespace Tests
//{
//    public class TestNonPublic : TestBase
//    {
//        [Test]
//        public void Test()
//        {
//            var instance = Create<I1, C1>();
//            Assert.IsNotNull(instance);
//            instance.DoNothing();
//            Assert.IsTrue(((C1)((IClassWrapper)instance).UnWrap()).Called);
//        }
//
//        public interface I1
//        {
//            void DoNothing();
//        }
//
//        internal class C1 : I1
//        {
//            public void DoNothing()
//            {
//                Called = true;
//            }
//
//            public bool Called { get; private set; }
//        }
//    }
//}