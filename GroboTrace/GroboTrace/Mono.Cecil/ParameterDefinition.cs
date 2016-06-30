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

namespace GroboTrace.Mono.Cecil
{

    public sealed class ParameterDefinition : ParameterReference
    {

        ushort attributes;

        internal IMethodSignature method;

        object constant = Mixin.NotResolved;

        public ParameterAttributes Attributes
        {
            get { return (ParameterAttributes)attributes; }
            set { attributes = (ushort)value; }
        }

        public IMethodSignature Method
        {
            get { return method; }
        }

        public int Sequence
        {
            get
            {
                if (method == null)
                    return -1;

                return method.HasImplicitThis() ? index + 1 : index;
            }
        }

        #region ParameterAttributes

        public bool IsIn
        {
            get { return attributes.GetAttributes((ushort)ParameterAttributes.In); }
            set { attributes = attributes.SetAttributes((ushort)ParameterAttributes.In, value); }
        }

        public bool IsOut
        {
            get { return attributes.GetAttributes((ushort)ParameterAttributes.Out); }
            set { attributes = attributes.SetAttributes((ushort)ParameterAttributes.Out, value); }
        }

        public bool IsLcid
        {
            get { return attributes.GetAttributes((ushort)ParameterAttributes.Lcid); }
            set { attributes = attributes.SetAttributes((ushort)ParameterAttributes.Lcid, value); }
        }

        public bool IsReturnValue
        {
            get { return attributes.GetAttributes((ushort)ParameterAttributes.Retval); }
            set { attributes = attributes.SetAttributes((ushort)ParameterAttributes.Retval, value); }
        }

        public bool IsOptional
        {
            get { return attributes.GetAttributes((ushort)ParameterAttributes.Optional); }
            set { attributes = attributes.SetAttributes((ushort)ParameterAttributes.Optional, value); }
        }

        public bool HasDefault
        {
            get { return attributes.GetAttributes((ushort)ParameterAttributes.HasDefault); }
            set { attributes = attributes.SetAttributes((ushort)ParameterAttributes.HasDefault, value); }
        }

        public bool HasFieldMarshal
        {
            get { return attributes.GetAttributes((ushort)ParameterAttributes.HasFieldMarshal); }
            set { attributes = attributes.SetAttributes((ushort)ParameterAttributes.HasFieldMarshal, value); }
        }

        #endregion

        internal ParameterDefinition(MetadataToken parameterType, IMethodSignature method)
            : this(string.Empty, ParameterAttributes.None, parameterType)
        {
            this.method = method;
        }

        public ParameterDefinition(MetadataToken parameterType)
            : this(string.Empty, ParameterAttributes.None, parameterType)
        {
        }

        public ParameterDefinition(string name, ParameterAttributes attributes, MetadataToken parameterType)
            : base(name, parameterType)
        {
            this.attributes = (ushort)attributes;
            this.token = new MetadataToken(TokenType.Param);
        }

        public override ParameterDefinition Resolve()
        {
            return this;
        }
    }
}