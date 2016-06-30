//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using System;
using System.Reflection;

using GroboTrace.Mono.Cecil.Metadata;
using GroboTrace.Mono.Cecil.PE;
using GroboTrace.Mono.Collections.Generic;

using RVA = System.UInt32;

namespace GroboTrace.Mono.Cecil.Cil {

    sealed class SignatureReader : ByteBuffer
    {
        readonly MetadataReader reader;
        readonly uint start, sig_length;

        TypeSystem TypeSystem
        {
            get { return reader.module.TypeSystem; }
        }

        public SignatureReader(uint blob, MetadataReader reader)
            : base(reader.buffer)
        {
            this.reader = reader;

            MoveToBlob(blob);

            this.sig_length = ReadCompressedUInt32();
            this.start = (uint)position;
        }

        void MoveToBlob(uint blob)
        {
            position = (int)(reader.image.BlobHeap.Offset + blob);
        }

        MetadataToken ReadTypeTokenSignature()
        {
            return CodedIndex.TypeDefOrRef.GetMetadataToken(ReadCompressedUInt32());
        }

        GenericParameter GetGenericParameter(GenericParameterType type, uint var)
        {
            var context = reader.context;
            int index = (int)var;

            if (context == null)
                return GetUnboundGenericParameter(type, index);

            IGenericParameterProvider provider;

            switch (type)
            {
                case GenericParameterType.Type:
                    provider = context.Type;
                    break;
                case GenericParameterType.Method:
                    provider = context.Method;
                    break;
                default:
                    throw new NotSupportedException();
            }

            if (!context.IsDefinition)
                CheckGenericContext(provider, index);

            if (index >= provider.GenericParameters.Count)
                return GetUnboundGenericParameter(type, index);

            return provider.GenericParameters[index];
        }

        GenericParameter GetUnboundGenericParameter(GenericParameterType type, int index)
        {
            return new GenericParameter(index, type, reader.module);
        }

        static void CheckGenericContext(IGenericParameterProvider owner, int index)
        {
            var owner_parameters = owner.GenericParameters;

            for (int i = owner_parameters.Count; i <= index; i++)
                owner_parameters.Add(new GenericParameter(owner));
        }

        public void ReadGenericInstanceSignature(IGenericParameterProvider provider, IGenericInstance instance)
        {
            var arity = ReadCompressedUInt32();

            if (!provider.IsDefinition)
                CheckGenericContext(provider, (int)arity - 1);

            var instance_arguments = instance.GenericArguments;

            for (int i = 0; i < arity; i++)
                instance_arguments.Add(ReadTypeSignature());
        }

        ArrayType ReadArrayTypeSignature()
        {
            var array = new ArrayType(ReadTypeSignature());

            var rank = ReadCompressedUInt32();

            var sizes = new uint[ReadCompressedUInt32()];
            for (int i = 0; i < sizes.Length; i++)
                sizes[i] = ReadCompressedUInt32();

            var low_bounds = new int[ReadCompressedUInt32()];
            for (int i = 0; i < low_bounds.Length; i++)
                low_bounds[i] = ReadCompressedInt32();

            array.Dimensions.Clear();

            for (int i = 0; i < rank; i++)
            {
                int? lower = null, upper = null;

                if (i < low_bounds.Length)
                    lower = low_bounds[i];

                if (i < sizes.Length)
                    upper = lower + (int)sizes[i] - 1;

                array.Dimensions.Add(new ArrayDimension(lower, upper));
            }

            return array;
        }

        TypeReference GetTypeDefOrRef(MetadataToken token)
        {
            return reader.GetTypeDefOrRef(token);
        }

        public TypeReference ReadTypeSignature()
        {
            return ReadTypeSignature((ElementType)ReadByte());
        }

        TypeReference ReadTypeSignature(ElementType etype)
        {
            switch (etype)
            {
                case ElementType.ValueType:
                    {
                        var value_type = GetTypeDefOrRef(ReadTypeTokenSignature());
                        value_type.IsValueType = true;
                        return value_type;
                    }
                case ElementType.Class:
                    return GetTypeDefOrRef(ReadTypeTokenSignature());
                case ElementType.Ptr:
                    return new PointerType(ReadTypeSignature());
                case ElementType.FnPtr:
                    {
                        var fptr = new FunctionPointerType();
                        ReadMethodSignature(fptr);
                        return fptr;
                    }
                case ElementType.ByRef:
                    return new ByReferenceType(ReadTypeSignature());
                case ElementType.Pinned:
                    return new PinnedType(ReadTypeSignature());
                case ElementType.SzArray:
                    return new ArrayType(ReadTypeSignature());
                case ElementType.Array:
                    return ReadArrayTypeSignature();
                case ElementType.CModOpt:
                    return new OptionalModifierType(
                        GetTypeDefOrRef(ReadTypeTokenSignature()), ReadTypeSignature());
                case ElementType.CModReqD:
                    return new RequiredModifierType(
                        GetTypeDefOrRef(ReadTypeTokenSignature()), ReadTypeSignature());
                case ElementType.Sentinel:
                    return new SentinelType(ReadTypeSignature());
                case ElementType.Var:
                    return GetGenericParameter(GenericParameterType.Type, ReadCompressedUInt32());
                case ElementType.MVar:
                    return GetGenericParameter(GenericParameterType.Method, ReadCompressedUInt32());
                case ElementType.GenericInst:
                    {
                        var is_value_type = ReadByte() == (byte)ElementType.ValueType;
                        var element_type = GetTypeDefOrRef(ReadTypeTokenSignature());
                        var generic_instance = new GenericInstanceType(element_type);

                        ReadGenericInstanceSignature(element_type, generic_instance);

                        if (is_value_type)
                        {
                            generic_instance.IsValueType = true;
                            element_type.GetElementType().IsValueType = true;
                        }

                        return generic_instance;
                    }
                case ElementType.Object: return TypeSystem.Object;
                case ElementType.Void: return TypeSystem.Void;
                case ElementType.TypedByRef: return TypeSystem.TypedReference;
                case ElementType.I: return TypeSystem.IntPtr;
                case ElementType.U: return TypeSystem.UIntPtr;
                default: return GetPrimitiveType(etype);
            }
        }

        public void ReadMethodSignature(IMethodSignature method)
        {
            var calling_convention = ReadByte();

            const byte has_this = 0x20;
            const byte explicit_this = 0x40;

            if ((calling_convention & has_this) != 0)
            {
                method.HasThis = true;
                calling_convention = (byte)(calling_convention & ~has_this);
            }

            if ((calling_convention & explicit_this) != 0)
            {
                method.ExplicitThis = true;
                calling_convention = (byte)(calling_convention & ~explicit_this);
            }

            method.CallingConvention = (MethodCallingConvention)calling_convention;

            var generic_context = method as MethodReference;
            if (generic_context != null && !generic_context.DeclaringType.IsArray)
                reader.context = generic_context;

            if ((calling_convention & 0x10) != 0)
            {
                var arity = ReadCompressedUInt32();

                if (generic_context != null && !generic_context.IsDefinition)
                    CheckGenericContext(generic_context, (int)arity - 1);
            }

            var param_count = ReadCompressedUInt32();

            method.MethodReturnType.ReturnType = ReadTypeSignature();

            if (param_count == 0)
                return;

            Collection<ParameterDefinition> parameters;

            var method_ref = method as MethodReference;
            if (method_ref != null)
                parameters = method_ref.parameters = new ParameterDefinitionCollection(method, (int)param_count);
            else
                parameters = method.Parameters;

            for (int i = 0; i < param_count; i++)
                parameters.Add(new ParameterDefinition(ReadTypeSignature()));
        }

        TypeReference GetPrimitiveType(ElementType etype)
        {
            switch (etype)
            {
                case ElementType.Boolean:
                    return TypeSystem.Boolean;
                case ElementType.Char:
                    return TypeSystem.Char;
                case ElementType.I1:
                    return TypeSystem.SByte;
                case ElementType.U1:
                    return TypeSystem.Byte;
                case ElementType.I2:
                    return TypeSystem.Int16;
                case ElementType.U2:
                    return TypeSystem.UInt16;
                case ElementType.I4:
                    return TypeSystem.Int32;
                case ElementType.U4:
                    return TypeSystem.UInt32;
                case ElementType.I8:
                    return TypeSystem.Int64;
                case ElementType.U8:
                    return TypeSystem.UInt64;
                case ElementType.R4:
                    return TypeSystem.Single;
                case ElementType.R8:
                    return TypeSystem.Double;
                case ElementType.String:
                    return TypeSystem.String;
                default:
                    throw new NotImplementedException(etype.ToString());
            }
        }
    }


	sealed unsafe class CodeReader : ByteBuffer {
	    private readonly Module module;
        readonly internal SignatureReader reader;

		int start;

		MethodDefinition method;
		MethodBody body;

		int Offset {
			get { return base.position - start; }
		}

        public CodeReader(byte* data, Module module, SignatureReader reader)
			: base (data)
		{
		    this.module = module;
		    this.reader = reader;
		}

	    public MethodBody ReadMethodBody (MethodDefinition method)
		{
			this.method = method;
			this.body = new MethodBody (method);

			ReadMethodBody ();

			return this.body;
		}

	    void ReadMethodBody ()
		{
	        position = 0;

	        var flags = ReadByte ();
			switch (flags & 0x3) {
			case 0x2: // tiny
				body.code_size = flags >> 2;
				body.MaxStackSize = 8;
				ReadCode ();
				break;
			case 0x3: // fat
				base.position--;
				ReadFatMethod ();
				break;
			default:
				throw new InvalidOperationException ();
			}
		}

		void ReadFatMethod ()
		{
			var flags = ReadUInt16 ();
			body.max_stack_size = ReadUInt16 ();
			body.code_size = (int) ReadUInt32 ();
			body.local_var_token = new MetadataToken (ReadUInt32 ());
			body.init_locals = (flags & 0x10) != 0;

			if (body.local_var_token.RID != 0)
				ReadVariables (body.local_var_token);

			ReadCode ();

			if ((flags & 0x8) != 0)
				ReadSection ();
		}

		public void ReadVariables (MetadataToken local_var_token)
		{
		    var signature = module.ResolveSignature(local_var_token.ToInt32());
		    reader.data = signature;

            const byte local_sig = 0x7;

            if (reader.ReadByte() != local_sig)
                throw new NotSupportedException();

            var count = reader.ReadCompressedUInt32();
		    body.variablesCount = count;
            if (count == 0)
                return;

		    body.variablesSignature = new byte[signature.Length - reader.position];
            Array.Copy(signature, reader.position, body.variablesSignature, 0, body.variablesSignature.Length);
		}

		void ReadCode ()
		{
			start = position;
			var code_size = body.code_size;

			if (code_size < 0 || buffer.Length <= (uint) (code_size + position))
				code_size = 0;

			var end = start + code_size;
			var instructions = body.instructions = new InstructionCollection ((code_size + 1) / 2);

			while (position < end) {
				var offset = base.position - start;
				var opcode = ReadOpCode ();
				var current = new Instruction (offset, opcode);

				if (opcode.OperandType != OperandType.InlineNone)
					current.operand = ReadOperand (current);

				instructions.Add (current);
			}

			ResolveBranches (instructions);
		}

		OpCode ReadOpCode ()
		{
			var il_opcode = ReadByte ();
			return il_opcode != 0xfe
				? OpCodes.OneByteOpCode [il_opcode]
				: OpCodes.TwoBytesOpCode [ReadByte ()];
		}

		object ReadOperand (Instruction instruction)
		{
			switch (instruction.opcode.OperandType) {
			case OperandType.InlineSwitch:
				var length = ReadInt32 ();
				var base_offset = Offset + (4 * length);
				var branches = new int [length];
				for (int i = 0; i < length; i++)
					branches [i] = base_offset + ReadInt32 ();
				return branches;
			case OperandType.ShortInlineBrTarget:
				return ReadSByte () + Offset;
			case OperandType.InlineBrTarget:
				return ReadInt32 () + Offset;
			case OperandType.ShortInlineI:
				if (instruction.opcode == OpCodes.Ldc_I4_S)
					return ReadSByte ();

				return ReadByte ();
			case OperandType.InlineI:
				return ReadInt32 ();
			case OperandType.ShortInlineR:
				return ReadSingle ();
			case OperandType.InlineR:
				return ReadDouble ();
			case OperandType.InlineI8:
				return ReadInt64 ();
			case OperandType.ShortInlineVar:
				return (int)ReadByte ();
			case OperandType.InlineVar:
				return (int)ReadUInt16 ();
			case OperandType.ShortInlineArg:
				return (int)ReadByte ();
			case OperandType.InlineArg:
				return (int)ReadUInt16 ();
			case OperandType.InlineSig:
				return ReadToken ();
			case OperandType.InlineString:
				return ReadToken ();
			case OperandType.InlineTok:
			case OperandType.InlineType:
			case OperandType.InlineMethod:
			case OperandType.InlineField:
				return ReadToken ();
			default:
				throw new NotSupportedException ();
			}
		}

	    void ResolveBranches (Collection<Instruction> instructions)
		{
			var items = instructions.items;
			var size = instructions.size;

			for (int i = 0; i < size; i++) {
				var instruction = items [i];
				switch (instruction.opcode.OperandType) {
				case OperandType.ShortInlineBrTarget:
				case OperandType.InlineBrTarget:
					instruction.operand = GetInstruction ((int) instruction.operand);
					break;
				case OperandType.InlineSwitch:
					var offsets = (int []) instruction.operand;
					var branches = new Instruction [offsets.Length];
					for (int j = 0; j < offsets.Length; j++)
						branches [j] = GetInstruction (offsets [j]);

					instruction.operand = branches;
					break;
				}
			}
		}

		Instruction GetInstruction (int offset)
		{
			return GetInstruction (body.Instructions, offset);
		}

		static Instruction GetInstruction (Collection<Instruction> instructions, int offset)
		{
			var size = instructions.size;
			var items = instructions.items;
			if (offset < 0 || offset > items [size - 1].offset)
				return null;

			int min = 0;
			int max = size - 1;
			while (min <= max) {
				int mid = min + ((max - min) / 2);
				var instruction = items [mid];
				var instruction_offset = instruction.offset;

				if (offset == instruction_offset)
					return instruction;

				if (offset < instruction_offset)
					max = mid - 1;
				else
					min = mid + 1;
			}

			return null;
		}

		void ReadSection ()
		{
			Align (4);

			const byte fat_format = 0x40;
			const byte more_sects = 0x80;

			var flags = ReadByte ();
			if ((flags & fat_format) == 0)
				ReadSmallSection ();
			else
				ReadFatSection ();

			if ((flags & more_sects) != 0)
				ReadSection ();
		}

		void ReadSmallSection ()
		{
			var count = ReadByte () / 12;
			Advance (2);

			ReadExceptionHandlers (
				count,
				() => (int) ReadUInt16 (),
				() => (int) ReadByte ());
		}

		void ReadFatSection ()
		{
			position--;
			var count = (ReadInt32 () >> 8) / 24;

			ReadExceptionHandlers (
				count,
				ReadInt32,
				ReadInt32);
		}

		// inline ?
		void ReadExceptionHandlers (int count, Func<int> read_entry, Func<int> read_length)
		{
			for (int i = 0; i < count; i++) {
				var handler = new ExceptionHandler (
					(ExceptionHandlerType) (read_entry () & 0x7));

				handler.TryStart = GetInstruction (read_entry ());
				handler.TryEnd = GetInstruction (handler.TryStart.Offset + read_length ());

				handler.HandlerStart = GetInstruction (read_entry ());
				handler.HandlerEnd = GetInstruction (handler.HandlerStart.Offset + read_length ());

				ReadExceptionHandlerSpecific (handler);

				this.body.ExceptionHandlers.Add (handler);
			}
		}

		void ReadExceptionHandlerSpecific (ExceptionHandler handler)
		{
			switch (handler.HandlerType) {
			case ExceptionHandlerType.Catch:
				handler.CatchType = ReadToken ();
				break;
			case ExceptionHandlerType.Filter:
				handler.FilterStart = GetInstruction (ReadInt32 ());
				break;
			default:
				Advance (4);
				break;
			}
		}

		void Align (int align)
		{
			align--;
			Advance (((position + align) & ~align) - position);
		}

		public MetadataToken ReadToken ()
		{
			return new MetadataToken (ReadUInt32 ());
		}
	}
}
