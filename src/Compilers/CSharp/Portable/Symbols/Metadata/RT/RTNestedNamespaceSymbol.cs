// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    using TypeInfo = System.Reflection.TypeInfo;

    internal sealed class RTNestedNamespaceSymbol : RTNamespaceSymbol
    {
        /// <summary>
        /// The parent namespace. There is always one, Global namespace contains all
        /// top level namespaces. 
        /// </summary>
        private readonly RTNamespaceSymbol _containingNamespaceSymbol;
        
        /// <summary>
        /// The name of the namespace.
        /// </summary>
        private readonly string _name;

        /// <summary>
        /// The sequence of groups of types contained within the namespace, 
        /// recursively including those from nested namespaces. The types are grouped by the 
        /// fully-qualified namespace name case-sensitively. There could be multiple groups 
        /// for each fully-qualified namespace name. The groups are sorted by their 
        /// key in case-sensitive manner. Empty string is used as namespace name for types 
        /// immediately contained within Global namespace. Therefore, all types in this namespace, if any, 
        /// will be in several first IGroupings.
        /// 
        /// This member is initialized by constructor and is cleared in EnsureAllMembersLoaded 
        /// as soon as symbols for children are created.
        /// </summary>
        private IEnumerable<IGrouping<string, TypeInfo>> _typesByNS;

        public RTNestedNamespaceSymbol(
            string name, 
            RTNamespaceSymbol containingNamespace,
            IEnumerable<IGrouping<string, TypeInfo>> typesByNS)
        {
            Debug.Assert(!string.IsNullOrEmpty(name));
            Debug.Assert((object)containingNamespace != null);
            Debug.Assert(typesByNS != null);

            _containingNamespaceSymbol = containingNamespace;
            _name = name;
            _typesByNS = typesByNS;
        }

        public override Symbol ContainingSymbol => _containingNamespaceSymbol;
        internal override MetadataModuleSymbol ContainingMetadataModule => _containingNamespaceSymbol.ContainingMetadataModule;
        public override string Name => _name;
        public override bool IsGlobalNamespace => false;
        public override AssemblySymbol ContainingAssembly => ContainingMetadataModule.ContainingAssembly;
        internal override ModuleSymbol ContainingModule => _containingNamespaceSymbol.ContainingMetadataModule;

        protected override void EnsureAllMembersLoaded()
        {
            var typesByNS = _typesByNS;

            if (!Initialized)
            {
                Debug.Assert(typesByNS != null);
                LoadAllMembers(typesByNS);
                Interlocked.Exchange(ref _typesByNS, null);
            }
        }
    }
}
