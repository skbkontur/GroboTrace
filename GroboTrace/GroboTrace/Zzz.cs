using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;

using GrEmit.Utils;

using GroboTrace.Mono.Cecil.Cil;
using GroboTrace.Mono.Cecil.Metadata;

using RGiesecke.DllExport;

using OpCodes = GroboTrace.Mono.Cecil.Cil.OpCodes;

namespace GroboTrace
{
    public static unsafe class Zzz
    {
        static Zzz()
        {
            sizes = new int[32];
            counts = new int[32];

            int size = 1;
            int count = 1;
            for(int i = 0; i < sizes.Length; i++)
            {
                sizes[i] = size;
                counts[i] = count;

                if(i < sizes.Length - 1)
                {
                    size *= 2;
                    count += size;
                }
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate uint SignatureTokenBuilderDelegate(IntPtr corProfiler, ulong moduleId, byte* signature, int len);

        [DllExport]
        // ReSharper disable once UnusedMember.Global
        public static void Init(IntPtr corProfiler,
            [MarshalAs(UnmanagedType.FunctionPtr)] SignatureTokenBuilderDelegate signatureTokenBuilderDelegate)
        {
            signatureTokenBuilder = (moduleId, signature) =>
                {
                    fixed(byte* b = &signature[0])
                    {
                        var tokenBuilderDelegate = signatureTokenBuilderDelegate(corProfiler, moduleId, b, signature.Length);
                        return new MetadataToken(tokenBuilderDelegate);
                    }
                };
        }

        [DllExport]
        // ReSharper disable once UnusedMember.Global
        public static byte* Trace([MarshalAs(UnmanagedType.LPWStr)] string assemblyName,
                                  [MarshalAs(UnmanagedType.LPWStr)] string moduleName,
                                  ulong moduleId,
                                  uint methodToken,
                                  byte* rawMethodBody)
        {
            if(trash != 0)
                Debug.WriteLine("SETTING FIELD FROM ANOTHER MODULE WORKED!!!");

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

            int i, j;
            AddMethod(method, out i, out j);

            var signature = new MethodSignatureReader(module.ResolveSignature((int)methodToken)).Read();
            Debug.WriteLine(".NET: method has {0} parameters", signature.ParamCount);

            Debug.WriteLine(".NET: method {0} is asked to be traced", method);

            var methodBody = new CodeReader(rawMethodBody, module).ReadMethodBody();

            if(method.Name == "SqrtMod")
            {
                //var field = typeof(Zzz).GetField("trash", BindingFlags.Static | BindingFlags.Public);
                //var fieldToken = GetFieldToken(module, field);
                //Debug.WriteLine(".NET: Field token = {0}", fieldToken.ToInt32());
                //methodBody.instructions.Insert(0, Instruction.Create(OpCodes.Ldc_I4_1));
                //methodBody.instructions.Insert(1, Instruction.Create(OpCodes.Stsfld, fieldToken));
                //var m = typeof(Zzz).GetMethod("Trash", BindingFlags.Static | BindingFlags.Public);
                //var fieldToken = GetMethodToken(moduleId, module, m);
                //Debug.WriteLine(".NET: Method token = {0}", fieldToken.ToInt32());
                //methodBody.instructions.Insert(0, Instruction.Create(OpCodes.Call, fieldToken));
            }

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

            //ILGenerator ilgen;
            //ilgen.Emit(System.Reflection.Emit.OpCodes.Ldfld, typeof(Zzz).GetField(""));

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

            var codeWriter = new CodeWriter(module, sig => signatureTokenBuilder(moduleId, sig), methodBody);
            codeWriter.WriteMethodBody();

            var res = Marshal.AllocHGlobal(codeWriter.length);
            Marshal.Copy(codeWriter.buffer, 0, res, codeWriter.length);
            return (byte*)res;
        }

        private static void AddMethod(MethodBase method, out int i, out int j)
        {
            int index = Interlocked.Increment(ref numberOfMethods) - 1;
            int adjustedIndex = index;

            int arrayIndex = GetArrayIndex(index + 1);
            if(arrayIndex > 0)
                adjustedIndex -= counts[arrayIndex - 1];

            if(methods[arrayIndex] == null)
            {
                int arrayLength = sizes[arrayIndex];
                Interlocked.CompareExchange(ref methods[arrayIndex], new MethodBase[arrayLength], null);
            }

            methods[arrayIndex][adjustedIndex] = method;
            i = arrayIndex;
            j = adjustedIndex;
        }

        private static int GetArrayIndex(int count)
        {
            int arrayIndex = 0;

            if((count & 0xFFFF0000) != 0)
            {
                count >>= 16;
                arrayIndex |= 16;
            }

            if((count & 0xFF00) != 0)
            {
                count >>= 8;
                arrayIndex |= 8;
            }

            if((count & 0xF0) != 0)
            {
                count >>= 4;
                arrayIndex |= 4;
            }

            if((count & 0xC) != 0)
            {
                count >>= 2;
                arrayIndex |= 2;
            }

            if((count & 0x2) != 0)
            {
                count >>= 1;
                arrayIndex |= 1;
            }

            return arrayIndex;
        }

        private static Func<ulong, byte[], MetadataToken> signatureTokenBuilder;

        public static readonly MethodBase[][] methods = new MethodBase[32][];
        public static volatile int trash;
        private static int numberOfMethods;

        public static void Trash()
        {
            trash = 1;
        }

        private static readonly int[] sizes;
        private static readonly int[] counts;
    }
}