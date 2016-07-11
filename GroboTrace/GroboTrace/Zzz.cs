using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using GroboTrace.Injection;
using GroboTrace.Mono.Cecil.Cil;
using GroboTrace.Mono.Cecil.Metadata;

using RGiesecke.DllExport;

using MethodBody = GroboTrace.Mono.Cecil.Cil.MethodBody;

namespace GroboTrace
{
    [StructLayout(LayoutKind.Sequential)]
    public struct COR_IL_MAP
    {
        public uint oldOffset;
        public uint newOffset;
        public int fAccurate; // real type is bool (false = 0, true != 0)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SharpResponse
    {
        public IntPtr newMethodBody;
        public IntPtr pMapEntries;
        public uint mapEntriesCount;
    } 

    public static unsafe class Zzz
    {

        public static long TemplateForTicksSignature()
        {
            return 0L;
        }
        

        static Zzz()
        {
            __canon = typeof(object).Assembly.GetTypes().First(x => x.FullName == "System.__Canon");

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
            getMethodBaseFunctionAddress = typeof(Zzz).GetMethod("getMethodBase", BindingFlags.Public | BindingFlags.Static).MethodHandle.GetFunctionPointer();
            methodStartedAddress = typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MethodHandle.GetFunctionPointer();
            methodFinishedAddress = typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MethodHandle.GetFunctionPointer();

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
            MethodUtil.MEMORY_PROTECTION_CONSTANTS oldProtect;
            if(!MethodUtil.VirtualProtect(ticksReaderAddress, (uint)bufSize, MethodUtil.MEMORY_PROTECTION_CONSTANTS.PAGE_EXECUTE_READWRITE, &oldProtect))
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

            TicksReader = (TicksReaderDelegate)Marshal.GetDelegateForFunctionPointer(ticksReaderAddress, typeof(TicksReaderDelegate));
        }


        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate uint SignatureTokenBuilderDelegate(UIntPtr moduleId, byte* signature, int len);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void* MethodBodyAllocator(UIntPtr moduleId, uint size);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr MapEntriesAllocator(UIntPtr size);
        

        [DllExport]
        public static void Init([MarshalAs(UnmanagedType.FunctionPtr)] SignatureTokenBuilderDelegate signatureTokenBuilderDelegate,
                                [MarshalAs(UnmanagedType.FunctionPtr)] MapEntriesAllocator mapEntriesAllocator)
        {
            signatureTokenBuilder = (moduleId, signature) =>
                {
                    fixed(byte* b = &signature[0])
                    {
                        var token = signatureTokenBuilderDelegate(moduleId, b, signature.Length);
                        return new MetadataToken(token);
                    }
                };

            allocateForMapEntries = mapEntriesAllocator;
            
            RuntimeHelpers.PrepareMethod(typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MethodHandle);
            RuntimeHelpers.PrepareMethod(typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MethodHandle);
        }

        [DllExport]
        public static SharpResponse Trace(UIntPtr functionId,
                                  [MarshalAs(UnmanagedType.LPWStr)] string assemblyName,
                                  [MarshalAs(UnmanagedType.LPWStr)] string moduleName,
                                  UIntPtr moduleId,
                                  uint methodToken,
                                  byte* rawMethodBody,
                                  [MarshalAs(UnmanagedType.FunctionPtr)] MethodBodyAllocator allocateForMethodBody,
                                  uint typeGenericParameters,
                                  uint methodGenericParameters)
        {
            SharpResponse response = new SharpResponse();
            
            Debug.WriteLine(".NET: assembly = {0}; module = {1}", assemblyName, moduleName);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);
            if(assembly == null)
            {
                Debug.WriteLine(".NET: Unable to obtain assembly with name {0}", assemblyName);
                return response;
            }

            var module = assembly.GetModules().FirstOrDefault(m => m.FullyQualifiedName == moduleName);
            if(module == null)
            {
                Debug.WriteLine(".NET: Unable to obtain module. Assembly = {0}, module path = {1}", assemblyName, moduleName);
                return response;
            }

            MethodBase method;
            try
            {
                method = module.ResolveMethod((int)methodToken);
            }
            catch(Exception)
            {
                Debug.WriteLine(".NET: Unable to obtain method with token {2}. Assembly = {0}, module path = {1}", assemblyName, moduleName, methodToken);
                return response;
            }

            int i, j;
            AddMethod(method, out i, out j);

            var methodSignature = new MethodSignatureReader(module.ResolveSignature((int)methodToken)).Read();
            Debug.WriteLine(".NET: method has {0} parameters", methodSignature.ParamCount);

            Debug.WriteLine(".NET: method {0} is asked to be traced", method);

            var methodBody = new CodeReader(rawMethodBody, module).ReadMethodBody();

            
            
            sendToDebug("Plain", method, methodBody);

            var methodContainsCycles = new CycleFinder(methodBody.Instructions.ToArray()).IsThereAnyCycles();

            if (methodBody.isTiny)
            {
                Debug.WriteLine(method + " is tiny");
            }

            Debug.WriteLine("Contains cycles: " + methodContainsCycles + "\n");

            if (methodBody.isTiny || !methodContainsCycles && methodBody.Instructions.Count < 50)
            {
                Debug.WriteLine(method + " too simple to be traced");
                return response;
            }



            //if (method.Name == "Main" || method.Name == "add2" || method.Name == "twice")
            //    return response;

            List<Tuple<Instruction, int>> oldOffsets = new List<Tuple<Instruction, int>>();

            foreach (var instruction in methodBody.instructions)
            {
                oldOffsets.Add(Tuple.Create(instruction, instruction.Offset));
            }



               

            int resultLocalIndex = -1;
            int ticksLocalIndex;
            int profilerOverheadLocalIndex;
            byte[] newSignature;

            if(methodSignature.HasReturnType)
            {
                resultLocalIndex = (int)methodBody.variablesCount;
                newSignature = new byte[methodBody.VariablesSignature.Length + methodSignature.ReturnTypeSignature.Length];
                Array.Copy(methodBody.VariablesSignature, newSignature, methodBody.VariablesSignature.Length);
                Array.Copy(methodSignature.ReturnTypeSignature, 0, newSignature, methodBody.VariablesSignature.Length, methodSignature.ReturnTypeSignature.Length);
                methodBody.VariablesSignature = newSignature;
                methodBody.variablesCount++;
            }

            ticksLocalIndex = (int)methodBody.variablesCount;
            newSignature = new byte[methodBody.VariablesSignature.Length + 1];
            Array.Copy(methodBody.VariablesSignature, newSignature, methodBody.VariablesSignature.Length);
            newSignature[newSignature.Length - 1] = (byte)ElementType.I8;
            methodBody.VariablesSignature = newSignature;
            methodBody.variablesCount++;

            profilerOverheadLocalIndex = (int)methodBody.variablesCount;
            newSignature = new byte[methodBody.VariablesSignature.Length + 1];
            Array.Copy(methodBody.VariablesSignature, newSignature, methodBody.VariablesSignature.Length);
            newSignature[newSignature.Length - 1] = (byte)ElementType.I8;
            methodBody.VariablesSignature = newSignature;
            methodBody.variablesCount++;
            


            var dummyInstr = Instruction.Create(OpCodes.Nop);
            methodBody.instructions.Insert(methodBody.instructions.Count, dummyInstr);
            int index = 0;
            while(index < methodBody.instructions.Count)
            {
                var instruction = methodBody.instructions[index];
                if(instruction.opcode == OpCodes.Ret)
                {
                    // replace Ret with Nop
                    methodBody.instructions[index].OpCode = OpCodes.Nop;
                    ++index;
                    
                    if(resultLocalIndex >= 0)
                    {
                        methodBody.instructions.Insert(index, Instruction.Create(OpCodes.Stloc, resultLocalIndex));
                        ++index;
                    }
                    methodBody.instructions.Insert(index, Instruction.Create(OpCodes.Br, dummyInstr));

                }
                ++index;
            }

            var ticksReaderSignature = typeof(Zzz).Module.ResolveSignature(typeof(Zzz).GetMethod("TemplateForTicksSignature", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var ticksReaderToken = signatureTokenBuilder(moduleId, ticksReaderSignature);

            var getMethodBaseSignature = typeof(Zzz).Module.ResolveSignature(typeof(Zzz).GetMethod("getMethodBase", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var getMethodBaseToken = signatureTokenBuilder(moduleId, getMethodBaseSignature);

            var methodStartedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var methodStartedToken = signatureTokenBuilder(moduleId, methodStartedSignature);

            var methodFinishedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var methodFinishedToken = signatureTokenBuilder(moduleId, methodFinishedSignature);



            int startIndex = 0;

            methodBody.instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress));
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, ticksReaderToken));
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Stloc, ticksLocalIndex));
            
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldc_I4, i)); // [ i ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldc_I4, j)); // [ i, j ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)getMethodBaseFunctionAddress : (long)getMethodBaseFunctionAddress)); // [ i, j, funcAddr ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, getMethodBaseToken)); // [ ourMethod ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldc_I8, (long)functionId)); // [ ourMethod, functionId ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodStartedAddress : (long)methodStartedAddress)); // [ ourMethod, functionId, funcAddr ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, methodStartedToken)); // []
           
            methodBody.instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress)); // [ funcAddr ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, ticksReaderToken)); // [ ticksNow ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Dup)); // [ ticksNow, ticksNow ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldloc, ticksLocalIndex)); // [ ticksNow, ticksNow, oldTicks ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Sub)); // [ ticksNow, profilingOverhead ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Stloc, profilerOverheadLocalIndex)); // [ ticksNow ]
            methodBody.instructions.Insert(startIndex++, Instruction.Create(OpCodes.Stloc, ticksLocalIndex)); // []

            
            var tryStartInstruction = methodBody.instructions[startIndex];


            Instruction tryEndInstruction;
            Instruction finallyStartInstruction;
           
            methodBody.instructions.Insert(methodBody.instructions.Count, finallyStartInstruction = tryEndInstruction = Instruction.Create(OpCodes.Ldc_I8, (long)functionId));  // [ functionId ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress)); // [ functionId, funcAddr ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Calli, ticksReaderToken));  // [ functionId, ticks ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Ldloc, ticksLocalIndex));  // [ functionId, ticks, startTicks ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Sub));  // [ functionId, elapsed ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Ldloc, profilerOverheadLocalIndex));  // [ functionId, elapsed, profilerOverhead ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodFinishedAddress : (long)methodFinishedAddress)); // [ functionId, elapsed, profilerOverhead , funcAddr ]
            methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Calli, methodFinishedToken)); // []




            Instruction endFinallyInstruction;
            methodBody.instructions.Insert(methodBody.instructions.Count, endFinallyInstruction = Instruction.Create(OpCodes.Endfinally));

            Instruction finallyEndInstruction;

            if (resultLocalIndex >= 0)
            {

                methodBody.instructions.Insert(methodBody.instructions.Count, finallyEndInstruction = Instruction.Create(OpCodes.Ldloc, resultLocalIndex));
                methodBody.instructions.Insert(methodBody.instructions.Count, Instruction.Create(OpCodes.Ret));
            }
            else
            {
                methodBody.instructions.Insert(methodBody.instructions.Count, finallyEndInstruction = Instruction.Create(OpCodes.Ret));
            }

            ExceptionHandler newException = new ExceptionHandler(ExceptionHandlerType.Finally);
            newException.TryStart = tryStartInstruction;
            newException.TryEnd = tryEndInstruction;
            newException.HandlerStart = finallyStartInstruction;
            newException.HandlerEnd = finallyEndInstruction;

            methodBody.instructions.Insert(methodBody.instructions.IndexOf(tryEndInstruction), Instruction.Create(OpCodes.Leave, finallyEndInstruction ));
            
            methodBody.ExceptionHandlers.Add(newException);


            var codeWriter = new CodeWriter(module, sig => signatureTokenBuilder(moduleId, sig), methodBody, typeGenericParameters, methodGenericParameters);
            codeWriter.WriteMethodBody();

         
            sendToDebug("Changed", method, methodBody);
            
            var newMethodBody = (IntPtr)allocateForMethodBody(moduleId, (uint)codeWriter.length);
            Marshal.Copy(codeWriter.buffer, 0, newMethodBody, codeWriter.length);

            response.newMethodBody = newMethodBody;

            

            var startMapEntries = allocateForMapEntries((UIntPtr)(oldOffsets.Count * Marshal.SizeOf(typeof(COR_IL_MAP))));

            var pointer = startMapEntries;
            foreach (var tuple in oldOffsets)
            {
                var mapEntry = new COR_IL_MAP
                    {
                        fAccurate = 1,
                        oldOffset = (uint)tuple.Item2,
                        newOffset = (uint)tuple.Item1.Offset
                    };

                Marshal.StructureToPtr(mapEntry, pointer, true);
                pointer += Marshal.SizeOf(typeof(COR_IL_MAP));
            }

            response.pMapEntries = startMapEntries;
            response.mapEntriesCount = (uint)oldOffsets.Count;
            
            return response;
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


        private static void sendToDebug(String label, MethodBase method, MethodBody methodBody)
        {
            Debug.WriteLine("");
            Debug.WriteLine(label + " " + method.DeclaringType + "." + method.Name);
            Debug.WriteLine(methodBody);
            Debug.WriteLine("");
        }

        public static void Trash()
        {
            trash = 1;
        }


        public static object getMethodBase(int i, int j)
        {
            return methods[i][j];
        }

        public delegate long TicksReaderDelegate();
        private static IntPtr ticksReaderAddress;
        public static TicksReaderDelegate TicksReader;
        private static IntPtr getMethodBaseFunctionAddress;
        private static IntPtr methodStartedAddress;
        private static IntPtr methodFinishedAddress;

        private static Func<UIntPtr, byte[], MetadataToken> signatureTokenBuilder;
        private static MapEntriesAllocator allocateForMapEntries;

        private static readonly MethodBase[][] methods = new MethodBase[32][];
        public static volatile int trash;
        private static int numberOfMethods;

        private static readonly int[] sizes;
        private static readonly int[] counts;
        public static Type __canon;
    }
}