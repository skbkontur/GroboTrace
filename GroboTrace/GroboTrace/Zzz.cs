using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

using GrEmit.Utils;

using GroboTrace.Mono.Cecil.Cil;
using GroboTrace.Mono.Cecil.Metadata;

using RGiesecke.DllExport;

namespace GroboTrace
{
    public static unsafe class Zzz
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate uint SignatureTokenBuilderDelegate(IntPtr corProfiler, byte* signature, int len);

        private static Func<byte[], MetadataToken> signatureTokenBuilder;

        [DllExport]
        public static void Init(IntPtr corProfiler, [MarshalAs(UnmanagedType.FunctionPtr)]SignatureTokenBuilderDelegate signatureTokenBuilderDelegate)
        {
            signatureTokenBuilder = signature =>
                {
                    fixed(byte* b = &signature[0])
                        return new MetadataToken(signatureTokenBuilderDelegate(corProfiler, b, signature.Length));
                };
        }

        [DllExport]
        public static byte* Trace([MarshalAs(UnmanagedType.LPWStr)] string assemblyName,
                                  [MarshalAs(UnmanagedType.LPWStr)] string moduleName,
                                  uint methodToken,
                                  byte* rawMethodBody)
        {
            Debug.WriteLine(".NET: assembly = {0}; module = {1}", assemblyName, moduleName);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);
            if(assembly == null)
            {
                Debug.WriteLine(".NET: Unable to obtain assembly with name {0}", assemblyName);
                return null;
            }

            var module = assembly.GetModules().FirstOrDefault(m => m.FullyQualifiedName == moduleName);
            if(module == null)
            {
                Debug.WriteLine(".NET: Unable to obtain module. Assembly = {0}, module path = {1}", assemblyName, moduleName);
                return null;
            }

            MethodBase method;
            try
            {
                method = module.ResolveMethod((int)methodToken);
            }
            catch(Exception)
            {
                Debug.WriteLine(".NET: Unable to obtain method with token {2}. Assembly = {0}, module path = {1}", assemblyName, moduleName, methodToken);
                return null;
            }

            var signature = new MethodSignatureReader(module.ResolveSignature((int)methodToken)).Read();
            Debug.WriteLine(".NET: method has {0} parameters", signature.ParamCount);

            Debug.WriteLine(".NET: method {0} is asked to be traced", method);

            //return rawMethodBody;

            var methodBody = new CodeReader(rawMethodBody, module).ReadMethodBody();

            // actions with methodBody
            //if(method.Name == "SqrtMod")
            //{
            //    methodBody.instructions.Insert(0, Instruction.Create(OpCodes.Ldc_I4_0));
            //    methodBody.instructions.Insert(1, Instruction.Create(OpCodes.Ret));
            //}

            int resultLocalIndex = -1;

            if(signature.HasReturnType)
            {
                resultLocalIndex = (int)methodBody.variablesCount;
                var newSignature = new byte[methodBody.variablesSignature.Length + signature.ReturnTypeSignature.Length];
                Array.Copy(methodBody.variablesSignature, newSignature, methodBody.variablesSignature.Length);
                Array.Copy(signature.ReturnTypeSignature, 0, newSignature, methodBody.variablesSignature.Length, signature.ReturnTypeSignature.Length);
                methodBody.variablesSignature = newSignature;
                methodBody.variablesCount++;
            }

            var dummyInstr = Instruction.Create(OpCodes.Nop);
            methodBody.instructions.Insert(methodBody.instructions.Count, dummyInstr);
            int index = 0;
            while(index < methodBody.instructions.Count)
            {
                var instruction = methodBody.instructions[index];
                if(instruction.opcode == OpCodes.Ret)
                {
                    methodBody.instructions.RemoveAt(index);
                    if(resultLocalIndex >= 0)
                    {
                        methodBody.instructions.Insert(index, Instruction.Create(OpCodes.Stloc, resultLocalIndex));
                        ++index;
                    }
                    methodBody.instructions.Insert(index, Instruction.Create(OpCodes.Br, dummyInstr));
                }
                ++index;
            }
            if(resultLocalIndex >= 0)
                methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Ldloc, resultLocalIndex));
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Ret));

            var codeWriter = new CodeWriter(module, signatureTokenBuilder, methodBody);
            codeWriter.WriteMethodBody();

            var res = Marshal.AllocHGlobal(codeWriter.length);
            Marshal.Copy(codeWriter.buffer, 0, res, codeWriter.length);
            return (byte*)res;
        }
    }
}