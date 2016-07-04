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
        public delegate uint SignatureTokenBuilderDelegate(IntPtr corProfiler, uint moduleId, byte* signature, int len);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate uint FieldReferencerDelegate(IntPtr corProfiler, uint moduleId, [MarshalAs(UnmanagedType.LPWStr)] string typeName, byte* signature, int len);

        [DllExport]
        // ReSharper disable once UnusedMember.Global
        public static void Init(IntPtr corProfiler,
            [MarshalAs(UnmanagedType.FunctionPtr)] SignatureTokenBuilderDelegate signatureTokenBuilderDelegate,
            [MarshalAs(UnmanagedType.FunctionPtr)] FieldReferencerDelegate fieldReferencerDelegate)
        {
            signatureTokenBuilder = (moduleId, signature) =>
                {
                    fixed(byte* b = &signature[0])
                        return new MetadataToken(signatureTokenBuilderDelegate(corProfiler, moduleId, b, signature.Length));
                };
            fieldReferencer = (moduleId, typeName, signature) =>
                {
                    fixed (byte* b = &signature[0])
                        return new MetadataToken(fieldReferencerDelegate(corProfiler, moduleId, typeName, b, signature.Length));
                };
        }

        [DllExport]
        // ReSharper disable once UnusedMember.Global
        public static byte* Trace([MarshalAs(UnmanagedType.LPWStr)] string assemblyName,
                                  [MarshalAs(UnmanagedType.LPWStr)] string moduleName,
                                  uint moduleId,
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
                var m = typeof(Zzz).GetMethod("Trash", BindingFlags.Static | BindingFlags.Public);
                var fieldToken = GetMethodToken(moduleId, module, m);
                Debug.WriteLine(".NET: Method token = {0}", fieldToken.ToInt32());
                methodBody.instructions.Insert(0, Instruction.Create(OpCodes.Call, fieldToken));
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

        public static MetadataToken GetFieldToken(uint moduleId, Module module, FieldInfo field)
        {
            // Assuming field is declared in non-generic non-nested type
            if(field.DeclaringType == null)
                throw new InvalidOperationException();
            if(module.Assembly.Equals(field.DeclaringType.Module.Assembly))
                throw new NotSupportedException();
            if(field.DeclaringType.DeclaringType != null)
                throw new NotSupportedException();

            var typeRefToken = fieldReferencer(moduleId, field.DeclaringType.FullName, field.Module.ResolveSignature(field.MetadataToken));
            return typeRefToken;
        }

        public static MetadataToken GetMethodToken(uint moduleId, Module module, MethodInfo method)
        {
            // Assuming field is declared in non-generic non-nested type
            if (method.DeclaringType == null)
                throw new InvalidOperationException();
            if (module.Assembly.Equals(method.DeclaringType.Module.Assembly))
                throw new NotSupportedException();
            if (method.DeclaringType.DeclaringType != null)
                throw new NotSupportedException();

            var typeRefToken = fieldReferencer(moduleId, method.DeclaringType.FullName, method.Module.ResolveSignature(method.MetadataToken));
            return typeRefToken;
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

        private static Func<uint, byte[], MetadataToken> signatureTokenBuilder;
        private static Func<uint, string, byte[], MetadataToken> fieldReferencer;

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