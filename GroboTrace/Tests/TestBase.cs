using System;

using GroboTrace;

using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public abstract class TestBase
    {
        protected static TInterface Create<TInterface, TImplementation>()
        {
            var type = TracingWrapper.Wrap(typeof(TImplementation));
            return (TInterface)Activator.CreateInstance(type, Activator.CreateInstance(typeof(TImplementation)));
        }
    }
}