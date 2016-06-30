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
using System.Collections.Generic;
using System.Text;

using GroboTrace.Mono.Collections.Generic;

using GroboTrace.Mono.Cecil.Metadata;
using GroboTrace.Mono.Cecil.PE;

using RVA = System.UInt32;

#if !READ_ONLY

namespace GroboTrace.Mono.Cecil.Cil {

    sealed class SignatureWriter : ByteBuffer
    {

        readonly MetadataBuilder metadata;

        public SignatureWriter(MetadataBuilder metadata)
            : base(6)
        {
            this.metadata = metadata;
        }

        public void WriteElementType(ElementType element_type)
        {
            WriteByte((byte)element_type);
        }

        public void WriteUTF8String(string @string)
        {
            if (@string == null)
            {
                WriteByte(0xff);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(@string);
            WriteCompressedUInt32((uint)bytes.Length);
            WriteBytes(bytes);
        }

        public void WriteMethodSignature(IMethodSignature method)
        {
            byte calling_convention = (byte)method.CallingConvention;
            if (method.HasThis)
                calling_convention |= 0x20;
            if (method.ExplicitThis)
                calling_convention |= 0x40;

            var generic_provider = method as IGenericParameterProvider;
            var generic_arity = generic_provider != null && generic_provider.HasGenericParameters
                ? generic_provider.GenericParameters.Count
                : 0;

            if (generic_arity > 0)
                calling_convention |= 0x10;

            var param_count = method.HasParameters ? method.Parameters.Count : 0;

            WriteByte(calling_convention);

            if (generic_arity > 0)
                WriteCompressedUInt32((uint)generic_arity);

            WriteCompressedUInt32((uint)param_count);
            WriteTypeSignature(method.ReturnType);

            if (param_count == 0)
                return;

            var parameters = method.Parameters;

            for (int i = 0; i < param_count; i++)
                WriteTypeSignature(parameters[i].ParameterType);
        }

        uint MakeTypeDefOrRefCodedRID(TypeReference type)
        {
            return CodedIndex.TypeDefOrRef.CompressMetadataToken(metadata.LookupToken(type));
        }

        public void WriteTypeSignature(TypeReference type)
        {
            if (type == null)
                throw new ArgumentNullException();

            var etype = type.etype;

            switch (etype)
            {
                case ElementType.MVar:
                case ElementType.Var:
                    {
                        var generic_parameter = (GenericParameter)type;

                        WriteElementType(etype);
                        var position = generic_parameter.Position;
                        if (position == -1)
                            throw new NotSupportedException();

                        WriteCompressedUInt32((uint)position);
                        break;
                    }

                case ElementType.GenericInst:
                    {
                        var generic_instance = (GenericInstanceType)type;
                        WriteElementType(ElementType.GenericInst);
                        WriteElementType(generic_instance.IsValueType ? ElementType.ValueType : ElementType.Class);
                        WriteCompressedUInt32(MakeTypeDefOrRefCodedRID(generic_instance.ElementType));

                        WriteGenericInstanceSignature(generic_instance);
                        break;
                    }

                case ElementType.Ptr:
                case ElementType.ByRef:
                case ElementType.Pinned:
                case ElementType.Sentinel:
                    {
                        var type_spec = (TypeSpecification)type;
                        WriteElementType(etype);
                        WriteTypeSignature(type_spec.ElementType);
                        break;
                    }

                case ElementType.FnPtr:
                    {
                        var fptr = (FunctionPointerType)type;
                        WriteElementType(ElementType.FnPtr);
                        WriteMethodSignature(fptr);
                        break;
                    }

                case ElementType.CModOpt:
                case ElementType.CModReqD:
                    {
                        var modifier = (IModifierType)type;
                        WriteModifierSignature(etype, modifier);
                        break;
                    }

                case ElementType.Array:
                    {
                        var array = (ArrayType)type;
                        if (!array.IsVector)
                        {
                            WriteArrayTypeSignature(array);
                            break;
                        }

                        WriteElementType(ElementType.SzArray);
                        WriteTypeSignature(array.ElementType);
                        break;
                    }

                case ElementType.None:
                    {
                        WriteElementType(type.IsValueType ? ElementType.ValueType : ElementType.Class);
                        WriteCompressedUInt32(MakeTypeDefOrRefCodedRID(type));
                        break;
                    }

                default:
                    if (!TryWriteElementType(type))
                        throw new NotSupportedException();

                    break;

            }
        }

        void WriteArrayTypeSignature(ArrayType array)
        {
            WriteElementType(ElementType.Array);
            WriteTypeSignature(array.ElementType);

            var dimensions = array.Dimensions;
            var rank = dimensions.Count;

            WriteCompressedUInt32((uint)rank);

            var sized = 0;
            var lbounds = 0;

            for (int i = 0; i < rank; i++)
            {
                var dimension = dimensions[i];

                if (dimension.UpperBound.HasValue)
                {
                    sized++;
                    lbounds++;
                }
                else if (dimension.LowerBound.HasValue)
                    lbounds++;
            }

            var sizes = new int[sized];
            var low_bounds = new int[lbounds];

            for (int i = 0; i < lbounds; i++)
            {
                var dimension = dimensions[i];
                low_bounds[i] = dimension.LowerBound.GetValueOrDefault();
                if (dimension.UpperBound.HasValue)
                    sizes[i] = dimension.UpperBound.Value - low_bounds[i] + 1;
            }

            WriteCompressedUInt32((uint)sized);
            for (int i = 0; i < sized; i++)
                WriteCompressedUInt32((uint)sizes[i]);

            WriteCompressedUInt32((uint)lbounds);
            for (int i = 0; i < lbounds; i++)
                WriteCompressedInt32(low_bounds[i]);
        }

        public void WriteGenericInstanceSignature(IGenericInstance instance)
        {
            var generic_arguments = instance.GenericArguments;
            var arity = generic_arguments.Count;

            WriteCompressedUInt32((uint)arity);
            for (int i = 0; i < arity; i++)
                WriteTypeSignature(generic_arguments[i]);
        }

        void WriteModifierSignature(ElementType element_type, IModifierType type)
        {
            WriteElementType(element_type);
            WriteCompressedUInt32(MakeTypeDefOrRefCodedRID(type.ModifierType));
            WriteTypeSignature(type.ElementType);
        }

        bool TryWriteElementType(TypeReference type)
        {
            var element = type.etype;

            if (element == ElementType.None)
                return false;

            WriteElementType(element);
            return true;
        }

        public void WriteConstantString(string value)
        {
            WriteBytes(Encoding.Unicode.GetBytes(value));
        }

        public void WriteConstantPrimitive(object value)
        {
            WritePrimitiveValue(value);
        }

        public void WriteCustomAttributeConstructorArguments(CustomAttribute attribute)
        {
            if (!attribute.HasConstructorArguments)
                return;

            var arguments = attribute.ConstructorArguments;
            var parameters = attribute.Constructor.Parameters;

            if (parameters.Count != arguments.Count)
                throw new InvalidOperationException();

            for (int i = 0; i < arguments.Count; i++)
                WriteCustomAttributeFixedArgument(parameters[i].ParameterType, arguments[i]);
        }

        void WriteCustomAttributeFixedArgument(TypeReference type, CustomAttributeArgument argument)
        {
            if (type.IsArray)
            {
                WriteCustomAttributeFixedArrayArgument((ArrayType)type, argument);
                return;
            }

            WriteCustomAttributeElement(type, argument);
        }

        void WriteCustomAttributeFixedArrayArgument(ArrayType type, CustomAttributeArgument argument)
        {
            var values = argument.Value as CustomAttributeArgument[];

            if (values == null)
            {
                WriteUInt32(0xffffffff);
                return;
            }

            WriteInt32(values.Length);

            if (values.Length == 0)
                return;

            var element_type = type.ElementType;

            for (int i = 0; i < values.Length; i++)
                WriteCustomAttributeElement(element_type, values[i]);
        }

        void WriteCustomAttributeElement(TypeReference type, CustomAttributeArgument argument)
        {
            if (type.IsArray)
            {
                WriteCustomAttributeFixedArrayArgument((ArrayType)type, argument);
                return;
            }

            if (type.etype == ElementType.Object)
            {
                argument = (CustomAttributeArgument)argument.Value;
                type = argument.Type;

                WriteCustomAttributeFieldOrPropType(type);
                WriteCustomAttributeElement(type, argument);
                return;
            }

            WriteCustomAttributeValue(type, argument.Value);
        }

        void WriteCustomAttributeValue(TypeReference type, object value)
        {
            var etype = type.etype;

            switch (etype)
            {
                case ElementType.String:
                    var @string = (string)value;
                    if (@string == null)
                        WriteByte(0xff);
                    else
                        WriteUTF8String(@string);
                    break;
                case ElementType.None:
                    if (type.IsTypeOf("System", "Type"))
                        WriteTypeReference((TypeReference)value);
                    else
                        WriteCustomAttributeEnumValue(type, value);
                    break;
                default:
                    WritePrimitiveValue(value);
                    break;
            }
        }

        void WritePrimitiveValue(object value)
        {
            if (value == null)
                throw new ArgumentNullException();

            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.Boolean:
                    WriteByte((byte)(((bool)value) ? 1 : 0));
                    break;
                case TypeCode.Byte:
                    WriteByte((byte)value);
                    break;
                case TypeCode.SByte:
                    WriteSByte((sbyte)value);
                    break;
                case TypeCode.Int16:
                    WriteInt16((short)value);
                    break;
                case TypeCode.UInt16:
                    WriteUInt16((ushort)value);
                    break;
                case TypeCode.Char:
                    WriteInt16((short)(char)value);
                    break;
                case TypeCode.Int32:
                    WriteInt32((int)value);
                    break;
                case TypeCode.UInt32:
                    WriteUInt32((uint)value);
                    break;
                case TypeCode.Single:
                    WriteSingle((float)value);
                    break;
                case TypeCode.Int64:
                    WriteInt64((long)value);
                    break;
                case TypeCode.UInt64:
                    WriteUInt64((ulong)value);
                    break;
                case TypeCode.Double:
                    WriteDouble((double)value);
                    break;
                default:
                    throw new NotSupportedException(value.GetType().FullName);
            }
        }

        void WriteCustomAttributeEnumValue(TypeReference enum_type, object value)
        {
            var type = enum_type.CheckedResolve();
            if (!type.IsEnum)
                throw new ArgumentException();

            WriteCustomAttributeValue(type.GetEnumUnderlyingType(), value);
        }

        void WriteCustomAttributeFieldOrPropType(TypeReference type)
        {
            if (type.IsArray)
            {
                var array = (ArrayType)type;
                WriteElementType(ElementType.SzArray);
                WriteCustomAttributeFieldOrPropType(array.ElementType);
                return;
            }

            var etype = type.etype;

            switch (etype)
            {
                case ElementType.Object:
                    WriteElementType(ElementType.Boxed);
                    return;
                case ElementType.None:
                    if (type.IsTypeOf("System", "Type"))
                        WriteElementType(ElementType.Type);
                    else
                    {
                        WriteElementType(ElementType.Enum);
                        WriteTypeReference(type);
                    }
                    return;
                default:
                    WriteElementType(etype);
                    return;
            }
        }

        public void WriteCustomAttributeNamedArguments(CustomAttribute attribute)
        {
            var count = GetNamedArgumentCount(attribute);

            WriteUInt16((ushort)count);

            if (count == 0)
                return;

            WriteICustomAttributeNamedArguments(attribute);
        }

        static int GetNamedArgumentCount(ICustomAttribute attribute)
        {
            int count = 0;

            if (attribute.HasFields)
                count += attribute.Fields.Count;

            if (attribute.HasProperties)
                count += attribute.Properties.Count;

            return count;
        }

        void WriteICustomAttributeNamedArguments(ICustomAttribute attribute)
        {
            if (attribute.HasFields)
                WriteCustomAttributeNamedArguments(0x53, attribute.Fields);

            if (attribute.HasProperties)
                WriteCustomAttributeNamedArguments(0x54, attribute.Properties);
        }

        void WriteCustomAttributeNamedArguments(byte kind, Collection<CustomAttributeNamedArgument> named_arguments)
        {
            for (int i = 0; i < named_arguments.Count; i++)
                WriteCustomAttributeNamedArgument(kind, named_arguments[i]);
        }

        void WriteCustomAttributeNamedArgument(byte kind, CustomAttributeNamedArgument named_argument)
        {
            var argument = named_argument.Argument;

            WriteByte(kind);
            WriteCustomAttributeFieldOrPropType(argument.Type);
            WriteUTF8String(named_argument.Name);
            WriteCustomAttributeFixedArgument(argument.Type, argument);
        }

        void WriteSecurityAttribute(SecurityAttribute attribute)
        {
            WriteTypeReference(attribute.AttributeType);

            var count = GetNamedArgumentCount(attribute);

            if (count == 0)
            {
                WriteCompressedUInt32(1); // length
                WriteCompressedUInt32(0); // count
                return;
            }

            var buffer = new SignatureWriter(metadata);
            buffer.WriteCompressedUInt32((uint)count);
            buffer.WriteICustomAttributeNamedArguments(attribute);

            WriteCompressedUInt32((uint)buffer.length);
            WriteBytes(buffer);
        }

        public void WriteSecurityDeclaration(SecurityDeclaration declaration)
        {
            WriteByte((byte)'.');

            var attributes = declaration.security_attributes;
            if (attributes == null)
                throw new NotSupportedException();

            WriteCompressedUInt32((uint)attributes.Count);

            for (int i = 0; i < attributes.Count; i++)
                WriteSecurityAttribute(attributes[i]);
        }

        public void WriteXmlSecurityDeclaration(SecurityDeclaration declaration)
        {
            var xml = GetXmlSecurityDeclaration(declaration);
            if (xml == null)
                throw new NotSupportedException();

            WriteBytes(Encoding.Unicode.GetBytes(xml));
        }

        static string GetXmlSecurityDeclaration(SecurityDeclaration declaration)
        {
            if (declaration.security_attributes == null || declaration.security_attributes.Count != 1)
                return null;

            var attribute = declaration.security_attributes[0];

            if (!attribute.AttributeType.IsTypeOf("System.Security.Permissions", "PermissionSetAttribute"))
                return null;

            if (attribute.properties == null || attribute.properties.Count != 1)
                return null;

            var property = attribute.properties[0];
            if (property.Name != "XML")
                return null;

            return (string)property.Argument.Value;
        }

        void WriteTypeReference(TypeReference type)
        {
            WriteUTF8String(TypeParser.ToParseable(type));
        }

        public void WriteMarshalInfo(MarshalInfo marshal_info)
        {
            WriteNativeType(marshal_info.native);

            switch (marshal_info.native)
            {
                case NativeType.Array:
                    {
                        var array = (ArrayMarshalInfo)marshal_info;
                        if (array.element_type != NativeType.None)
                            WriteNativeType(array.element_type);
                        if (array.size_parameter_index > -1)
                            WriteCompressedUInt32((uint)array.size_parameter_index);
                        if (array.size > -1)
                            WriteCompressedUInt32((uint)array.size);
                        if (array.size_parameter_multiplier > -1)
                            WriteCompressedUInt32((uint)array.size_parameter_multiplier);
                        return;
                    }
                case NativeType.SafeArray:
                    {
                        var array = (SafeArrayMarshalInfo)marshal_info;
                        if (array.element_type != VariantType.None)
                            WriteVariantType(array.element_type);
                        return;
                    }
                case NativeType.FixedArray:
                    {
                        var array = (FixedArrayMarshalInfo)marshal_info;
                        if (array.size > -1)
                            WriteCompressedUInt32((uint)array.size);
                        if (array.element_type != NativeType.None)
                            WriteNativeType(array.element_type);
                        return;
                    }
                case NativeType.FixedSysString:
                    var sys_string = (FixedSysStringMarshalInfo)marshal_info;
                    if (sys_string.size > -1)
                        WriteCompressedUInt32((uint)sys_string.size);
                    return;
                case NativeType.CustomMarshaler:
                    var marshaler = (CustomMarshalInfo)marshal_info;
                    WriteUTF8String(marshaler.guid != Guid.Empty ? marshaler.guid.ToString() : string.Empty);
                    WriteUTF8String(marshaler.unmanaged_type);
                    WriteTypeReference(marshaler.managed_type);
                    WriteUTF8String(marshaler.cookie);
                    return;
            }
        }

        void WriteNativeType(NativeType native)
        {
            WriteByte((byte)native);
        }

        void WriteVariantType(VariantType variant)
        {
            WriteByte((byte)variant);
        }
    }


	sealed class CodeWriter : ByteBuffer {

		RVA current;
		MethodBody body;

		public CodeWriter ()
			: base (0)
		{
			this.current = 0;
		}

		public RVA WriteMethodBody (MethodBody body)
		{
			var rva = BeginMethod ();

            WriteResolvedMethodBody(body);

			Align (4);

			EndMethod ();
			return rva;
		}

        void WriteResolvedMethodBody(MethodBody body)
		{
			this.body = body;
			ComputeHeader ();
			if (RequiresFatHeader ())
				WriteFatHeader ();
			else
				WriteByte ((byte) (0x2 | (body.CodeSize << 2))); // tiny

			WriteInstructions ();

			if (body.HasExceptionHandlers)
				WriteExceptionHandlers ();
		}

		void WriteFatHeader ()
		{
			var body = this.body;
			byte flags = 0x3;	// fat
			if (body.InitLocals)
				flags |= 0x10;	// init locals
			if (body.HasExceptionHandlers)
				flags |= 0x8;	// more sections

			WriteByte (flags);
			WriteByte (0x30);
			WriteInt16 ((short) body.max_stack_size);
			WriteInt32 (body.code_size);
            body.local_var_token = body.HasVariables
                ? GetVariablesSignature(body.Variables)
                : MetadataToken.Zero;
			WriteMetadataToken (body.local_var_token);
		}

        MetadataToken GetVariablesSignature(Collection<VariableDefinition> variables)
        {
            var signature = new SignatureWriter(this);
            signature.WriteByte(0x7);
            signature.WriteCompressedUInt32((uint)variables.Count);
            for (int i = 0; i < variables.Count; i++)
                signature.WriteTypeSignature(variables[i].VariableType);
            return signature;
        }

		void WriteInstructions ()
		{
			var instructions = body.Instructions;
			var items = instructions.items;
			var size = instructions.size;

			for (int i = 0; i < size; i++) {
				var instruction = items [i];
				WriteOpCode (instruction.opcode);
				WriteOperand (instruction);
			}
		}

		void WriteOpCode (OpCode opcode)
		{
			if (opcode.Size == 1) {
				WriteByte (opcode.Op2);
			} else {
				WriteByte (opcode.Op1);
				WriteByte (opcode.Op2);
			}
		}

		void WriteOperand (Instruction instruction)
		{
			var opcode = instruction.opcode;
			var operand_type = opcode.OperandType;
			if (operand_type == OperandType.InlineNone)
				return;

			var operand = instruction.operand;
			if (operand == null)
				throw new ArgumentException ();

			switch (operand_type) {
			case OperandType.InlineSwitch: {
				var targets = (Instruction []) operand;
				WriteInt32 (targets.Length);
				var diff = instruction.Offset + opcode.Size + (4 * (targets.Length + 1));
				for (int i = 0; i < targets.Length; i++)
					WriteInt32 (GetTargetOffset (targets [i]) - diff);
				break;
			}
			case OperandType.ShortInlineBrTarget: {
				var target = (Instruction) operand;
				WriteSByte ((sbyte) (GetTargetOffset (target) - (instruction.Offset + opcode.Size + 1)));
				break;
			}
			case OperandType.InlineBrTarget: {
				var target = (Instruction) operand;
				WriteInt32 (GetTargetOffset (target) - (instruction.Offset + opcode.Size + 4));
				break;
			}
			case OperandType.ShortInlineVar:
				WriteByte ((byte) (int) operand);
				break;
			case OperandType.ShortInlineArg:
				WriteByte ((byte) (int) operand);
				break;
			case OperandType.InlineVar:
				WriteInt16 ((short) (int) operand);
				break;
			case OperandType.InlineArg:
				WriteInt16 ((short) (int) operand);
				break;
			case OperandType.InlineSig:
				WriteMetadataToken ((MetadataToken)operand);
				break;
			case OperandType.ShortInlineI:
				if (opcode == OpCodes.Ldc_I4_S)
					WriteSByte ((sbyte) operand);
				else
					WriteByte ((byte) operand);
				break;
			case OperandType.InlineI:
				WriteInt32 ((int) operand);
				break;
			case OperandType.InlineI8:
				WriteInt64 ((long) operand);
				break;
			case OperandType.ShortInlineR:
				WriteSingle ((float) operand);
				break;
			case OperandType.InlineR:
				WriteDouble ((double) operand);
				break;
			case OperandType.InlineString:
				WriteMetadataToken ((MetadataToken)operand);
				break;
			case OperandType.InlineType:
			case OperandType.InlineField:
			case OperandType.InlineMethod:
			case OperandType.InlineTok:
                WriteMetadataToken((MetadataToken)operand);
				break;
			default:
				throw new ArgumentException ();
			}
		}

		int GetTargetOffset (Instruction instruction)
		{
			if (instruction == null) {
				var last = body.instructions [body.instructions.size - 1];
				return last.offset + last.GetSize ();
			}

			return instruction.offset;
		}

	    bool RequiresFatHeader ()
		{
			var body = this.body;
			return body.CodeSize >= 64
				|| body.InitLocals
				|| body.HasVariables
				|| body.HasExceptionHandlers
				|| body.MaxStackSize > 8;
		}

		void ComputeHeader ()
		{
			int offset = 0;
			var instructions = body.instructions;
			var items = instructions.items;
			var count = instructions.size;
			var stack_size = 0;
			var max_stack = 0;
			Dictionary<Instruction, int> stack_sizes = null;

			if (body.HasExceptionHandlers)
				ComputeExceptionHandlerStackSize (ref stack_sizes);

			for (int i = 0; i < count; i++) {
				var instruction = items [i];
				instruction.offset = offset;
				offset += instruction.GetSize ();

				ComputeStackSize (instruction, ref stack_sizes, ref stack_size, ref max_stack);
			}

			body.code_size = offset;
			body.max_stack_size = max_stack;
		}

		void ComputeExceptionHandlerStackSize (ref Dictionary<Instruction, int> stack_sizes)
		{
			var exception_handlers = body.ExceptionHandlers;

			for (int i = 0; i < exception_handlers.Count; i++) {
				var exception_handler = exception_handlers [i];

				switch (exception_handler.HandlerType) {
				case ExceptionHandlerType.Catch:
					AddExceptionStackSize (exception_handler.HandlerStart, ref stack_sizes);
					break;
				case ExceptionHandlerType.Filter:
					AddExceptionStackSize (exception_handler.FilterStart, ref stack_sizes);
					AddExceptionStackSize (exception_handler.HandlerStart, ref stack_sizes);
					break;
				}
			}
		}

		static void AddExceptionStackSize (Instruction handler_start, ref Dictionary<Instruction, int> stack_sizes)
		{
			if (handler_start == null)
				return;

			if (stack_sizes == null)
				stack_sizes = new Dictionary<Instruction, int> ();

			stack_sizes [handler_start] = 1;
		}

		static void ComputeStackSize (Instruction instruction, ref Dictionary<Instruction, int> stack_sizes, ref int stack_size, ref int max_stack)
		{
			int computed_size;
			if (stack_sizes != null && stack_sizes.TryGetValue (instruction, out computed_size))
				stack_size = computed_size;

			max_stack = System.Math.Max (max_stack, stack_size);
			ComputeStackDelta (instruction, ref stack_size);
			max_stack = System.Math.Max (max_stack, stack_size);

			CopyBranchStackSize (instruction, ref stack_sizes, stack_size);
			ComputeStackSize (instruction, ref stack_size);
		}

		static void CopyBranchStackSize (Instruction instruction, ref Dictionary<Instruction, int> stack_sizes, int stack_size)
		{
			if (stack_size == 0)
				return;

			switch (instruction.opcode.OperandType) {
			case OperandType.ShortInlineBrTarget:
			case OperandType.InlineBrTarget:
				CopyBranchStackSize (ref stack_sizes, (Instruction) instruction.operand, stack_size);
				break;
			case OperandType.InlineSwitch:
				var targets = (Instruction[]) instruction.operand;
				for (int i = 0; i < targets.Length; i++)
					CopyBranchStackSize (ref stack_sizes, targets [i], stack_size);
				break;
			}
		}

		static void CopyBranchStackSize (ref Dictionary<Instruction, int> stack_sizes, Instruction target, int stack_size)
		{
			if (stack_sizes == null)
				stack_sizes = new Dictionary<Instruction, int> ();

			int branch_stack_size = stack_size;

			int computed_size;
			if (stack_sizes.TryGetValue (target, out computed_size))
				branch_stack_size = System.Math.Max (branch_stack_size, computed_size);

			stack_sizes [target] = branch_stack_size;
		}

		static void ComputeStackSize (Instruction instruction, ref int stack_size)
		{
			switch (instruction.opcode.FlowControl) {
			case FlowControl.Branch:
			case FlowControl.Break:
			case FlowControl.Throw:
			case FlowControl.Return:
				stack_size = 0;
				break;
			}
		}

		static void ComputeStackDelta (Instruction instruction, ref int stack_size)
		{
			switch (instruction.opcode.FlowControl) {
			case FlowControl.Call: {
				var method = (IMethodSignature) instruction.operand;
				// pop 'this' argument
				if (method.HasImplicitThis() && instruction.opcode.Code != Code.Newobj)
					stack_size--;
				// pop normal arguments
				if (method.HasParameters)
					stack_size -= method.Parameters.Count;
				// pop function pointer
				if (instruction.opcode.Code == Code.Calli)
					stack_size--;
				// push return value
				if (method.ReturnType.etype != ElementType.Void || instruction.opcode.Code == Code.Newobj)
					stack_size++;
				break;
			}
			default:
				ComputePopDelta (instruction.opcode.StackBehaviourPop, ref stack_size);
				ComputePushDelta (instruction.opcode.StackBehaviourPush, ref stack_size);
				break;
			}
		}

		static void ComputePopDelta (StackBehaviour pop_behavior, ref int stack_size)
		{
			switch (pop_behavior) {
			case StackBehaviour.Popi:
			case StackBehaviour.Popref:
			case StackBehaviour.Pop1:
				stack_size--;
				break;
			case StackBehaviour.Pop1_pop1:
			case StackBehaviour.Popi_pop1:
			case StackBehaviour.Popi_popi:
			case StackBehaviour.Popi_popi8:
			case StackBehaviour.Popi_popr4:
			case StackBehaviour.Popi_popr8:
			case StackBehaviour.Popref_pop1:
			case StackBehaviour.Popref_popi:
				stack_size -= 2;
				break;
			case StackBehaviour.Popi_popi_popi:
			case StackBehaviour.Popref_popi_popi:
			case StackBehaviour.Popref_popi_popi8:
			case StackBehaviour.Popref_popi_popr4:
			case StackBehaviour.Popref_popi_popr8:
			case StackBehaviour.Popref_popi_popref:
				stack_size -= 3;
				break;
			case StackBehaviour.PopAll:
				stack_size = 0;
				break;
			}
		}

		static void ComputePushDelta (StackBehaviour push_behaviour, ref int stack_size)
		{
			switch (push_behaviour) {
			case StackBehaviour.Push1:
			case StackBehaviour.Pushi:
			case StackBehaviour.Pushi8:
			case StackBehaviour.Pushr4:
			case StackBehaviour.Pushr8:
			case StackBehaviour.Pushref:
				stack_size++;
				break;
			case StackBehaviour.Push1_push1:
				stack_size += 2;
				break;
			}
		}

		void WriteExceptionHandlers ()
		{
			Align (4);

			var handlers = body.ExceptionHandlers;

			if (handlers.Count < 0x15 && !RequiresFatSection (handlers))
				WriteSmallSection (handlers);
			else
				WriteFatSection (handlers);
		}

		static bool RequiresFatSection (Collection<ExceptionHandler> handlers)
		{
			for (int i = 0; i < handlers.Count; i++) {
				var handler = handlers [i];

				if (IsFatRange (handler.TryStart, handler.TryEnd))
					return true;

				if (IsFatRange (handler.HandlerStart, handler.HandlerEnd))
					return true;

				if (handler.HandlerType == ExceptionHandlerType.Filter
					&& IsFatRange (handler.FilterStart, handler.HandlerStart))
					return true;
			}

			return false;
		}

		static bool IsFatRange (Instruction start, Instruction end)
		{
			if (start == null)
				throw new ArgumentException ();

			if (end == null)
				return true;

			return end.Offset - start.Offset > 255 || start.Offset > 65535;
		}

		void WriteSmallSection (Collection<ExceptionHandler> handlers)
		{
			const byte eh_table = 0x1;

			WriteByte (eh_table);
			WriteByte ((byte) (handlers.Count * 12 + 4));
			WriteBytes (2);

			WriteExceptionHandlers (
				handlers,
				i => WriteUInt16 ((ushort) i),
				i => WriteByte ((byte) i));
		}

		void WriteFatSection (Collection<ExceptionHandler> handlers)
		{
			const byte eh_table = 0x1;
			const byte fat_format = 0x40;

			WriteByte (eh_table | fat_format);

			int size = handlers.Count * 24 + 4;
			WriteByte ((byte) (size & 0xff));
			WriteByte ((byte) ((size >> 8) & 0xff));
			WriteByte ((byte) ((size >> 16) & 0xff));

			WriteExceptionHandlers (handlers, WriteInt32, WriteInt32);
		}

		void WriteExceptionHandlers (Collection<ExceptionHandler> handlers, Action<int> write_entry, Action<int> write_length)
		{
			for (int i = 0; i < handlers.Count; i++) {
				var handler = handlers [i];

				write_entry ((int) handler.HandlerType);

				write_entry (handler.TryStart.Offset);
				write_length (GetTargetOffset (handler.TryEnd) - handler.TryStart.Offset);

				write_entry (handler.HandlerStart.Offset);
				write_length (GetTargetOffset (handler.HandlerEnd) - handler.HandlerStart.Offset);

				WriteExceptionHandlerSpecific (handler);
			}
		}

		void WriteExceptionHandlerSpecific (ExceptionHandler handler)
		{
			switch (handler.HandlerType) {
			case ExceptionHandlerType.Catch:
				WriteMetadataToken (handler.CatchType);
				break;
			case ExceptionHandlerType.Filter:
				WriteInt32 (handler.FilterStart.Offset);
				break;
			default:
				WriteInt32 (0);
				break;
			}
		}

		RVA BeginMethod ()
		{
			return current;
		}

		void WriteMetadataToken (MetadataToken token)
		{
			WriteUInt32 (token.ToUInt32 ());
		}

		void Align (int align)
		{
			align--;
			WriteBytes (((position + align) & ~align) - position);
		}

		void EndMethod ()
		{
			current = (RVA) (0 + position);
		}
	}
}

#endif
