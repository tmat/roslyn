// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    /// <summary>
    /// The base class to represent a namespace imported from a PE/module. Namespaces that differ
    /// only by casing in name are not merged.
    /// </summary>
    internal abstract class PENamespaceSymbol : MetadataNamespaceSymbol<TypeDefinitionHandle, PENamespaceSymbol, PENamedTypeSymbol, PEFieldSymbol>
    {
        internal abstract PEModuleSymbol ContainingPEModule { get; }
        internal sealed override MetadataModuleSymbol ContainingMetadataModule => ContainingPEModule;

        protected sealed override PENamespaceSymbol CreateNestedNamespace(string name, IEnumerable<IGrouping<string, TypeDefinitionHandle>> typesByNamespace)
        {
            return new PENestedNamespaceSymbol(name, this, typesByNamespace);
        }

        protected sealed override NamedTypeSymbol CreateNoPiaTypeSymbol(TypeDefinitionHandle handle)
        {
            bool isNoPiaLocalType;
            var result = (NamedTypeSymbol)new MetadataDecoder(ContainingPEModule).GetTypeOfToken(handle, out isNoPiaLocalType);
            Debug.Assert(isNoPiaLocalType);
            return result;
        }

        protected sealed override void CreateContainedNamedTypes(
            IEnumerable<IGrouping<string, TypeDefinitionHandle>> typeHandleGroups,
            ArrayBuilder<PENamedTypeSymbol> result,
            ref Dictionary<string, TypeDefinitionHandle> lazyNoPiaLocalTypes)
        {
            var moduleSymbol = ContainingPEModule;
            var skipCheckForPiaType = !moduleSymbol.Module.ContainsNoPiaLocalTypes();

            foreach (var g in typeHandleGroups)
            {
                foreach (var t in g)
                {
                    if (skipCheckForPiaType || !moduleSymbol.Module.IsNoPiaLocalType(t))
                    {
                        result.Add(PENamedTypeSymbol.Create(moduleSymbol, this, t, g.Key));
                    }
                    else
                    {
                        try
                        {
                            string typeDefName = moduleSymbol.Module.GetTypeDefNameOrThrow(t);

                            if (lazyNoPiaLocalTypes == null)
                            {
                                lazyNoPiaLocalTypes = new Dictionary<string, TypeDefinitionHandle>();
                            }

                            lazyNoPiaLocalTypes[typeDefName] = t;
                        }
                        catch (BadImageFormatException)
                        { }
                    }
                }
            }
        }

        protected override void OnContainedNamedTypesCreated(Dictionary<string, ImmutableArray<PENamedTypeSymbol>> types)
        {
            ContainingPEModule.OnNewTypeDeclarationsLoaded(types);
        }
    }
}
