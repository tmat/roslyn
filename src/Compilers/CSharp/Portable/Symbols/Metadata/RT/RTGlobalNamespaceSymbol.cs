// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Roslyn.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    using TypeInfo = System.Reflection.TypeInfo;

    internal sealed class RTGlobalNamespaceSymbol : RTNamespaceSymbol
    {
        /// <summary>
        /// The module containing the namespace.
        /// </summary>
        private readonly RTModuleSymbol _moduleSymbol;

        internal RTGlobalNamespaceSymbol(RTModuleSymbol moduleSymbol)
        {
            Debug.Assert((object)moduleSymbol != null);
            _moduleSymbol = moduleSymbol;
        }

        public override Symbol ContainingSymbol => _moduleSymbol;
        internal override MetadataModuleSymbol ContainingMetadataModule => _moduleSymbol;
        public override string Name => string.Empty;
        public override bool IsGlobalNamespace => true;
        public override AssemblySymbol ContainingAssembly => _moduleSymbol.ContainingAssembly;
        internal override ModuleSymbol ContainingModule => _moduleSymbol;

        protected override void EnsureAllMembersLoaded()
        {
            if (!Initialized)
            {
                LoadAllMembers(GetTypeGroups());
            }
        }

        private IEnumerable<IGrouping<string, TypeInfo>> GetTypeGroups()
        {
            //try
            //{
            //    return _moduleSymbol.Module.GroupTypesByNamespaceOrThrow(StringComparer.Ordinal);
            //}
            //catch (BadImageFormatException)
            //{
            //    return SpecializedCollections.EmptyEnumerable<IGrouping<string, TypeDefinitionHandle>>();
            //}
            throw new NotImplementedException();
        }
    }
}
