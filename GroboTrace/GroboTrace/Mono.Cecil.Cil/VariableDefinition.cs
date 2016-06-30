//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using GroboTrace.Mono.Cecil.Metadata;

namespace GroboTrace.Mono.Cecil.Cil {

	public sealed class VariableDefinition : VariableReference {

		public VariableDefinition (MetadataToken variableType)
			: base (variableType)
		{
		}

        public VariableDefinition(string name, MetadataToken variableType)
			: base (name, variableType)
		{
		}

		public override VariableDefinition Resolve ()
		{
			return this;
		}
	}
}
