using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using GroboTrace.Injection;
using GroboTrace.MethodBodyParsing;

using RGiesecke.DllExport;

using ExceptionHandler = GroboTrace.MethodBodyParsing.ExceptionHandler;
using MetadataToken = GroboTrace.MethodBodyParsing.MetadataToken;
using MethodBody = GroboTrace.MethodBodyParsing.MethodBody;
using OpCode = GroboTrace.MethodBodyParsing.OpCode;
using OpCodes = GroboTrace.MethodBodyParsing.OpCodes;

namespace GroboTrace
{
    public static class MetadataExtensions
    {
        private static readonly Type __canon = typeof(object).Assembly.GetTypes().First(t => t.FullName == "System.__Canon");
        private static readonly Type[] universalParameters = Enumerable.Repeat(__canon, 1024).ToArray();

        public static object Resolve(this Module module, MetadataToken token)
        {
            switch (token.TokenType)
            {
                case TokenType.Method:
                case TokenType.MethodSpec:
                    return module.ResolveMethod(token.ToInt32(), universalParameters, universalParameters);
                case TokenType.MemberRef:
                    return module.ResolveMember(token.ToInt32(), universalParameters, universalParameters);
                case TokenType.Field:
                    return module.ResolveField(token.ToInt32(), universalParameters, universalParameters);
                case TokenType.TypeDef:
                case TokenType.TypeRef:
                    return module.ResolveType(token.ToInt32(), universalParameters, universalParameters);
                case TokenType.Signature:
                    return module.ResolveSignature(token.ToInt32());
                case TokenType.String:
                    return module.ResolveString(token.ToInt32());
                default:
                    throw new NotSupportedException();
            }
        }

        public static MethodBase ResolveMethod(Module module, MetadataToken token)
        {
            switch (token.TokenType)
            {
                case TokenType.MethodSpec:
                case TokenType.Method:
                    return module.ResolveMethod(token.ToInt32(), universalParameters, universalParameters);
                case TokenType.MemberRef:
                    var member = module.ResolveMember(token.ToInt32(), universalParameters, universalParameters);
                    switch (member.MemberType)
                    {
                        case MemberTypes.Constructor:
                        case MemberTypes.Method:
                            return (MethodBase)member;
                        default:
                            return null;
                    }
                default:
                    return null;
            }
        }

        public static SignatureHelper BuildMemberRefSignature(MethodBase methodBase)
        {
            return BuildMemberRefSignature(methodBase.CallingConvention,
                                           GetReturnType(methodBase),
                                           methodBase.GetParameters().Select(p => p.ParameterType).ToArray(),
                                           null);
        }

        private static SignatureHelper BuildMemberRefSignature(
            CallingConventions call,
            Type returnType,
            Type[] parameterTypes,
            Type[] optionalParameterTypes)
        {
            var sig = SignatureHelper.GetMethodSigHelper(call, returnType);
            if (parameterTypes != null)
            {
                foreach (var parameterType in parameterTypes)
                    sig.AddArgument(parameterType);
            }
            if (optionalParameterTypes != null && optionalParameterTypes.Length != 0)
            {
                // add the sentinel 
                sig.AddSentinel();
                foreach (var optionalParameterType in optionalParameterTypes)
                    sig.AddArgument(optionalParameterType);
            }
            return sig;
        }

        private static Type GetReturnType(MethodBase methodBase)
        {
            var methodInfo = methodBase as MethodInfo;
            if (methodInfo != null)
                return methodInfo.ReturnType;
            if (methodBase is ConstructorInfo)
                return typeof(void);
            throw new InvalidOperationException(string.Format("{0} is not supported", methodBase.GetType()));
        }
    }

    public static class Extensions
    {
        public static Type GetDelegateType(Type[] parameterTypes, Type returnType)
        {
            if(returnType == typeof(void))
            {
                switch(parameterTypes.Length)
                {
                case 0:
                    return typeof(Action);
                case 1:
                    return typeof(Action<>).MakeGenericType(parameterTypes);
                case 2:
                    return typeof(Action<,>).MakeGenericType(parameterTypes);
                case 3:
                    return typeof(Action<,,>).MakeGenericType(parameterTypes);
                case 4:
                    return typeof(Action<,,,>).MakeGenericType(parameterTypes);
                case 5:
                    return typeof(Action<,,,,>).MakeGenericType(parameterTypes);
                case 6:
                    return typeof(Action<,,,,,>).MakeGenericType(parameterTypes);
                default:
                    throw new NotSupportedException("Too many parameters for Action: " + parameterTypes.Length);
                }
            }
            parameterTypes = parameterTypes.Concat(new[] {returnType}).ToArray();
            switch(parameterTypes.Length)
            {
            case 1:
                return typeof(Func<>).MakeGenericType(parameterTypes);
            case 2:
                return typeof(Func<,>).MakeGenericType(parameterTypes);
            case 3:
                return typeof(Func<,,>).MakeGenericType(parameterTypes);
            case 4:
                return typeof(Func<,,,>).MakeGenericType(parameterTypes);
            case 5:
                return typeof(Func<,,,,>).MakeGenericType(parameterTypes);
            case 6:
                return typeof(Func<,,,,,>).MakeGenericType(parameterTypes);
            default:
                throw new NotSupportedException("Too many parameters for Func: " + parameterTypes.Length);
            }
        }
    }

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
            getMethodBaseFunctionAddress = typeof(Zzz).GetMethod("getMethodBase", BindingFlags.Public | BindingFlags.Static).MethodHandle.GetFunctionPointer();
            methodStartedAddress = typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MethodHandle.GetFunctionPointer();
            methodFinishedAddress = typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MethodHandle.GetFunctionPointer();

            //Console.ReadLine();
        }

        internal static ConcurrentDictionary<MethodBase, int> tracedMethods = new ConcurrentDictionary<MethodBase, int>();

        public static long TemplateForTicksSignature()
        {
            return 0L;
        }

        public static void HookCreateDelegate(MethodInfo createDelegateMethod)
        {
            RuntimeHelpers.PrepareMethod(createDelegateMethod.MethodHandle);
            Type[] parameterTypes;
            if(createDelegateMethod.IsStatic)
                parameterTypes = createDelegateMethod.GetParameters().Select(x => x.ParameterType).ToArray();
            else
                parameterTypes = new[] {createDelegateMethod.ReflectedType ?? createDelegateMethod.DeclaringType}.Concat(createDelegateMethod.GetParameters().Select(x => x.ParameterType)).ToArray();
            var dynamicMethod = new DynamicMethod(createDelegateMethod.Name + "_" + Guid.NewGuid(), createDelegateMethod.ReturnType, parameterTypes, typeof(DynamicMethod), true);

            var methodBody = MethodBody.Read(createDelegateMethod, true);

            sendToDebug("Plain", createDelegateMethod, methodBody);

            var traceMethod = typeof(DynamicMethodExtender).GetMethod("Trace", BindingFlags.Static | BindingFlags.Public);

            int startIndex = 0;

            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldarg_0));
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Call, traceMethod));

            methodBody.WriteToDynamicMethod(dynamicMethod, Math.Max(methodBody.MaxStack, 2));

            //var reflectionMethodBodyBuilder = new ReflectionMethodBodyBuilder(methodBody);

//            var methodBody2 = MethodBody.Build(methodBody.GetILAsByteArray(), stackSize, dynamicMethod.InitLocals, localSignature, methodBody.GetExceptionsAsByteArray());
//
//            sendToDebug("Changed", createDelegateMethod, methodBody2);

            createDelegateMethods.Add(dynamicMethod.CreateDelegate(Extensions.GetDelegateType(parameterTypes, createDelegateMethod.ReturnType)));

            if(!MethodUtil.HookMethod(dynamicMethod, createDelegateMethod))
                throw new InvalidOperationException("Unable to hook DynamicMethod.CreateDelegate");
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

            //TicksReader = (TicksReaderDelegate)Marshal.GetDelegateForFunctionPointer(ticksReaderAddress, typeof(TicksReaderDelegate));
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(long), Type.EmptyTypes, typeof(string), true);
            var il = method.GetILGenerator();
            if(IntPtr.Size == 8)
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I8, ticksReaderAddress.ToInt64());
            else
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, ticksReaderAddress.ToInt32());
            il.EmitCalli(System.Reflection.Emit.OpCodes.Calli, CallingConventions.Standard, typeof(long), Type.EmptyTypes, null);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            TicksReader = (Func<long>)method.CreateDelegate(typeof(Func<long>));
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

            MethodBody.Init();

            HookCreateDelegate(typeof(DynamicMethod).GetMethod("CreateDelegate", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Type), typeof(object) }, null));
            HookCreateDelegate(typeof(DynamicMethod).GetMethod("CreateDelegate", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Type) }, null));

            RuntimeHelpers.PrepareMethod(typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MethodHandle);
            RuntimeHelpers.PrepareMethod(typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MethodHandle);
        }

        [DllExport]
        public static SharpResponse Trace(
            [MarshalAs(UnmanagedType.LPWStr)] string assemblyName,
            [MarshalAs(UnmanagedType.LPWStr)] string moduleName,
            UIntPtr moduleId,
            uint methodToken,
            byte* rawMethodBody,
            [MarshalAs(UnmanagedType.FunctionPtr)] MethodBodyAllocator allocateForMethodBody)
        {
            SharpResponse response = new SharpResponse();

            Debug.WriteLine(".NET: assembly = {0}; module = {1}", assemblyName, moduleName);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);
            if(assembly == null)
            {
                Debug.WriteLine(".NET: Unable to obtain assembly with name {0}", assemblyName);
                return response;
            }

            var module = assembly.GetModules().FirstOrDefault(m => !m.Assembly.IsDynamic && m.FullyQualifiedName == moduleName);
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

            Debug.WriteLine(".NET: type = {0}, method = {1}", method.DeclaringType, method);

            if(method.GetCustomAttribute(typeof(DontTraceAttribute), false) != null)
            {
                Debug.WriteLine(method + " is marked with DontTraceAttribute and will not be traced");
                return response;
            }

            if(method.DeclaringType != null && method.DeclaringType.GetCustomAttribute(typeof(DontTraceAttribute), false) != null)
            {
                Debug.WriteLine(method + " declaring type is marked with DontTraceAttribute and will not be traced");
                return response;
            }

            //var output = method.IsConstructor && method.DeclaringType.FullName.Contains("JsonSerializer");
            //var output = method.IsConstructor && method.DeclaringType.FullName.Contains("SKBKontur.Catalogue.ClientLib.Sharding");
            var output = true;

            if (output) Debug.WriteLine(".NET: method {0} is asked to be traced", method);

            var methodBody = MethodBody.Read(rawMethodBody, module, new MetadataToken(methodToken), false);

            var rawSignature = methodBody.MethodSignature;
            var methodSignature = new SignatureReader(rawSignature).ReadAndParseMethodSignature();

            if (output) Debug.WriteLine(".NET: method's signature is: " + Convert.ToBase64String(rawSignature));
            if (output) Debug.WriteLine(".NET: method has {0} parameters", methodSignature.ParamCount);


            if (output) sendToDebug("Plain", method, methodBody);

            var methodContainsCycles = CycleFinderWithoutRecursion.HasCycle(methodBody.Instructions.ToArray());

            if (output) Debug.WriteLine("Contains cycles: " + methodContainsCycles + "\n");

            if (!methodContainsCycles && methodBody.Instructions.Count < 50)
            {
                Debug.WriteLine(method + " too simple to be traced");
                return response;
            }

            //if (method.Name == "Main" || method.Name == "add2" || method.Name == "twice")
            //    return response;

            int functionId;
            AddMethod(method, out functionId);

            List<Tuple<Instruction, int>> oldOffsets = new List<Tuple<Instruction, int>>();

            foreach(var instruction in methodBody.Instructions)
                oldOffsets.Add(Tuple.Create(instruction, instruction.Offset));

            int resultLocalIndex = -1;
            int ticksLocalIndex;
            byte[] newSignature;

            if(methodSignature.HasReturnType)
            {
                resultLocalIndex = methodBody.AddLocalVariable(methodSignature.ReturnTypeSignature).LocalIndex;
            }

            ticksLocalIndex = methodBody.AddLocalVariable(typeof(long)).LocalIndex;

            var endInstructionBeforeModifying = ReplaceRetInstructions(methodBody.Instructions, resultLocalIndex >= 0, resultLocalIndex);

            var ticksReaderSignature = typeof(Zzz).Module.ResolveSignature(typeof(Zzz).GetMethod("TemplateForTicksSignature", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var ticksReaderToken = signatureTokenBuilder(moduleId, ticksReaderSignature);

            var methodStartedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodStarted", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var methodStartedToken = signatureTokenBuilder(moduleId, methodStartedSignature);

            var methodFinishedSignature = typeof(TracingAnalyzer).Module.ResolveSignature(typeof(TracingAnalyzer).GetMethod("MethodFinished", BindingFlags.Public | BindingFlags.Static).MetadataToken);
            var methodFinishedToken = signatureTokenBuilder(moduleId, methodFinishedSignature);

            int startIndex = 0;

            if(method.IsConstructor)
            {
                // Skip call to ::base() or ::this()
                var declaringType = method.DeclaringType;
                if(declaringType != null)
                {
                    var baseType = declaringType.BaseType ?? typeof(object);
                    var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var constructors = new HashSet<int>(declaringType.GetConstructors(bindingFlags)
                        .Concat(baseType.GetConstructors(bindingFlags))
                        .Select(c => c.MetadataToken));
                    for(int i = 0; i < methodBody.Instructions.Count; ++i)
                    {
                        var instruction = methodBody.Instructions[i];
                        if(instruction.OpCode != OpCodes.Call) continue;
                        var token = (MetadataToken)instruction.Operand;
                        var m = MetadataExtensions.ResolveMethod(module, token);
                        if(constructors.Contains(m.MetadataToken))
                        {
                            startIndex = i + 1;
                            break;
                        }
                    }
                }
            }

            methodBody.Instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress));
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, ticksReaderToken));
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Stloc, ticksLocalIndex));

            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Ldc_I4, (int)functionId)); // [ ourMethod, functionId ]
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodStartedAddress : (long)methodStartedAddress)); // [ ourMethod, functionId, funcAddr ]
            methodBody.Instructions.Insert(startIndex++, Instruction.Create(OpCodes.Calli, methodStartedToken)); // []

            var tryStartInstruction = methodBody.Instructions[startIndex];

            Instruction tryEndInstruction;
            Instruction finallyStartInstruction;

            methodBody.Instructions.Insert(methodBody.Instructions.Count, finallyStartInstruction = tryEndInstruction = Instruction.Create(OpCodes.Ldc_I4, (int)functionId)); // [ functionId ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)ticksReaderAddress : (long)ticksReaderAddress)); // [ functionId, funcAddr ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Calli, ticksReaderToken)); // [ functionId, ticks ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Ldloc, ticksLocalIndex)); // [ functionId, ticks, startTicks ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Sub)); // [ functionId, elapsed ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(IntPtr.Size == 4 ? OpCodes.Ldc_I4 : OpCodes.Ldc_I8, IntPtr.Size == 4 ? (int)methodFinishedAddress : (long)methodFinishedAddress)); // [ functionId, elapsed, profilerOverhead , funcAddr ]
            methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Calli, methodFinishedToken)); // []

            Instruction endFinallyInstruction;
            methodBody.Instructions.Insert(methodBody.Instructions.Count, endFinallyInstruction = Instruction.Create(OpCodes.Endfinally));

            Instruction finallyEndInstruction;

            if(resultLocalIndex >= 0)
            {
                methodBody.Instructions.Insert(methodBody.Instructions.Count, finallyEndInstruction = Instruction.Create(OpCodes.Ldloc, resultLocalIndex));
                methodBody.Instructions.Insert(methodBody.Instructions.Count, Instruction.Create(OpCodes.Ret));
            }
            else
                methodBody.Instructions.Insert(methodBody.Instructions.Count, finallyEndInstruction = Instruction.Create(OpCodes.Ret));

            ExceptionHandler newException = new ExceptionHandler(ExceptionHandlerType.Finally);
            newException.TryStart = tryStartInstruction;
            newException.TryEnd = tryEndInstruction;
            newException.HandlerStart = finallyStartInstruction;
            newException.HandlerEnd = finallyEndInstruction;

            methodBody.Instructions.Insert(methodBody.Instructions.IndexOf(tryEndInstruction), Instruction.Create(OpCodes.Leave, finallyEndInstruction));

            foreach (var exceptionHandler in methodBody.ExceptionHandlers)
                if (exceptionHandler.HandlerEnd == null)
                    exceptionHandler.HandlerEnd = endInstructionBeforeModifying;

            methodBody.ExceptionHandlers.Add(newException);

            if (output) Debug.WriteLine("Initial maxStackSize = " + methodBody.MaxStack);
            if (output) Debug.WriteLine("");

            methodBody.Seal();
          
            var methodBytes = methodBody.GetFullMethodBody(sig => signatureTokenBuilder(moduleId, sig), Math.Max(methodBody.MaxStack, 4));

            if (output) sendToDebug("Changed", method, methodBody);

            if (output) Debug.WriteLine("Calculated maxStackSize = " + methodBody.MaxStack);
            if (output) Debug.WriteLine("");

            var newMethodBody = (IntPtr)allocateForMethodBody(moduleId, (uint)methodBytes.Length);
            Marshal.Copy(methodBytes, 0, newMethodBody, methodBytes.Length);

            response.newMethodBody = newMethodBody;

            var startMapEntries = allocateForMapEntries((UIntPtr)(oldOffsets.Count * Marshal.SizeOf(typeof(COR_IL_MAP))));

            var pointer = startMapEntries;
            foreach(var tuple in oldOffsets)
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

        public static void AddMethod(MethodBase method, out int functionId)
        {
            var index = Interlocked.Increment(ref numberOfMethods) - 1;
            functionId = index + 1;
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

        public static Instruction ReplaceRetInstructions(Collection<Instruction> instructions, bool hasReturnType, int resultLocalIndex = -1)
        {
            if(hasReturnType && resultLocalIndex == -1)
                throw new ArgumentException("hasReturnType = true, but resultLocalIndex = -1");

            var dummyInstr = Instruction.Create(OpCodes.Nop);
            instructions.Insert(instructions.Count, dummyInstr);
            int index = 0;
            while(index < instructions.Count)
            {
                var instruction = instructions[index];
                if (instruction.OpCode == OpCodes.Ret)
                {
                    // replace Ret with Nop
                    instructions[index].OpCode = OpCodes.Nop;
                    ++index;

                    if(hasReturnType)
                    {
                        instructions.Insert(index, Instruction.Create(OpCodes.Stloc, resultLocalIndex));
                        ++index;
                    }
                    instructions.Insert(index, Instruction.Create(OpCodes.Br, dummyInstr));
                }
                ++index;
            }
            return dummyInstr;
        }

        public static void sendToDebug(String label, MethodBase method, MethodBody methodBody)
        {
            Debug.WriteLine("");
            Debug.WriteLine(label + " " + method.DeclaringType + "." + method.Name);
            Debug.WriteLine(methodBody);
            Debug.WriteLine("");
        }

        public static object getMethodBase(int i, int j)
        {
            return methods[i][j];
        }

        public static MethodBase GetMethod(int id)
        {
            if(id == 0) return null;

            int index = id - 1;
            int adjustedIndex = index;

            int arrayIndex = GetArrayIndex(index + 1);
            if (arrayIndex > 0)
                adjustedIndex -= counts[arrayIndex - 1];

            return methods[arrayIndex][adjustedIndex];
        }

        private static readonly List<Delegate> createDelegateMethods = new List<Delegate>();

        public static IntPtr ticksReaderAddress;
        public static Func<long> TicksReader;
        public static IntPtr getMethodBaseFunctionAddress;
        public static IntPtr methodStartedAddress;
        public static IntPtr methodFinishedAddress;

        private static Func<UIntPtr, byte[], MetadataToken> signatureTokenBuilder;
        private static MapEntriesAllocator allocateForMapEntries;

        private static readonly MethodBase[][] methods = new MethodBase[32][];
        private static int numberOfMethods;

        private static readonly int[] sizes;
        private static readonly int[] counts;
    }
}