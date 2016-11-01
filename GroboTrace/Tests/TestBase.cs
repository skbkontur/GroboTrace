//using System;
//
//using GroboTrace;
//
//using NUnit.Framework;
//
//namespace Tests
//{
//    [TestFixture]
//    public abstract class TestBase
//    {
//        protected static TInterface Create<TInterface, TImplementation>(TracingWrapper tracingWrapper)
//        {
//            Type wrapperType;
//            tracingWrapper.TryWrap(typeof(TImplementation), out wrapperType);
//            //TracingWrapper.assembly.Save("zzz.dll");
//            return (TInterface)Activator.CreateInstance(wrapperType, Activator.CreateInstance(typeof(TImplementation)));
//        }
//
//        protected TInterface Create<TInterface, TImplementation>()
//        {
//            return Create<TInterface, TImplementation>(tracingWrapper);
//        }
//
//        protected static TInterface Create<TInterface, TImplementation>(TracingWrapper tracingWrapper, Func<TImplementation> factory)
//        {
//            Type wrapperType;
//            tracingWrapper.TryWrap(typeof(TImplementation), out wrapperType);
//            //TracingWrapper.assembly.Save("zzz.dll");
//            return (TInterface)Activator.CreateInstance(wrapperType, factory());
//        }
//
//        protected TInterface Create<TInterface, TImplementation>(Func<TImplementation> factory)
//        {
//            return Create<TInterface, TImplementation>(tracingWrapper, factory);
//        }
//
//        protected readonly TracingWrapper tracingWrapper = new TracingWrapper(new TracingWrapperConfigurator());
//    }
//}