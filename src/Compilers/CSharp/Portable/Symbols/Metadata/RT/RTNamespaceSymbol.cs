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
    using TypeInfo = System.Reflection.TypeInfo;
   
    /// <summary>
    /// The base class to represent a namespace imported from a PE/module. Namespaces that differ
    /// only by casing in name are not merged.
    /// </summary>
    internal abstract class RTNamespaceSymbol : MetadataNamespaceSymbol<TypeInfo, RTNamespaceSymbol, RTNamedTypeSymbol, RTFieldSymbol>
    {
        protected sealed override RTNamespaceSymbol CreateNestedNamespace(string name, IEnumerable<IGrouping<string, TypeInfo>> typesByNamespace)
        {
            return new RTNestedNamespaceSymbol(name, this, typesByNamespace);
        }

        protected sealed override NamedTypeSymbol CreateNoPiaTypeSymbol(TypeInfo handle)
        {
            // TODO:
            //bool isNoPiaLocalType;
            //var result = (NamedTypeSymbol)new MetadataDecoder(ContainingPEModule).GetTypeOfToken(handle, out isNoPiaLocalType);
            //Debug.Assert(isNoPiaLocalType);
            //return result;
            return null;
        }

        protected sealed override void CreateContainedNamedTypes(
            IEnumerable<IGrouping<string, TypeInfo>> typeHandleGroups,
            ArrayBuilder<RTNamedTypeSymbol> result,
            ref Dictionary<string, TypeInfo> lazyNoPiaLocalTypes)
        {
            throw new NotImplementedException();
        }

        protected sealed override void OnContainedNamedTypesCreated(Dictionary<string, ImmutableArray<RTNamedTypeSymbol>> types)
        {
            // TODO
        }
    }
}
