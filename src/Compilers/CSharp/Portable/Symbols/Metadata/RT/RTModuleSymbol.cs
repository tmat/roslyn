// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis.CSharp.Symbols.Retargeting;
using Roslyn.Utilities;
using System.Reflection.PortableExecutable;
using System.Reflection;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    using TypeInfo = System.Reflection.TypeInfo;
    
    /// <summary>
    /// Represents a net-module imported from runtime metadata. Can be a primary module of an assembly.
    /// </summary>
    internal sealed class RTModuleSymbol : MetadataModuleSymbol
    {
        private readonly Module _module;
        private readonly RTGlobalNamespaceSymbol _globalNamespace;

        internal RTModuleSymbol(RTAssemblySymbol assemblySymbol, Module module, MetadataImportOptions importOptions, int ordinal)
            : this((AssemblySymbol)assemblySymbol, module, importOptions, ordinal)
        {
            Debug.Assert(ordinal >= 0);
        }

        internal RTModuleSymbol(SourceAssemblySymbol assemblySymbol, Module module, MetadataImportOptions importOptions, int ordinal)
            : this((AssemblySymbol)assemblySymbol, module, importOptions, ordinal)
        {
            Debug.Assert(ordinal > 0);
        }

        internal RTModuleSymbol(RetargetingAssemblySymbol assemblySymbol, Module module, MetadataImportOptions importOptions, int ordinal)
            : this((AssemblySymbol)assemblySymbol, module, importOptions, ordinal)
        {
            Debug.Assert(ordinal > 0);
        }

        private RTModuleSymbol(AssemblySymbol assemblySymbol, Module module, MetadataImportOptions importOptions, int ordinal)
            : base(assemblySymbol, importOptions, ordinal)
        {
            Debug.Assert(module != null);

            _module = module;
            _globalNamespace = new RTGlobalNamespaceSymbol(this);
        }

        public override NamespaceSymbol GlobalNamespace => _globalNamespace;

        // TODO:
        internal override bool Bit32Required => false;

        // TODO:
        internal override Machine Machine => Machine.I386;

        protected override IdentifierCollection GetNamespaceNames()
        {
            throw new NotImplementedException();
        }

        protected override IdentifierCollection GetTypeNames()
        {
            throw new NotImplementedException();
        }

        protected override void LoadAssemblyCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes)
        {
            throw new NotImplementedException();
        }

        protected override void LoadModuleCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes)
        {
            throw new NotImplementedException();
        }

        internal override AssemblySymbol GetAssemblyForForwardedType(ref MetadataTypeName fullName)
        {
            throw new NotImplementedException();
        }

        internal override IEnumerable<NamedTypeSymbol> GetForwardedTypes()
        {
            throw new NotImplementedException();
        }
    }
}
