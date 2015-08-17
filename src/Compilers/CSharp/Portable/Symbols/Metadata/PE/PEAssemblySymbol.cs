// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using Roslyn.Utilities;
using System.Diagnostics;
using System.Linq;
using System;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    /// <summary>
    /// Represents an assembly imported from a PE.
    /// </summary>
    internal sealed class PEAssemblySymbol : MetadataAssemblySymbol
    {
        /// <summary>
        /// The list of contained <see cref="PEModuleSymbol"/> objects.
        /// The list doesn't use type ImmutableArray{PEModuleSymbol} so that we
        /// can return it from Modules property as is.
        /// 
        /// Immutable after initialized in derived constructor.
        /// </summary>
        private readonly ImmutableArray<ModuleSymbol> _modules;

        internal PEAssembly Assembly { get; }

        internal PEAssemblySymbol(PEAssembly assembly, DocumentationProvider documentationProvider, bool isLinked, MetadataImportOptions importOptions)
            : base(documentationProvider, isLinked, importOptions)
        {
            Debug.Assert(assembly != null);
            Assembly = assembly;

            var modules = new ModuleSymbol[assembly.Modules.Length];

            for (int i = 0; i < assembly.Modules.Length; i++)
            {
                modules[i] = new PEModuleSymbol(this, assembly.Modules[i], importOptions, i);
            }

            _modules = modules.AsImmutableOrNull();
        }

        public override ImmutableArray<ModuleSymbol> Modules => _modules;
        public override AssemblyIdentity Identity => Assembly.Identity;

        protected override void LoadAssemblyCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes)
        {
            Debug.Assert(this.MightContainExtensionMethods);
            this.PrimaryModule.LoadCustomAttributesFilterExtensions(Assembly.Handle, ref lazyCustomAttributes);
        }

        internal override IEnumerable<ImmutableArray<byte>> GetInternalsVisibleToPublicKeys(string simpleName)
        {
            return Assembly.GetInternalsVisibleToPublicKeys(simpleName);
        }
    }
}
