//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Reflection;
//using System.Runtime.CompilerServices;
//
//using GroboTrace;
//using GroboTrace.Injection;
//
//using NUnit.Framework;
//
//namespace Tests
//{
//
//    public class ForTests
//    {
//        public static long binPow(long x, int n)
//        {
//            if (n == 0) return 1;
//            long tmp = binPow(x, n / 2);
//
//            if (n % 2 == 0)
//            {
//                return tmp * tmp;
//            }
//            else
//            {
//                return tmp * tmp * x;
//            }
//        }
//
//
//        //[MethodImpl(MethodImplOptions.NoInlining)]
//        public static int add(int a, int b)
//        {
//            return a + b;
//        }
//
//        public static int crash(int a, int b)
//        {
//            int div = a / b;
//            Console.WriteLine(div);
//            return div;
//        }
//
//        public static int exception1(int a, int b)
//        {
//            int div = 0;
//            try
//            {
//                div = a / b;
//            }
//            catch
//            {
//                Console.WriteLine("Exception has occurred");
//            }
//
//            return div;
//        }
//
//        public static int exception2(int a, int b)
//        {
//            int div;
//            try
//            {
//                div = a / b;
//            }
//            finally
//            {
//                Console.WriteLine("It is finally");
//            }
//
//            return div;
//        }
//
//        public static int exception3(int a, int b)
//        {
//            int div = 100;
//            try
//            {
//                div = a / b;
//                Console.WriteLine(div);
//            }
//            catch(ArithmeticException ex)
//            {
//                Console.WriteLine(ex);
//            }
//            finally
//            {
//                Console.WriteLine("It is finally");
//            }
//
//            return div;
//        }
//
//        public static int exception4(int a, int b)
//        {
//            int div = 100;
//            try
//            {
//                div = a / b;
//                Console.WriteLine(div);
//            }
//            catch(NullReferenceException ex)
//            {
//                Console.WriteLine(ex);
//            }
//            catch
//            {
//                Console.WriteLine("2nd catch block");
//            }
//            finally
//            {
//                Console.WriteLine("It is finally");
//            }
//
//            return div;
//        }
//
//        public static int exception5(string s)
//        {
//            int size = 0;
//            try
//            {
//                Console.WriteLine(s);
//            }
//            catch (NullReferenceException ex)
//            {
//                Console.WriteLine(ex);
//                throw;
//            }
//            finally
//            {
//                Console.WriteLine("It is finally");
//            }
//
//            return size;
//        }
//
//
//
//        public static int exception6(int a, int b, string s)
//        {
//            int div = 100;
//            try
//            {
//                div = a / b;
//                Console.WriteLine(div);
//
//                try
//                {
//                    Console.WriteLine("String size = " + s.Length);
//                }
//                catch(NullReferenceException ex)
//                {
//                    Console.WriteLine(ex);
//                    throw;
//                }
//                finally
//                {
//                    Console.WriteLine("It is inside finaly");
//                }
//            }
//            catch (ArithmeticException ex)
//            {
//                Console.WriteLine(ex);
//            }
//            catch
//            {
//                Console.WriteLine("2nd catch block");
//            }
//            finally
//            {
//                Console.WriteLine("It is finally");
//            }
//
//            return div;
//        }
//
//
//
//
//
//
//    }
//
//    public class Aaa<T>
//    {
//        public void swap(ref T a, ref T b)
//        {
//            //Console.WriteLine(a.ToString() + " " + b.ToString());
//
//            T c = a;
//            a = b;
//            b = c;
//
//            //Console.WriteLine(a.ToString() + " " + b.ToString());
//        }
//    }
//
//
//
//
//    [TestFixture]
//    public class TestILReader
//    {
//
//        [Test]
//        public void TestString()
//        {
//            TracingAnalyzer.ClearStats();
//            var wrapper = new MethodWrapper();
//            var methodInfos = typeof(string).GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public);
//
//
//
//            foreach (var methodInfo in methodInfos)
//            {
//                Console.WriteLine(methodInfo);
//
//                try
//                {
//                    wrapper.Trace(methodInfo);
//                    Console.WriteLine("OK");
//                }
//                catch (Exception ex)
//                {
//
//                    Console.WriteLine("FAIL");
//                    Console.WriteLine(ex);
//                }
//
//                Console.WriteLine();
//
//
//            }
//
//
//
//
//
//
//
//
//        }
//
//
//
//
//        [Test]
//        public void TestDictionary()
//        {
//            TracingAnalyzer.ClearStats();
//            var wrapper = new MethodWrapper();
//            var methodInfos = typeof(Dictionary<string, int>).GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
//
//            
//            wrapper.Trace(methodInfos[47]);
//
//
//
//
//
//
//        
//        }
//
//        [Test]
//        public void MyGenericClass()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(Aaa<>).MakeGenericType(new[]{typeof(string)}).GetMethod("swap");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            int a = 2;
//            int b = 3;
//            Aaa<int> aaa = new Aaa<int>();
//
//            aaa.swap(ref a, ref b);
//
//            //Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children.Length);
//
//            //Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls,Is.EqualTo(1));
//            //Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//
//
//
//
//
//        [Test]
//        public void SmallMethod()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("add");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//            
//
//            Assert.That(ForTests.add(2,3),Is.EqualTo(5));
//
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children.Length);
//
//            //Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls,Is.EqualTo(1));
//            //Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Recursion()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("binPow");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.binPow(2,10),Is.EqualTo(1024));
//
//            //Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//
//
//        [Test]
//        public void MethodCrashes()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("crash");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.Throws<DivideByZeroException>(() => ForTests.crash(7,0));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//
//        [Test]
//        public void Exception1()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception1");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception1(5,0), Is.EqualTo(0));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception1_1()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception1");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception1(5, 1), Is.EqualTo(5));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception2()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception2");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception2(5, 2), Is.EqualTo(2));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception2_1()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception2");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.Throws<DivideByZeroException>(() => ForTests.exception2(5, 0));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception3()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception3");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception3(5, 2), Is.EqualTo(2));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception3_1()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception3");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception3(5, 0), Is.EqualTo(100));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception4()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception4");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception4(5, 2), Is.EqualTo(2));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception4_1()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception4");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception4(5, 0), Is.EqualTo(100));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception5()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception5");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception5("abcd"), Is.EqualTo(0));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception5_1()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception5");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception5(null), Is.EqualTo(0));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//
//
//
//        [Test]
//        public void Exception6()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception6");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception6(5, 2, "abcd"), Is.EqualTo(2));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception6_1()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception6");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception6(5, 0, "abcd"), Is.EqualTo(100));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//
//        [Test]
//        public void Exception6_2()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception6");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception6(5, 2, null), Is.EqualTo(2));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//        [Test]
//        public void Exception6_3()
//        {
//            TracingAnalyzer.ClearStats();
//            var methodInfo = typeof(ForTests).GetMethod("exception6");
//            MethodWrapper wrapper = new MethodWrapper();
//            wrapper.Trace(methodInfo);
//
//            Assert.That(ForTests.exception6(5, 0, null), Is.EqualTo(100));
//
//            Assert.That(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Calls, Is.EqualTo(1));
//            Console.WriteLine(TracingAnalyzer.GetStats().Tree.Children[0].MethodStats.Ticks);
//        }
//
//
//    }
//}