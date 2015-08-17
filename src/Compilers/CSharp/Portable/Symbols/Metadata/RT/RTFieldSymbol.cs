// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Threading;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    using TypeInfo = System.Reflection.TypeInfo;

    internal sealed class RTFieldSymbol : MetadataFieldSymbol
    {
        internal readonly FieldInfo RuntimeField;

        public RTFieldSymbol(PENamedTypeSymbol containingType, FieldInfo handle)
            : base(containingType)
        {
            Debug.Assert(handle != null);
            RuntimeField = handle;
        }

        public override string Name => RuntimeField.Name;
        internal override bool HasSpecialName => (RuntimeField.Attributes & FieldAttributes.SpecialName) != 0;
        internal override bool HasRuntimeSpecialName => (RuntimeField.Attributes & FieldAttributes.RTSpecialName) != 0;
        internal override bool IsNotSerialized => (RuntimeField.Attributes & FieldAttributes.NotSerialized) != 0;
        internal override bool IsMarshalledExplicitly => ((RuntimeField.Attributes & FieldAttributes.HasFieldMarshal) != 0);
        public override bool IsReadOnly => (RuntimeField.Attributes & FieldAttributes.InitOnly) != 0;
        public override bool IsStatic => (RuntimeField.Attributes & FieldAttributes.Static) != 0;
        public override Accessibility DeclaredAccessibility => GetDeclaredAccessibility(RuntimeField.Attributes);
        
        public override bool IsConst
        {
            get
            {
                return (RuntimeField.Attributes & FieldAttributes.Literal) != 0 || 
                       GetConstantValue(ConstantFieldsInProgress.Empty, earlyDecodingWellKnownAttributes: false) != null;
            }
        }

        internal override int? TypeLayoutOffset
        {
            get
            {
                // ???
                throw new NotImplementedException();
            }
        }

        internal override ObsoleteAttributeData ObsoleteAttributeData
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        protected override void LoadSignature()
        {
            // FieldInfo.Type -> type
            
            // Handle.GetOptionalCustomModifiers/GetRequiredCustomModifiers
            // -> isVolatile, custom mods

            // FixedAttribute -> isFixed
        }

        protected override ConstantValue DecodeConstantValue()
        {
            // TODO: GetValue(null), raw?
            return CodeAnalysis.ConstantValue.Null;
        }

        protected override void LoadCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes, bool omitDecimalConstantAttribute)
        {
            throw new NotImplementedException();
        }

        protected override CSharpAttributeData CreateDecimalConstantAttributeData()
        {
            throw new NotImplementedException();
        }
    }
}
