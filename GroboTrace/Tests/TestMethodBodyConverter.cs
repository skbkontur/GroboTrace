using System;
using System.Collections.Generic;
using System.Linq;
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

            using (var il = new GroboIL(dynamicMethod))
            {

                il.BeginExceptionBlock();
                il.Ldarg(0);
                il.Ldarg(1);
                il.Add();
                il.Ret();
                il.BeginCatchBlock(typeof(Exception));
                il.Pop();
                il.Ldc_I4(12);
                il.Ret();
                il.BeginFinallyBlock();
                il.Nop();
                il.EndExceptionBlock();
                il.Ldc_I4(100);
                il.Ret();
            }

            
            
             new DynamicMethodExtender(dynamicMethod).Trace();



        }



    }
}
