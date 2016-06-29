using System;
using System.Runtime.CompilerServices;

namespace GroboTrace.Injection
{

    public class Program
    {
        public string CompareOneAndTwo()
        {
            int a = 1;
            int b = 2;
            if (a < b)
            {
                return "Number 1 is less than 2";
            }
            else
            {
                return "Number 1 is greater than 2 (O_o)";
            }
        }


        public static void Main(string[] args)
        {
            var methodInfo = typeof (Program).GetMethod("CompareOneAndTwo");
            RuntimeHelpers.PrepareMethod(methodInfo.MethodHandle);
            var methodBody = methodInfo.GetMethodBody();
            var parsedBody = new MethodBodyModifier(methodInfo);
            parsedBody.GetBodyCode();
            return;
        }

         
    }
}