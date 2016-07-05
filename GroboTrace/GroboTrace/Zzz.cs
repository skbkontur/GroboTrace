using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using GroboTrace.Mono.Cecil.Cil;
using GroboTrace.Mono.Cecil.Metadata;

using RGiesecke.DllExport;

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

            EmitTicksReader();
        }

        private static void EmitTicksReader()
        {
            byte[] code;
            if(IntPtr.Size == 8)
            {
                // x64
                code = new byte[]
                    {
                        0x0f, 0x31, // rdtsc
                        0x48, 0xc1, 0xe2, 0x20, // shl rdx, 32
                        0x48, 0x09, 0xd0, // or rax, rdx
                        0xc3, // ret
                    };
            }
            else
            {
                // x86
                code = new byte[]
                    {
                        0x0F, 0x31, // rdtsc
                        0xC3 // ret
                    };
            }
            var bufSize = code.Length + 8;
            ticksReaderAddress = Marshal.AllocHGlobal(bufSize);
            MEMORY_PROTECTION_CONSTANTS oldProtect;
            if(!VirtualProtect(ticksReaderAddress, (uint)bufSize, MEMORY_PROTECTION_CONSTANTS.PAGE_EXECUTE_READWRITE, &oldProtect))
                throw new InvalidOperationException();
            int align = 7;
            ticksReaderAddress = new IntPtr((ticksReaderAddress.ToInt64() + align) & ~align);

            var pointer = (byte*)ticksReaderAddress;
            fixed(byte* p = &code[0])
            {
                var pp = p;
                for(var i = 0; i < code.Length; ++i)
                    *pointer++ = *pp++;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern unsafe bool VirtualProtect(IntPtr lpAddress, uint dwSize, MEMORY_PROTECTION_CONSTANTS flNewProtect, MEMORY_PROTECTION_CONSTANTS* lpflOldProtect);

        [Flags]
        private enum MEMORY_PROTECTION_CONSTANTS
        {
            PAGE_EXECUTE = 0x10,
            PAGE_EXECUTE_READ = 0x20,
            PAGE_EXECUTE_READWRITE = 0x40,
            PAGE_EXECUTE_WRITECOPY = 0x80,
            PAGE_NOACCESS = 0x01,
            PAGE_READONLY = 0x02,
            PAGE_READWRITE = 0x04,
            PAGE_WRITECOPY = 0x08,
            PAGE_GUARD = 0x100,
            PAGE_NOCACHE = 0x200,
            PAGE_WRITECOMBINE = 0x400,
            PAGE_TARGETS_INVALID = 0x40000000,
            PAGE_TARGETS_NO_UPDATE = 0x40000000,
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate uint SignatureTokenBuilderDelegate(UIntPtr moduleId, byte* signature, int len);

        [DllExport]
        public static void Init([MarshalAs(UnmanagedType.FunctionPtr)] SignatureTokenBuilderDelegate signatureTokenBuilderDelegate)
        {
            signatureTokenBuilder = (moduleId, signature) =>
                {
                    fixed(byte* b = &signature[0])
                    {
                        var tokenBuilderDelegate = signatureTokenBuilderDelegate(moduleId, b, signature.Length);
                        return new MetadataToken(tokenBuilderDelegate);
                    }
                };

            RuntimeHelpers.PrepareMethod(typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MethodHandle);
            RuntimeHelpers.PrepareMethod(typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MethodHandle);
        }

        [DllExport]
        public static byte* Trace([MarshalAs(UnmanagedType.LPWStr)] string assemblyName,
                                  [MarshalAs(UnmanagedType.LPWStr)] string moduleName,
                                  UIntPtr moduleId,
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

            var methodSignature = new MethodSignatureReader(module.ResolveSignature((int)methodToken)).Read();
            Debug.WriteLine(".NET: method has {0} parameters", methodSignature.ParamCount);

            Debug.WriteLine(".NET: method {0} is asked to be traced", method);

            var methodBody = new CodeReader(rawMethodBody, module).ReadMethodBody();

            if(method.Name == "gcd")
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

            if(methodSignature.HasReturnType)
            {
                resultLocalIndex = (int)methodBody.variablesCount;
                var newSignature = new byte[methodBody.variablesSignature.Length + methodSignature.ReturnTypeSignature.Length];
                Array.Copy(methodBody.variablesSignature, newSignature, methodBody.variablesSignature.Length);
                Array.Copy(methodSignature.ReturnTypeSignature, 0, newSignature, methodBody.variablesSignature.Length, methodSignature.ReturnTypeSignature.Length);
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

        public static void Trash()
        {
            trash = 1;
        }

        private static IntPtr ticksReaderAddress;

        private static Func<UIntPtr, byte[], MetadataToken> signatureTokenBuilder;

        public static readonly MethodBase[][] methods = new MethodBase[32][];
        public static volatile int trash;
        private static int numberOfMethods;

        private static readonly int[] sizes;
        private static readonly int[] counts;
    }
}