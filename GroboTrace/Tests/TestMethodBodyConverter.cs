using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using GrEmit;
using GroboTrace;
using GroboTrace.Injection;

using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class TestMethodBodyConverter
    {
        [Test]
        public void Simple()
        {
            var dynamicMethod = new DynamicMethod(Guid.NewGuid().ToString(), typeof(int), new[] { typeof(int), typeof(int) }, typeof(string), true);

            using (var il = new GroboIL(dynamicMethod, false))
            {
                il.Ldarg(0);
                il.Ldarg(1);
                il.Add();
                il.Ret();
            }
            

            new DynamicMethodExtender(dynamicMethod).Trace();

            var func = (Func<int, int, int>)dynamicMethod.CreateDelegate(typeof(Func<int, int, int>));
 
            Console.WriteLine(func(2, 3));

        }


        [Test]
        public void WithExceptions()
        {
            var dynamicMethod = new DynamicMethod(Guid.NewGuid().ToString(), typeof(int), new[] { typeof(int), typeof(int) }, typeof(string), true);

            using (var il = new GroboIL(dynamicMethod, false))
            {
                var endLabel = il.DefineLabel("end");
                var resultLocal = il.DeclareLocal(typeof(int), "result");
                il.BeginExceptionBlock();
                il.Ldarg(0);
                il.Ldarg(1);
                il.Div(false);
                il.Stloc(resultLocal);
                il.BeginCatchBlock(typeof(DivideByZeroException));
                il.Pop();
                il.WriteLine("Division by zero caught");
                il.Ldc_I4(0);
                il.Stloc(resultLocal);
                il.BeginFinallyBlock();
                il.WriteLine("It is finally");
                il.EndExceptionBlock();
                il.MarkLabel(endLabel);
                il.Ldloc(resultLocal);
                il.Ret();
            }

            new DynamicMethodExtender(dynamicMethod).Trace();

            var func = (Func<int, int, int>)dynamicMethod.CreateDelegate(typeof(Func<int, int, int>));

            Console.WriteLine(func(12, 5));
            Console.WriteLine(func(5,0));
            
        }








    }
}
