// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    using TypeInfo = System.Reflection.TypeInfo;

    internal sealed class RTNamedTypeSymbol : MetadataNamedTypeSymbol<TypeInfo, RTNamedTypeSymbol, RTFieldSymbol>
    {
        internal readonly TypeInfo RuntimeType;

        public RTNamedTypeSymbol(
            TypeInfo runtimeType,
            NamespaceOrTypeSymbol container,
            string emittedNamespaceName,
            string metadataNameOpt,
            ushort arity,
            out bool mangleName)
            : base(container, emittedNamespaceName, metadataNameOpt, runtimeType.Attributes, arity, out mangleName)
        {
            RuntimeType = runtimeType;
        }

        public override int Arity
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        protected override IComparer<Symbol> DeclarationOrderComparer
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        internal override TypeLayout Layout
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        internal override bool MangleName
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        internal override int MetadataArity
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        protected override IEnumerable<RTNamedTypeSymbol> CreateNestedTypes()
        {
            throw new NotImplementedException();
        }

        protected override string DecodeDefaultMemberNameAttribute()
        {
            throw new NotImplementedException();
        }

        protected override NamedTypeSymbol DecodeEnumUnderlyingType()
        {
            throw new NotImplementedException();
        }

        protected override TypeSymbol GetCoClassType()
        {
            throw new NotImplementedException();
        }

        protected override ImmutableArray<string> GetConditionalAttributeValues()
        {
            throw new NotImplementedException();
        }

        protected override AttributeUsageInfo? GetDeclaredAttributeUsageInfo()
        {
            throw new NotImplementedException();
        }

        protected override bool HasAnyCustomAttributes()
        {
            throw new NotImplementedException();
        }

        protected override bool HasExtensionAttribute()
        {
            throw new NotImplementedException();
        }

        protected override bool HasRequiredAttributeAttribute()
        {
            throw new NotImplementedException();
        }

        protected override void LoadCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes)
        {
            throw new NotImplementedException();
        }

        protected override HashSet<string> LoadNonTypeMemberNames()
        {
            throw new NotImplementedException();
        }

        protected override void LoadObsoleteAttributeData(ref ObsoleteAttributeData lazyObsoleteAttributeData)
        {
            throw new NotImplementedException();
        }

        protected override NamedTypeSymbol MakeDeclaredBaseType()
        {
            throw new NotImplementedException();
        }

        protected override ImmutableArray<NamedTypeSymbol> MakeDeclaredInterfaces()
        {
            throw new NotImplementedException();
        }

        protected override IEnumerable<FieldSymbol> MergeEnumFieldsToEmit(ImmutableArray<Symbol> allMembers, ImmutableArray<RTFieldSymbol> instanceEnumFields)
        {
            throw new NotImplementedException();
        }

        protected override IEnumerable<FieldSymbol> MergeNonEnumFieldsToEmit(IEnumerable<FieldSymbol> nonEventFields, ArrayBuilder<FieldSymbol> eventFields)
        {
            throw new NotImplementedException();
        }

        protected override void OnContainedNamedTypesCreated(Dictionary<string, ImmutableArray<RTNamedTypeSymbol>> types)
        {
            throw new NotImplementedException();
        }

        protected override void PopulateEnumInstanceFields(ArrayBuilder<RTFieldSymbol> fields)
        {
            throw new NotImplementedException();
        }

        protected override void PopulateMembersInDeclarationOrder(ArrayBuilder<Symbol> members)
        {
            throw new NotImplementedException();
        }
    }
}
