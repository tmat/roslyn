// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    using TypeInfo = System.Reflection.TypeInfo;
   
    /// <summary>
    /// Represents an assembly imported from a runtime assembly.
    /// </summary>
    internal sealed class RTAssemblySymbol : MetadataAssemblySymbol
    {
        /// <summary>
        /// The list of contained <see cref="RTModuleSymbol"/> objects.
        /// The list doesn't use type ImmutableArray{RTModuleSymbol} so that we
        /// can return it from Modules property as is.
        /// 
        /// Immutable after initialized in derived constructor.
        /// </summary>
        private readonly ImmutableArray<ModuleSymbol> _modules;

        internal Assembly Assembly { get; }

        private readonly AssemblyIdentity _identity;

        internal RTAssemblySymbol(Assembly assembly, DocumentationProvider documentationProvider, bool isLinked, MetadataImportOptions importOptions)
            : base(documentationProvider, isLinked, importOptions)
        {
            Debug.Assert(assembly != null);
            Assembly = assembly;

            _identity = AssemblyIdentity.FromAssemblyDefinition(assembly);

            var modules = ArrayBuilder<ModuleSymbol>.GetInstance();

            int i = 0;
            foreach (var module in assembly.Modules)
            {
                modules.Add(new RTModuleSymbol(this, module, importOptions, i));
            }

            _modules = modules.ToImmutableAndFree();
        }

        public override ImmutableArray<ModuleSymbol> Modules => _modules;
        public override AssemblyIdentity Identity => _identity;

        protected override void LoadAssemblyCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes)
        {
            Debug.Assert(this.MightContainExtensionMethods);
            // this.PrimaryModule.LoadCustomAttributesFilterExtensions(Assembly.Handle, ref lazyCustomAttributes);
            throw new NotImplementedException();
        }

        internal override IEnumerable<ImmutableArray<byte>> GetInternalsVisibleToPublicKeys(string simpleName)
        {
            // return Assembly.GetInternalsVisibleToPublicKeys(simpleName);
            throw new NotImplementedException();
        }
    }
}
