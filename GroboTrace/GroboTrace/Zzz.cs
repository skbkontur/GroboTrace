using System.Reflection;
using System.Runtime.InteropServices;

using GroboTrace.Mono.Cecil;
using GroboTrace.Mono.Cecil.Cil;
using GroboTrace.Mono.Cecil.PE;

namespace GroboTrace
{
    public static unsafe class Zzz
    {
        public static byte* Trace(byte* rawMethodBody)
        {
            Module module = ???;
            var methodBody = new CodeReader(rawMethodBody, null).ReadMethodBody();
            
            // actions with methodBody

//            ModuleDefinition z;
//            TypeDefinition t;
//            var methodDefinition = new MethodDefinition("zzz", MethodAttributes.Public, null);
//            methodDefinition.Body = new MethodBody();
//            t.Methods.Add(methodDefinition);

            var codeWriter = new CodeWriter();
            codeWriter.WriteMethodBody(methodBody);
            var res = Marshal.AllocHGlobal(codeWriter.length);
            Marshal.Copy(codeWriter.buffer, 0, res, codeWriter.length);
            return (byte*)res;
        }
    }
}