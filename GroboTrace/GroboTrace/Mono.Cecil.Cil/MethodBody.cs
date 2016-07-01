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
using System.Threading;

using GroboTrace.Mono.Cecil.Metadata;
using GroboTrace.Mono.Collections.Generic;

namespace GroboTrace.Mono.Cecil.Cil {

	public sealed class MethodBody {

		internal int max_stack_size;
		internal int code_size;
		internal bool init_locals;
		internal MetadataToken local_var_token;

		internal Collection<Instruction> instructions;
		internal Collection<ExceptionHandler> exceptions;
		internal byte[] variablesSignature;
	    internal uint variablesCount;

		public int MaxStackSize {
			get { return max_stack_size; }
			set { max_stack_size = value; }
		}

		public int CodeSize {
			get { return code_size; }
		}

		public bool InitLocals {
			get { return init_locals; }
			set { init_locals = value; }
		}

		public MetadataToken LocalVarToken {
			get { return local_var_token; }
			set { local_var_token = value; }
		}

		public Collection<Instruction> Instructions {
			get { return instructions ?? (instructions = new InstructionCollection ()); }
		}

		public bool HasExceptionHandlers {
			get { return !exceptions.IsNullOrEmpty (); }
		}

		public Collection<ExceptionHandler> ExceptionHandlers {
			get { return exceptions ?? (exceptions = new Collection<ExceptionHandler> ()); }
		}

		public bool HasVariables {
			get { return variablesCount > 0; }
		}

        public byte[] VariablesSignature
        {
			get { return variablesSignature; }
		}

	    public ILProcessor GetILProcessor ()
		{
			return new ILProcessor (this);
		}
	}

	public interface IVariableDefinitionProvider {
		bool HasVariables { get; }
		Collection<VariableDefinition> Variables { get; }
	}

	class VariableDefinitionCollection : Collection<VariableDefinition> {

		internal VariableDefinitionCollection ()
		{
		}

		internal VariableDefinitionCollection (int capacity)
			: base (capacity)
		{
		}

		protected override void OnAdd (VariableDefinition item, int index)
		{
			item.index = index;
		}

		protected override void OnInsert (VariableDefinition item, int index)
		{
			item.index = index;

			for (int i = index; i < size; i++)
				items [i].index = i + 1;
		}

		protected override void OnSet (VariableDefinition item, int index)
		{
			item.index = index;
		}

		protected override void OnRemove (VariableDefinition item, int index)
		{
			item.index = -1;

			for (int i = index + 1; i < size; i++)
				items [i].index = i - 1;
		}
	}

	class InstructionCollection : Collection<Instruction> {

		internal InstructionCollection ()
		{
		}

		internal InstructionCollection (int capacity)
			: base (capacity)
		{
		}

		protected override void OnAdd (Instruction item, int index)
		{
			if (index == 0)
				return;

			var previous = items [index - 1];
			previous.next = item;
			item.previous = previous;
		}

		protected override void OnInsert (Instruction item, int index)
		{
			if (size == 0)
				return;

			var current = items [index];
			if (current == null) {
				var last = items [index - 1];
				last.next = item;
				item.previous = last;
				return;
			}

			var previous = current.previous;
			if (previous != null) {
				previous.next = item;
				item.previous = previous;
			}

			current.previous = item;
			item.next = current;
		}

		protected override void OnSet (Instruction item, int index)
		{
			var current = items [index];

			item.previous = current.previous;
			item.next = current.next;

			current.previous = null;
			current.next = null;
		}

		protected override void OnRemove (Instruction item, int index)
		{
			var previous = item.previous;
			if (previous != null)
				previous.next = item.next;

			var next = item.next;
			if (next != null)
				next.previous = item.previous;

			item.previous = null;
			item.next = null;
		}
	}
}
