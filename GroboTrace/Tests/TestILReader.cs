using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

using GroboTrace;
using GroboTrace.Injection;

using NUnit.Framework;

namespace Tests
{

    public class ForTests
    {
        public static int divideThanToStringThanGetLength(int a, int b)
        {
            int answer = 0;
            try
            {
                int div = a / b;
                String s = div.ToString();
                answer = s.Length;

            }
            catch(DivideByZeroException)
            {
                answer = 3;
            }
            catch(NotSupportedException)
            {
                answer = 7;
            }
            finally
            {
                
                Console.WriteLine("xfssp");
            }


            try
            {
                int sum = a + b;
                Console.WriteLine(sum);
            }
            catch(Exception)
            {
                Console.WriteLine("algglgb");
            }

            try
            {
                int div = a / b;
                Console.WriteLine(div);
            }
            finally 
            {
                Console.WriteLine("It is finally");
            }





            return answer;

            //int answer;
            
            //int div = a / b;
            //String s = div.ToString();
            //answer = s.Length;

            //return answer;


        }

        public static int bla(int a, int b)
        {

            try
            {
                Console.WriteLine(a / b);
                return 100;
            }
            //catch (DivideByZeroException)
            //{
            //    Console.WriteLine("1 catch");
            //}
            //catch (ArithmeticException)
            //{
            //    Console.WriteLine("2 catch");
            //}
            finally
            {
                Console.WriteLine("It is finally");
            }
           
        }

        private static MethodInfo[] methods;

        //public static bool isEqual_hacked(int a, int b)
        //{
        //    TracingAnalyzer.MethodStarted(methods[123], 89463782643);
        //    var stopwatch = Stopwatch.StartNew();
        //    bool res;
        //    if(a == b)
        //    {
        //        res = true;
        //        goto _ret;
        //    }
        //    res = false;
        //    goto _ret;
        //_ret:
        //    var elapsed = stopwatch.Elapsed;
        //    TracingAnalyzer.MethodFinished(methods[123], 89463782643, elapsed.Ticks);
        //    return res;
        //}
    }


    [TestFixture]
    public class TestILReader
    {
        
        [Test]
        public void Test1()
        {
            var methodInfo = typeof(ForTests).GetMethod("bla");

            MethodWrapper wrapper = new MethodWrapper(methodInfo);
            wrapper.Trace();

            Console.WriteLine(ForTests.bla(76,1));

//            Delegate newMethod;
//            wrapper.delegates.TryTake(out newMethod);
//
//
//            var result = newMethod.DynamicInvoke(5, 7);
            
            return;
        }
    }
}