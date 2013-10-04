using System;
using System.Reflection;

using NUnit.Framework;

namespace Tests
{
    public class TestClassByAttributeSelector : TestBase
    {
        [Test]
        public void Test()
        {
            Console.WriteLine(typeof(ClassByAttributeSelector<ZzzAttribute>).TypeHandle.Value.ToInt64());
            Console.WriteLine(typeof(ClassByAttributeSelector<Zzz>).TypeHandle.Value.ToInt64());
            Console.WriteLine(typeof(ClassByAttributeSelector<ZzzAttribute>).GetMethod("Get").MethodHandle.Value.ToInt64());
            Console.WriteLine(typeof(ClassByAttributeSelector<Zzz>).GetMethod("Get").MethodHandle.Value.ToInt64());
            new ClassByAttributeSelector_Wrapper<ZzzAttribute>(new ClassByAttributeSelector<ZzzAttribute>());
            var instance = Create<IClassByAttributeSelector<ZzzAttribute>, ClassByAttributeSelector<ZzzAttribute>>();
            instance.Get<Zzz>(/*(attribute, s) => true,*/ /*"zzz"*/);
        }

        public class ClassByAttributeSelector<TAttribute> : IClassByAttributeSelector<TAttribute>/* where TAttribute : Attribute*/
        {
            public T Get<T>(/*Func<TAttribute, string, bool> predicate,*/ /*string code*/)// where T : class
            {
                return default(T);
            }
        }

        public interface IClassByAttributeSelector<TAttribute>/* where TAttribute : Attribute*/
        {
            T Get<T>(/*Func<TAttribute, string, bool> predicate,*/ /*string code*/); // where T : class;
        }

        public class ZzzAttribute: Attribute
        {
            
        }

        public class Zzz
        {
            
        }

        public class ClassByAttributeSelector_Wrapper<TAttribute>: IClassByAttributeSelector<TAttribute>
        {
            private static readonly MethodInfo getMethod = (MethodInfo)MethodBase.GetMethodFromHandle(typeof(ClassByAttributeSelector<TAttribute>).GetMethod("Get").MethodHandle, typeof(ClassByAttributeSelector<>).TypeHandle);

            private readonly ClassByAttributeSelector<TAttribute> impl;

            public ClassByAttributeSelector_Wrapper(ClassByAttributeSelector<TAttribute> impl)
            {
                this.impl = impl;
            }

            public T Get<T>()
            {
                return default(T);
            }
        }
    }
}