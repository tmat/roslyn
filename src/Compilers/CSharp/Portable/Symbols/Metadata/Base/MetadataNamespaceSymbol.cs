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
    internal abstract class MetadataNamespaceSymbol<TTypeDefinitionHandle, TNamespaceSymbol, TNamedTypeSymbol, TFieldSymbol> : NamespaceSymbol
        where TNamespaceSymbol : MetadataNamespaceSymbol<TTypeDefinitionHandle, TNamespaceSymbol, TNamedTypeSymbol, TFieldSymbol>
        where TNamedTypeSymbol : MetadataNamedTypeSymbol<TTypeDefinitionHandle, TNamedTypeSymbol, TFieldSymbol>
        where TFieldSymbol : FieldSymbol
    {
        /// <summary>
        /// A map of namespaces immediately contained within this namespace 
        /// mapped by their name (case-sensitively).
        /// </summary>
        private Dictionary<string, TNamespaceSymbol> _lazyNamespaces;

        /// <summary>
        /// A map of types immediately contained within this namespace 
        /// grouped by their name (case-sensitively).
        /// </summary>
        private Dictionary<string, ImmutableArray<TNamedTypeSymbol>> _lazyTypes;

        /// <summary>
        /// A map of NoPia local types immediately contained in this assembly.
        /// Maps type name (non-qualified) to the row id. Note, for VB we should use
        /// full name.
        /// </summary>
        private Dictionary<string, TTypeDefinitionHandle> _lazyNoPiaLocalTypes;

        /// <summary>
        /// All type members in a flat array
        /// </summary>
        private ImmutableArray<NamedTypeSymbol> _lazyFlattenedTypes;

        #region Initialization

        protected abstract TNamespaceSymbol CreateNestedNamespace(string name, IEnumerable<IGrouping<string, TTypeDefinitionHandle>> typesByNamespace);

        protected abstract NamedTypeSymbol CreateNoPiaTypeSymbol(TTypeDefinitionHandle handle);

        protected abstract void CreateContainedNamedTypes(
            IEnumerable<IGrouping<string, TTypeDefinitionHandle>> typeHandleGroups,
            ArrayBuilder<TNamedTypeSymbol> result,
            ref Dictionary<string, TTypeDefinitionHandle> lazyNoPiaLocalTypes);

        protected abstract void OnContainedNamedTypesCreated(Dictionary<string, ImmutableArray<TNamedTypeSymbol>> types);

        protected abstract void EnsureAllMembersLoaded();
        protected bool Initialized => _lazyNamespaces != null && _lazyTypes != null;

        private ImmutableArray<NamedTypeSymbol> GetMemberTypesPrivate()
        {
            //assume that EnsureAllMembersLoaded() has initialize lazyTypes
            if (_lazyFlattenedTypes.IsDefault)
            {
                var flattened = StaticCast<NamedTypeSymbol>.From(_lazyTypes.Flatten());
                ImmutableInterlocked.InterlockedExchange(ref _lazyFlattenedTypes, flattened);
            }

            return _lazyFlattenedTypes;
        }

        private IEnumerable<TNamespaceSymbol> GetMemberNamespacesPrivate()
        {
            return _lazyNamespaces.Values;
        }

        public sealed override ImmutableArray<Symbol> GetMembers()
        {
            EnsureAllMembersLoaded();

            var builder = ArrayBuilder<Symbol>.GetInstance();
            builder.AddRange(GetMemberTypesPrivate());
            builder.AddRange(GetMemberNamespacesPrivate());
            return builder.ToImmutableAndFree();
        }

        public sealed override ImmutableArray<Symbol> GetMembers(string name)
        {
            EnsureAllMembersLoaded();

            TNamespaceSymbol ns = null;
            ImmutableArray<TNamedTypeSymbol> types;

            if (_lazyNamespaces.TryGetValue(name, out ns))
            {
                if (_lazyTypes.TryGetValue(name, out types))
                {
                    // TODO - Eliminate the copy by storing all members and type members instead of non-type and type members?
                    return StaticCast<Symbol>.From(types).Add(ns);
                }
                else
                {
                    return ImmutableArray.Create<Symbol>(ns);
                }
            }
            else if (_lazyTypes.TryGetValue(name, out types))
            {
                return StaticCast<Symbol>.From(types);
            }

            return ImmutableArray<Symbol>.Empty;
        }

        public sealed override ImmutableArray<NamedTypeSymbol> GetTypeMembers()
        {
            EnsureAllMembersLoaded();

            return GetMemberTypesPrivate();
        }

        public sealed override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name)
        {
            EnsureAllMembersLoaded();

            ImmutableArray<TNamedTypeSymbol> types;

            return _lazyTypes.TryGetValue(name, out types)
                ? StaticCast<NamedTypeSymbol>.From(types)
                : ImmutableArray<NamedTypeSymbol>.Empty;
        }

        /// <summary>
        /// Initializes namespaces and types maps with information about 
        /// namespaces and types immediately contained within this namespace.
        /// </summary>
        /// <param name="typesByNS">
        /// The sequence of groups of TypeDef row ids for types contained within the namespace, 
        /// recursively including those from nested namespaces. The row ids must be grouped by the 
        /// fully-qualified namespace name case-sensitively. There could be multiple groups 
        /// for each fully-qualified namespace name. The groups must be sorted by
        /// their key in case-sensitive manner. Empty string must be used as namespace name for types 
        /// immediately contained within Global namespace. Therefore, all types in this namespace, if any, 
        /// must be in several first IGroupings.
        /// </param>
        protected void LoadAllMembers(IEnumerable<IGrouping<string, TTypeDefinitionHandle>> typesByNS)
        {
            Debug.Assert(typesByNS != null);

            // A sequence of groups of TypeDef row ids for types immediately contained within this namespace.
            IEnumerable<IGrouping<string, TTypeDefinitionHandle>> nestedTypes = null;

            // A sequence with information about namespaces immediately contained within this namespace.
            // For each pair:
            //    Key - contains simple name of a child namespace.
            //    Value – contains a sequence similar to the one passed to this function, but
            //            calculated for the child namespace. 
            IEnumerable<KeyValuePair<string, IEnumerable<IGrouping<string, TTypeDefinitionHandle>>>> nestedNamespaces = null;

            MetadataHelpers.GetInfoForImmediateNamespaceMembers(
                this.ToDisplayString(SymbolDisplayFormat.QualifiedNameOnlyFormat).Length,
                typesByNS,
                StringComparer.Ordinal,
                out nestedTypes, out nestedNamespaces);

            LazyInitializeNamespaces(nestedNamespaces);

            LazyInitializeTypes(nestedTypes);
        }

        /// <summary>
        /// Create symbols for nested namespaces and initialize namespaces map.
        /// </summary>
        private void LazyInitializeNamespaces(
            IEnumerable<KeyValuePair<string, IEnumerable<IGrouping<string, TTypeDefinitionHandle>>>> childNamespaces)
        {
            if (_lazyNamespaces == null)
            {
                var namespaces = new Dictionary<string, TNamespaceSymbol>();

                foreach (var child in childNamespaces)
                {
                    string name = child.Key;
                    namespaces.Add(name, CreateNestedNamespace(name, child.Value));
                }

                Interlocked.CompareExchange(ref _lazyNamespaces, namespaces, null);
            }
        }

        /// <summary>
        /// Create symbols for nested types and initialize types map.
        /// </summary>
        private void LazyInitializeTypes(IEnumerable<IGrouping<string, TTypeDefinitionHandle>> typeGroups)
        {
            if (_lazyTypes == null)
            {
                var children = ArrayBuilder<TNamedTypeSymbol>.GetInstance();

                Dictionary<string, TTypeDefinitionHandle> lazyNoPiaLocalTypes = null;
                CreateContainedNamedTypes(typeGroups, children, ref lazyNoPiaLocalTypes);

                var typesDict = children.ToDictionary(c => c.Name);
                children.Free();

                if (lazyNoPiaLocalTypes != null)
                {
                    Interlocked.CompareExchange(ref _lazyNoPiaLocalTypes, lazyNoPiaLocalTypes, null);
                }

                var original = Interlocked.CompareExchange(ref _lazyTypes, typesDict, null);

                // Build cache of TypeDef Tokens
                // Potentially this can be done in the background.
                if (original == null)
                {
                    OnContainedNamedTypesCreated(typesDict);
                }
            }
        }

        internal NamedTypeSymbol LookupMetadataType(ref MetadataTypeName emittedTypeName, out bool isNoPiaLocalType)
        {
            NamedTypeSymbol result = LookupMetadataType(ref emittedTypeName);
            isNoPiaLocalType = false;

            if (result is MissingMetadataTypeSymbol)
            {
                EnsureAllMembersLoaded();

                // See if this is a NoPia local type, which we should unify.
                // Note, VB should use FullName.
                TTypeDefinitionHandle typeDef;
                if (_lazyNoPiaLocalTypes != null && _lazyNoPiaLocalTypes.TryGetValue(emittedTypeName.TypeName, out typeDef))
                {
                    result = CreateNoPiaTypeSymbol(typeDef);
                }
            }

            return result;
        }

        #endregion

        internal abstract MetadataModuleSymbol ContainingMetadataModule { get; }

        internal sealed override NamespaceExtent Extent => new NamespaceExtent(ContainingMetadataModule);

        public sealed override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
        {
            return GetTypeMembers(name).WhereAsArray(type => type.Arity == arity);
        }

        public sealed override ImmutableArray<Location> Locations =>
            ContainingMetadataModule.MetadataLocation.Cast<MetadataLocation, Location>();

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => 
            ImmutableArray<SyntaxReference>.Empty;

        // perf, not correctness
        internal sealed override CSharpCompilation DeclaringCompilation => null;
    }
}
