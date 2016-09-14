using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace GroboTrace.MethodBodyParsing
{
    internal class MethodBodyOnUnmanagedArray : MethodBody
    {
        public unsafe MethodBodyOnUnmanagedArray(byte* rawMethodBody, Module module, MetadataToken methodSignatureToken, bool resolveTokens)
            : base(GetMethodSignature(module, methodSignatureToken), resolveTokens)
        {
            if(rawMethodBody != null)
                new FullMethodBodyReader(rawMethodBody, module).Read(this);
        }

        private static byte[] GetMethodSignature(Module module, MetadataToken methodSignatureToken)
        {
            return module == null || methodSignatureToken == MetadataToken.Zero
                       ? new byte[0]
                       : module.ResolveSignature(methodSignatureToken.ToInt32());
        }
    }

    internal class MethodBodyOnMethodBase : MethodBody
    {
        public MethodBodyOnMethodBase(MethodBase method, bool resolveTokens)
            : base(GetMethodSignature(method), resolveTokens)
        {
            var methodBody = method.GetMethodBody();
            TemporaryMaxStack = methodBody.MaxStackSize;
            InitLocals = methodBody.InitLocals;

            var localSignature = methodBody.LocalSignatureMetadataToken != 0
                                     ? method.Module.ResolveSignature(methodBody.LocalSignatureMetadataToken)
                                     : SignatureHelper.GetLocalVarSigHelper().GetSignature(); // null is invalid value
            SetLocalSignature(localSignature);

            ILCodeReader.Read(methodBody.GetILAsByteArray(), this);

            ReadExceptions(methodBody.ExceptionHandlingClauses);
        }

        private static byte[] GetMethodSignature(MethodBase method)
        {
            return method.Module.ResolveSignature(method.MetadataToken);
        }

        private void ReadExceptions(IList<ExceptionHandlingClause> exceptionClauses)
        {
            foreach(var exceptionClause in exceptionClauses)
            {
                var handler = new ExceptionHandler((ExceptionHandlerType)exceptionClause.Flags);

                handler.TryStart = GetInstruction(exceptionClause.TryOffset);
                handler.TryEnd = GetInstruction(handler.TryStart.Offset + exceptionClause.TryLength);

                handler.HandlerStart = GetInstruction(exceptionClause.HandlerOffset);
                handler.HandlerEnd = GetInstruction(handler.HandlerStart.Offset + exceptionClause.HandlerLength);

                switch(handler.HandlerType)
                {
                case ExceptionHandlerType.Catch:
                    handler.CatchType = new MetadataToken((uint)exceptionClause.CatchType.MetadataToken);
                    break;
                case ExceptionHandlerType.Filter:
                    handler.FilterStart = GetInstruction(exceptionClause.FilterOffset);
                    break;
                }

                ExceptionHandlers.Add(handler);
            }
        }
    }

    internal class MethodBodyOnDynamicILInfo : MethodBody
    {
        public MethodBodyOnDynamicILInfo(DynamicMethod dynamicMethod, DynamicILInfo dynamicILInfo, bool resolveTokens)
            : base(GetMethodSignature(dynamicILInfo), resolveTokens)
        {
            using(var dynamicResolver = new DynamicResolver(dynamicMethod, dynamicILInfo))
            {
                int stackSize;
                int initLocals;
                int EHCount;

                var code = dynamicResolver.GetCodeInfo(out stackSize, out initLocals, out EHCount);

                TemporaryMaxStack = stackSize;
                InitLocals = initLocals != 0;

                SetLocalSignature(dynamicResolver.m_localSignature);

                ILCodeReader.Read(code, this);

                ExceptionsInfoReader.Read(dynamicResolver.GetRawEHInfo(), this);
            }
        }

        private static byte[] GetMethodSignature(DynamicILInfo dynamicILInfo)
        {
            var wrapper = new DynamicILInfoWrapper(dynamicILInfo);
            return (byte[])wrapper.m_scope[wrapper.m_methodSignature];
        }
    }

    internal class MethodBodyOnDynamicILGenerator : MethodBody
    {
        public MethodBodyOnDynamicILGenerator(DynamicMethod dynamicMethod, ILGenerator ilGenerator, bool resolveTokens)
            : base(GetMethodSignature(ilGenerator), resolveTokens)
        {
            using(var dynamicResolver = new DynamicResolver(dynamicMethod, ilGenerator))
            {
                int stackSize;
                int initLocals;
                int EHCount;

                var code = dynamicResolver.GetCodeInfo(out stackSize, out initLocals, out EHCount);

                TemporaryMaxStack = stackSize;
                InitLocals = initLocals != 0;

                SetLocalSignature(dynamicResolver.m_localSignature);

                ILCodeReader.Read(code, this);

                ReadExceptions(GetILGeneratorExceptions(dynamicResolver, EHCount));
            }
        }

        private static unsafe CORINFO_EH_CLAUSE[] GetILGeneratorExceptions(DynamicResolver dynamicResolver, int excCount)
        {
            var exceptions = new CORINFO_EH_CLAUSE[excCount];

            for(int i = 0; i < excCount; ++i)
            {
                fixed(CORINFO_EH_CLAUSE* pointer = &exceptions[i])
                    dynamicResolver.GetEHInfo(i, pointer);
            }

            return exceptions;
        }

        private void ReadExceptions(CORINFO_EH_CLAUSE[] exceptionClauses)
        {
            foreach(var exceptionClause in exceptionClauses)
            {
                var handler = new ExceptionHandler((ExceptionHandlerType)exceptionClause.Flags);

                handler.TryStart = GetInstruction(exceptionClause.TryOffset);
                handler.TryEnd = GetInstruction(handler.TryStart.Offset + exceptionClause.TryLength);

                handler.HandlerStart = GetInstruction(exceptionClause.HandlerOffset);
                handler.HandlerEnd = GetInstruction(handler.HandlerStart.Offset + exceptionClause.HandlerLength);

                switch(handler.HandlerType)
                {
                case ExceptionHandlerType.Catch:
                    handler.CatchType = new MetadataToken((uint)exceptionClause.ClassTokenOrFilterOffset);
                    break;
                case ExceptionHandlerType.Filter:
                    handler.FilterStart = GetInstruction(exceptionClause.ClassTokenOrFilterOffset);
                    break;
                }

                ExceptionHandlers.Add(handler);
            }
        }

        private static byte[] GetMethodSignature(ILGenerator ilGenerator)
        {
            var wrapper = new DynamicILGenerator(ilGenerator);
            return (byte[])wrapper.m_scope[wrapper.m_methodSigToken];
        }
    }

    internal class DynamicResolver : IDisposable
    {
        static DynamicResolver()
        {
            var assembly = typeof(DynamicMethod).Assembly;
            t_DynamicResolver = assembly.GetType("System.Reflection.Emit.DynamicResolver");
            t_DynamicILInfo = assembly.GetType("System.Reflection.Emit.DynamicILInfo");
            t_DynamicILGenerator = assembly.GetType("System.Reflection.Emit.DynamicILGenerator");
            var t_DynamicMethod = typeof(DynamicMethod);
            BuildFactoryByDynamicILInfo();
            BuildFactoryByDynamicILGenerator();
            BuildGetCodeInfoDelegate();
            BuildGetRawEHInfoDelegate();
            BuildGetEHInfoDelegate();
            m_methodSetter = FieldsExtractor.GetSetter(t_DynamicResolver.GetField("m_method", BindingFlags.Instance | BindingFlags.NonPublic));
            var m_resolverField = t_DynamicMethod.GetField("m_resolver", BindingFlags.Instance | BindingFlags.NonPublic);
            m_resolverSetter = FieldsExtractor.GetSetter<DynamicMethod, object>(m_resolverField);
            var m_localSignatureField = t_DynamicResolver.GetField("m_localSignature", BindingFlags.Instance | BindingFlags.NonPublic);
            m_localSignatureExtractor = FieldsExtractor.GetExtractor<object, byte[]>(m_localSignatureField);
        }

        public DynamicResolver(DynamicMethod dynamicMethod, DynamicILInfo dynamicILInfo)
        {
            this.dynamicMethod = dynamicMethod;
            inst = factoryByDynamicILInfo(dynamicILInfo);
        }

        public DynamicResolver(DynamicMethod dynamicMethod, ILGenerator ilGenerator)
        {
            this.dynamicMethod = dynamicMethod;
            inst = factoryByDynamicILGenerator(ilGenerator);
        }

        private static void BuildGetCodeInfoDelegate()
        {
            var parameterTypes = new[] {typeof(object), typeof(int).MakeByRefType(), typeof(int).MakeByRefType(), typeof(int).MakeByRefType()};
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(byte[]), parameterTypes, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicResolver);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_3);
            var getCodeInfoMethod = t_DynamicResolver.GetMethod("GetCodeInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            il.EmitCall(System.Reflection.Emit.OpCodes.Callvirt, getCodeInfoMethod, null);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            getCodeInfoDelegate = (GetCodeInfoDelegate)method.CreateDelegate(typeof(GetCodeInfoDelegate));
        }

        private static void BuildGetEHInfoDelegate()
        {
            var parameterTypes = new[] {typeof(object), typeof(int), typeof(void*)};
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(void), parameterTypes, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicResolver);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
            var getCodeInfoMethod = t_DynamicResolver.GetMethod("GetEHInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            il.EmitCall(System.Reflection.Emit.OpCodes.Callvirt, getCodeInfoMethod, null);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            getEHInfoDelegate = (GetEHInfoDelegate)method.CreateDelegate(typeof(GetEHInfoDelegate));
        }

        private static void BuildGetRawEHInfoDelegate()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(byte[]), new[] {typeof(object)}, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicResolver);
            var getRawEHInfoMethod = t_DynamicResolver.GetMethod("GetRawEHInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            il.EmitCall(System.Reflection.Emit.OpCodes.Callvirt, getRawEHInfoMethod, null);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            getRawEHInfoDelegate = (Func<object, byte[]>)method.CreateDelegate(typeof(Func<object, byte[]>));
        }

        private static void BuildFactoryByDynamicILInfo()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(object), new[] {t_DynamicILInfo}, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            var constructor = t_DynamicResolver.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {t_DynamicILInfo}, null);
            il.Emit(System.Reflection.Emit.OpCodes.Newobj, constructor);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            factoryByDynamicILInfo = (Func<DynamicILInfo, object>)method.CreateDelegate(typeof(Func<DynamicILInfo, object>));
        }

        private static void BuildFactoryByDynamicILGenerator()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(object), new[] {typeof(ILGenerator)}, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicILGenerator);
            var constructor = t_DynamicResolver.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {t_DynamicILGenerator}, null);
            il.Emit(System.Reflection.Emit.OpCodes.Newobj, constructor);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            factoryByDynamicILGenerator = (Func<ILGenerator, object>)method.CreateDelegate(typeof(Func<ILGenerator, object>));
        }

        private delegate byte[] GetCodeInfoDelegate(object inst, out int stackSize, out int initLocals, out int EHCount);

        private unsafe delegate void GetEHInfoDelegate(object inst, int excNumber, void* exc);

        public void Dispose()
        {
            m_methodSetter(inst, null);
            m_resolverSetter(dynamicMethod, null);
        }

        public byte[] GetCodeInfo(out int stackSize, out int initLocals, out int EHCount)
        {
            return getCodeInfoDelegate(inst, out stackSize, out initLocals, out EHCount);
        }

        public byte[] GetRawEHInfo()
        {
            return getRawEHInfoDelegate(inst);
        }

        public unsafe void GetEHInfo(int excNumber, void* exc)
        {
            getEHInfoDelegate(inst, excNumber, exc);
        }

        public byte[] m_localSignature { get { return m_localSignatureExtractor(inst); } }

        public static void Init()
        {
        }

        private readonly DynamicMethod dynamicMethod;
        private readonly object inst;

        private static Func<DynamicILInfo, object> factoryByDynamicILInfo;
        private static Func<ILGenerator, object> factoryByDynamicILGenerator;
        private static GetCodeInfoDelegate getCodeInfoDelegate;
        private static GetEHInfoDelegate getEHInfoDelegate;
        private static Func<object, byte[]> getRawEHInfoDelegate;
        private static readonly Action<object, object> m_methodSetter;
        private static readonly Type t_DynamicResolver;
        private static readonly Type t_DynamicILInfo;
        private static readonly Action<DynamicMethod, object> m_resolverSetter;
        private static readonly Func<object, byte[]> m_localSignatureExtractor;
        private static readonly Type t_DynamicILGenerator;
    }

    internal class DynamicILInfoWrapper
    {
        static DynamicILInfoWrapper()
        {
            var t_DynamicILInfo = typeof(DynamicILInfo);

            var m_methodSignatureField = t_DynamicILInfo.GetField("m_methodSignature", BindingFlags.Instance | BindingFlags.NonPublic);
            m_methodSignatureExtractor = FieldsExtractor.GetExtractor<DynamicILInfo, int>(m_methodSignatureField);

            var m_scopeField = t_DynamicILInfo.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic);
            m_scopeExtractor = FieldsExtractor.GetExtractor<DynamicILInfo, object>(m_scopeField);
        }

        public DynamicILInfoWrapper(DynamicILInfo inst)
        {
            this.inst = inst;
        }

        public int m_methodSignature { get { return m_methodSignatureExtractor(inst); } }
        public DynamicScope m_scope { get { return new DynamicScope(m_scopeExtractor(inst)); } }

        public static void Init()
        {
        }

        public DynamicILInfo inst;

        private static readonly Func<DynamicILInfo, int> m_methodSignatureExtractor;
        private static readonly Func<DynamicILInfo, object> m_scopeExtractor;
    }

    internal class DynamicILGenerator
    {
        static DynamicILGenerator()
        {
            var assembly = typeof(DynamicMethod).Assembly;
            var t_DynamicILGenerator = assembly.GetType("System.Reflection.Emit.DynamicILGenerator");

            var m_methodSigTokenField = t_DynamicILGenerator.GetField("m_methodSigToken", BindingFlags.Instance | BindingFlags.NonPublic);
            m_methodSigTokenExtractor = FieldsExtractor.GetExtractor<ILGenerator, int>(m_methodSigTokenField);

            var m_scopeField = t_DynamicILGenerator.GetField("m_scope", BindingFlags.Instance | BindingFlags.NonPublic);
            m_scopeExtractor = FieldsExtractor.GetExtractor<ILGenerator, object>(m_scopeField);
        }

        public DynamicILGenerator(ILGenerator inst)
        {
            this.inst = inst;
        }

        public int m_methodSigToken { get { return m_methodSigTokenExtractor(inst); } }
        public DynamicScope m_scope { get { return new DynamicScope(m_scopeExtractor(inst)); } }

        public static void Init()
        {
        }

        public ILGenerator inst;

        private static readonly Func<ILGenerator, int> m_methodSigTokenExtractor;
        private static readonly Func<ILGenerator, object> m_scopeExtractor;
    }

    internal class DynamicMethodWrapper
    {
        static DynamicMethodWrapper()
        {
            var t_DynamicMethod = typeof(DynamicMethod);

            var m_DynamicILInfoField = t_DynamicMethod.GetField("m_DynamicILInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            m_DynamicILInfoExtractor = FieldsExtractor.GetExtractor<DynamicMethod, DynamicILInfo>(m_DynamicILInfoField);

            var m_ilGeneratorField = t_DynamicMethod.GetField("m_ilGenerator", BindingFlags.Instance | BindingFlags.NonPublic);
            m_ilGeneratorExtractor = FieldsExtractor.GetExtractor<DynamicMethod, ILGenerator>(m_ilGeneratorField);
        }

        public DynamicMethodWrapper(DynamicMethod inst)
        {
            this.inst = inst;
        }

        public DynamicILInfo m_DynamicILInfo { get { return m_DynamicILInfoExtractor(inst); } }
        public ILGenerator m_ilGenerator { get { return m_ilGeneratorExtractor(inst); } }

        public static void Init()
        {
        }

        public DynamicMethod inst;

        private static readonly Func<DynamicMethod, DynamicILInfo> m_DynamicILInfoExtractor;
        private static readonly Func<DynamicMethod, ILGenerator> m_ilGeneratorExtractor;
    }

    internal class DynamicScope
    {
        static DynamicScope()
        {
            var assembly = typeof(DynamicMethod).Assembly;
            var t_DynamicScope = assembly.GetType("System.Reflection.Emit.DynamicScope");

            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(object), new[] {typeof(object), typeof(int)}, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0); // stack: [scope]
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicScope);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1); // stack: [scope, token]
            var getter = t_DynamicScope.GetProperty("Item", BindingFlags.Instance | BindingFlags.NonPublic).GetGetMethod(true);
            il.EmitCall(System.Reflection.Emit.OpCodes.Call, getter, null); // stack: [scope[this]]
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            itemGetter = (Func<object, int, object>)method.CreateDelegate(typeof(Func<object, int, object>));
        }

        public DynamicScope(object inst)
        {
            this.inst = inst;
        }

        public object this[int token] { get { return itemGetter(inst, token); } }

        public static void Init()
        {
        }

        private readonly object inst;

        private static readonly Func<object, int, object> itemGetter;
    }

    public abstract class MethodBody
    {
        protected MethodBody(byte[] methodSignature, bool resolveTokens)
        {
            this.resolveTokens = resolveTokens;
            Instructions = new InstructionCollection();
            ExceptionHandlers = new Collection<ExceptionHandler>();
            MethodSignature = methodSignature;
            InitLocals = true;
        }

        public static unsafe MethodBody Read(byte* rawMethodBody, Module module, MetadataToken methodSignatureToken, bool resolveTokens)
        {
            return new MethodBodyOnUnmanagedArray(rawMethodBody, module, methodSignatureToken, resolveTokens);
        }

        public static MethodBody Read(MethodBase method, bool resolveTokens)
        {
            return new MethodBodyOnMethodBase(method, resolveTokens);
        }

        public static unsafe MethodBody Read(DynamicMethod dynamicMethod, bool resolveTokens)
        {
            var wrapper = new DynamicMethodWrapper(dynamicMethod);
            var dynamicILInfo = wrapper.m_DynamicILInfo;
            if(dynamicILInfo != null)
                return new MethodBodyOnDynamicILInfo(dynamicMethod, dynamicILInfo, resolveTokens);
            var ilGenerator = wrapper.m_ilGenerator;
            if(ilGenerator != null)
                return new MethodBodyOnDynamicILGenerator(dynamicMethod, ilGenerator, resolveTokens);
            return new MethodBodyOnUnmanagedArray(null, null, MetadataToken.Zero, resolveTokens);
        }

        protected Instruction GetInstruction(int offset)
        {
            return Instructions.GetInstruction(offset);
        }

        public int TemporaryMaxStack { get; set; }

        public void TryCalculateMaxStackSize(Module module)
        {
            TemporaryMaxStack = new MaxStackSizeCalculator(this, module).TryComputeMaxStack();
        }

        public bool InitLocals { get; set; }

        public MetadataToken LocalVarToken { get; set; }

        public byte[] MethodSignature { get; }

        public bool HasExceptionHandlers { get { return !ExceptionHandlers.IsNullOrEmpty(); } }

        public void SetLocalSignature(byte[] localSignature)
        {
            localVarSigBuilder = new LocalVarSigBuilder(localSignature);
        }

        public LocalInfo AddLocalVariable(byte[] signature)
        {
            if(localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.AddLocalVariable(signature);
        }

        public LocalInfo AddLocalVariable(Type localType, bool isPinned = false)
        {
            if(localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.AddLocalVariable(localType, isPinned);
        }

        public byte[] GetLocalSignature()
        {
            if(localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.GetSignature();
        }

        public int LocalVariablesCount()
        {
            if(localVarSigBuilder == null)
                localVarSigBuilder = new LocalVarSigBuilder();
            return localVarSigBuilder.Count;
        }

        public void Seal()
        {
            Instructions.SimplifyMacros();
            Instructions.OptimizeMacros();

            isSealed = true;
        }

        public byte[] GetILAsByteArray()
        {
            if(!isSealed)
                throw new NotSupportedException("MethodBody has not been sealed");

            return new ILCodeBaker(Instructions).BakeILCode();
        }

        public byte[] GetExceptionsAsByteArray()
        {
            if(!isSealed)
                throw new NotSupportedException("MethodBody has not been sealed");

            return new ExceptionsBaker(ExceptionHandlers, Instructions).BakeExceptions();
        }

        public byte[] GetFullMethodBody(Module module, Func<byte[], MetadataToken> signatureTokenBuilder, int maxStackSize)
        {
            if(!isSealed)
                throw new NotSupportedException("MethodBody has not been sealed");

            return new MethodBodyBaker(module, signatureTokenBuilder, this, maxStackSize).BakeMethodBody();
        }

        public ILProcessor GetILProcessor()
        {
            return new ILProcessor(this);
        }

        public override string ToString()
        {
            var result = new StringBuilder();

            result.AppendLine("Instructions:");
            foreach(var instruction in Instructions)
                result.AppendLine(instruction.ToString());

            result.AppendLine();

            result.AppendLine("Exception handlers:");
            foreach(var exceptionHandler in ExceptionHandlers)
                result.AppendLine(exceptionHandler.ToString());

            return result.ToString();
        }

        public InstructionCollection Instructions { get; }
        public Collection<ExceptionHandler> ExceptionHandlers { get; }

        public static void Init()
        {
            DynamicMethodWrapper.Init();
            DynamicILGenerator.Init();
            DynamicILInfoWrapper.Init();
            DynamicScope.Init();
            DynamicResolver.Init();
        }

        private readonly bool resolveTokens;

        private bool isSealed;

        private LocalVarSigBuilder localVarSigBuilder;
    }
}