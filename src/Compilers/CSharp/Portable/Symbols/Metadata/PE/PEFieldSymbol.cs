// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.DocumentationComments;
using Microsoft.CodeAnalysis.CSharp.Emit;
using Roslyn.Utilities;
using System.Linq;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    /// <summary>
    /// The class to represent all fields imported from a PE/module.
    /// </summary>
    internal sealed class PEFieldSymbol : MetadataFieldSymbol
    {
        internal readonly FieldDefinitionHandle Handle;
        internal readonly FieldAttributes Flags;
        private readonly string _name;

        private ObsoleteAttributeData _lazyObsoleteAttributeData = ObsoleteAttributeData.Uninitialized;

        internal PEFieldSymbol(
            PEModuleSymbol moduleSymbol,
            PENamedTypeSymbol containingType,
            FieldDefinitionHandle handle) 
            : base(containingType)
        {
            Debug.Assert((object)moduleSymbol != null);
            Debug.Assert(!handle.IsNil);

            Handle = handle;

            try
            {
                moduleSymbol.Module.GetFieldDefPropsOrThrow(handle, out _name, out Flags);
            }
            catch (BadImageFormatException)
            {
                if ((object)_name == null)
                {
                    _name = string.Empty;
                }

                _lazyUseSiteDiagnostic = new CSDiagnosticInfo(ErrorCode.ERR_BindToBogus, this);
            }
        }

        private PEModuleSymbol ContainingPEModule => (PEModuleSymbol)ContainingModule;
        private PENamedTypeSymbol ContainingPEType => (PENamedTypeSymbol)ContainingType;

        public override string Name => _name;
        internal override bool HasSpecialName => (Flags & FieldAttributes.SpecialName) != 0;
        internal override bool HasRuntimeSpecialName => (Flags & FieldAttributes.RTSpecialName) != 0;
        internal override bool IsNotSerialized => (Flags & FieldAttributes.NotSerialized) != 0;
        internal override bool IsMarshalledExplicitly => ((Flags & FieldAttributes.HasFieldMarshal) != 0);
        public override bool IsReadOnly => (Flags & FieldAttributes.InitOnly) != 0;
        public override bool IsStatic => (Flags & FieldAttributes.Static) != 0;
        public override Accessibility DeclaredAccessibility => GetDeclaredAccessibility(Flags);

        internal override UnmanagedType MarshallingType
        {
            get
            {
                if ((Flags & FieldAttributes.HasFieldMarshal) == 0)
                {
                    return 0;
                }

                return ContainingPEModule.Module.GetMarshallingType(Handle);
            }
        }

        internal override ImmutableArray<byte> MarshallingDescriptor
        {
            get
            {
                if ((Flags & FieldAttributes.HasFieldMarshal) == 0)
                {
                    return default(ImmutableArray<byte>);
                }

                return ContainingPEModule.Module.GetMarshallingDescriptor(Handle);
            }
        }

        internal override int? TypeLayoutOffset => 
            ContainingPEModule.Module.GetFieldOffset(Handle);

        protected override void LoadSignature()
        {
            var moduleSymbol = ContainingPEModule;
            bool isVolatile;
            ImmutableArray<ModifierInfo<TypeSymbol>> customModifiers;
            TypeSymbol type = new MetadataDecoder(moduleSymbol, ContainingPEType).DecodeFieldSignature(Handle, out isVolatile, out customModifiers);
            ImmutableArray<CustomModifier> customModifiersArray = CSharpCustomModifier.Convert(customModifiers);
            type = DynamicTypeDecoder.TransformType(type, customModifiersArray.Length, Handle, moduleSymbol);
            _lazyIsVolatile = isVolatile;

            TypeSymbol fixedElementType;
            int fixedSize;
            if (customModifiersArray.IsEmpty && IsFixedBuffer(out fixedSize, out fixedElementType))
            {
                _lazyFixedSize = fixedSize;
                _lazyFixedImplementationType = type as NamedTypeSymbol;
                type = new PointerTypeSymbol(fixedElementType);
            }

            ImmutableInterlocked.InterlockedCompareExchange(ref _lazyCustomModifiers, customModifiersArray, default(ImmutableArray<CustomModifier>));
            Interlocked.CompareExchange(ref _lazyType, type, null);
        }

        private bool IsFixedBuffer(out int fixedSize, out TypeSymbol fixedElementType)
        {
            fixedSize = 0;
            fixedElementType = null;

            string elementTypeName;
            int bufferSize;
            PEModuleSymbol containingPEModule = this.ContainingPEModule;
            if (containingPEModule.Module.HasFixedBufferAttribute(Handle, out elementTypeName, out bufferSize))
            {
                var decoder = new MetadataDecoder(containingPEModule);
                var elementType = decoder.GetTypeSymbolForSerializedType(elementTypeName);
                if (elementType.FixedBufferElementSizeInBytes() != 0)
                {
                    fixedSize = bufferSize;
                    fixedElementType = elementType;
                    return true;
                }
            }

            return false;
        }

        public override bool IsConst
        {
            get
            {
                return (Flags & FieldAttributes.Literal) != 0 || 
                       GetConstantValue(ConstantFieldsInProgress.Empty, earlyDecodingWellKnownAttributes: false) != null;
            }
        }

        protected override ConstantValue DecodeConstantValue()
        {
            ConstantValue value = null;

            if ((Flags & FieldAttributes.Literal) != 0)
            {
                value = ContainingPEModule.Module.GetConstantFieldValue(Handle);
            }

            // If this is a Decimal, the constant value may come from DecimalConstantAttribute

            if (this.Type.SpecialType == SpecialType.System_Decimal)
            {
                ConstantValue defaultValue;

                if (ContainingPEModule.Module.HasDecimalConstantAttribute(Handle, out defaultValue))
                {
                    value = defaultValue;
                }
            }

            return value;
        }

        protected override void LoadCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes, bool omitDecimalConstantAttribute)
        {
            var containingPEModuleSymbol = (PEModuleSymbol)this.ContainingModule;

            if (omitDecimalConstantAttribute)
            {
                // filter out DecimalConstantAttribute
                CustomAttributeHandle ignore1;
                CustomAttributeHandle ignore2;
                var attributes = containingPEModuleSymbol.GetCustomAttributesForToken(
                    Handle,
                    out ignore1,
                    AttributeDescription.DecimalConstantAttribute,
                    out ignore2,
                    default(AttributeDescription));

                ImmutableInterlocked.InterlockedInitialize(ref lazyCustomAttributes, attributes);
            }
            else
            {
                containingPEModuleSymbol.LoadCustomAttributes(Handle, ref lazyCustomAttributes);
            }
        }

        protected override CSharpAttributeData CreateDecimalConstantAttributeData()
        {
            var containingPEModuleSymbol = ContainingPEModule;
            return new PEAttributeData(
                containingPEModuleSymbol,
                containingPEModuleSymbol.Module.FindLastTargetAttribute(Handle, AttributeDescription.DecimalConstantAttribute).Handle);
        }

        internal override ObsoleteAttributeData ObsoleteAttributeData
        {
            get
            {
                ObsoleteAttributeHelpers.InitializeObsoleteDataFromMetadata(ref _lazyObsoleteAttributeData, Handle, (PEModuleSymbol)(this.ContainingModule));
                return _lazyObsoleteAttributeData;
            }
        }
    }
}
