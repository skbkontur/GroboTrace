using System;

using GroboTrace;

using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public abstract class TestBase
    {
        [SetUp]
        public virtual void SetUp()
        {
            tracingWrapper = new TracingWrapper();
        }

        protected TInterface Create<TInterface, TImplementation>()
        {
            var type = tracingWrapper.Wrap(typeof(TImplementation));
            return (TInterface)Activator.CreateInstance(type, Activator.CreateInstance(typeof(TImplementation)));
        }

        protected TracingWrapper tracingWrapper;
    }
}