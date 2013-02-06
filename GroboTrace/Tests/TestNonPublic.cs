using GroboContainer.Core;
using GroboContainer.Impl;

using GroboTrace;

using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class TestNonPublic
    {
        [SetUp]
        public void SetUp()
        {
            container = new Container(new ContainerConfiguration(GetType().Module.Assembly), new TracingWrapper());
        }

        [Test]
        public void Test()
        {
            var instance = container.Get<I1>();
            Assert.IsNotNull(instance);
            instance.DoNothing();
            Assert.IsTrue(((C1)((IClassWrapper)instance).UnWrap()).Called);
        }

        public interface I1
        {
            void DoNothing();
        }

        internal class C1: I1
        {
            public bool Called { get; private set; }

            public void DoNothing()
            {
                Called = true;
            }
        }


        private Container container;
    }
}