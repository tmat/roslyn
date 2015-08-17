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
    /// <summary>
    /// Represents a net-module imported from a PE. Can be a primary module of an assembly.
    /// </summary>
    internal abstract class MetadataModuleSymbol : NonMissingModuleSymbol
    {
        /// <summary>
        /// Owning AssemblySymbol. This can be a <see cref="MetadataAssemblySymbol"/>, <see cref="SourceAssemblySymbol"/>, 
        /// or <see cref="RetargetingAssemblySymbol"/>.
        /// </summary>
        private readonly AssemblySymbol _assemblySymbol;
        private readonly int _ordinal;

        /// <summary>
        /// Cache the symbol for well-known type System.Type because we use it frequently
        /// (for attributes).
        /// </summary>
        private NamedTypeSymbol _lazySystemTypeSymbol;
        private NamedTypeSymbol _lazyEventRegistrationTokenSymbol;
        private NamedTypeSymbol _lazyEventRegistrationTokenTableSymbol;

        internal readonly ImmutableArray<MetadataLocation> MetadataLocation;
        internal readonly MetadataImportOptions ImportOptions;

        /// <summary>
        /// Module's custom attributes
        /// </summary>
        private ImmutableArray<CSharpAttributeData> _lazyCustomAttributes;

        /// <summary>
        /// Module's assembly attributes
        /// </summary>
        private ImmutableArray<CSharpAttributeData> _lazyAssemblyAttributes;

        // Type names from module
        private ICollection<string> _lazyTypeNames;

        // Namespace names from module
        private ICollection<string> _lazyNamespaceNames;

        protected MetadataModuleSymbol(AssemblySymbol assemblySymbol, MetadataImportOptions importOptions, int ordinal)
        {
            Debug.Assert(ordinal >= 0);
            Debug.Assert((object)assemblySymbol != null);

            _assemblySymbol = assemblySymbol;
            _ordinal = ordinal;
            this.ImportOptions = importOptions;

            this.MetadataLocation = ImmutableArray.Create<MetadataLocation>(new MetadataLocation(this));
        }

        internal override int Ordinal => _ordinal;

        public sealed override Symbol ContainingSymbol => _assemblySymbol;
        public sealed override AssemblySymbol ContainingAssembly => _assemblySymbol;
        public sealed override ImmutableArray<Location> Locations => MetadataLocation.Cast<MetadataLocation, Location>();

        public override ImmutableArray<CSharpAttributeData> GetAttributes()
        {
            if (_lazyCustomAttributes.IsDefault)
            {
                LoadModuleCustomAttributes(ref _lazyCustomAttributes);
            }

            return _lazyCustomAttributes;
        }

        protected abstract void LoadModuleCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes);
        protected abstract void LoadAssemblyCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes);

        internal ImmutableArray<CSharpAttributeData> GetAssemblyAttributes()
        {
            if (_lazyAssemblyAttributes.IsDefault)
            {
                LoadAssemblyCustomAttributes(ref _lazyAssemblyAttributes);
            }

            return _lazyAssemblyAttributes;
        }

        internal sealed override ICollection<string> TypeNames
        {
            get
            {
                if (_lazyTypeNames == null)
                {
                    Interlocked.CompareExchange(ref _lazyTypeNames, GetTypeNames().AsCaseSensitiveCollection(), null);
                }

                return _lazyTypeNames;
            }
        }

        protected abstract IdentifierCollection GetTypeNames();

        internal sealed override ICollection<string> NamespaceNames
        {
            get
            {
                if (_lazyNamespaceNames == null)
                {
                    Interlocked.CompareExchange(ref _lazyNamespaceNames, GetNamespaceNames().AsCaseSensitiveCollection(), null);
                }

                return _lazyNamespaceNames;
            }
        }

        protected abstract IdentifierCollection GetNamespaceNames();

        internal DocumentationProvider DocumentationProvider
        {
            get
            {
                var assembly = _assemblySymbol as MetadataAssemblySymbol;
                if ((object)assembly != null)
                {
                    return assembly.DocumentationProvider;
                }
                else
                {
                    return DocumentationProvider.Default;
                }
            }
        }

        internal NamedTypeSymbol EventRegistrationToken
        {
            get
            {
                if ((object)_lazyEventRegistrationTokenSymbol == null)
                {
                    Interlocked.CompareExchange(ref _lazyEventRegistrationTokenSymbol,
                                                GetTypeSymbolForWellKnownType(
                                                    WellKnownType.System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken
                                                    ),
                                                null);
                    Debug.Assert((object)_lazyEventRegistrationTokenSymbol != null);
                }
                return _lazyEventRegistrationTokenSymbol;
            }
        }


        internal NamedTypeSymbol EventRegistrationTokenTable_T
        {
            get
            {
                if ((object)_lazyEventRegistrationTokenTableSymbol == null)
                {
                    Interlocked.CompareExchange(ref _lazyEventRegistrationTokenTableSymbol,
                                                GetTypeSymbolForWellKnownType(
                                                    WellKnownType.System_Runtime_InteropServices_WindowsRuntime_EventRegistrationTokenTable_T
                                                    ),
                                                null);
                    Debug.Assert((object)_lazyEventRegistrationTokenTableSymbol != null);
                }
                return _lazyEventRegistrationTokenTableSymbol;
            }
        }

        internal NamedTypeSymbol SystemTypeSymbol
        {
            get
            {
                if ((object)_lazySystemTypeSymbol == null)
                {
                    Interlocked.CompareExchange(ref _lazySystemTypeSymbol,
                                                GetTypeSymbolForWellKnownType(WellKnownType.System_Type),
                                                null);
                    Debug.Assert((object)_lazySystemTypeSymbol != null);
                }
                return _lazySystemTypeSymbol;
            }
        }

        private NamedTypeSymbol GetTypeSymbolForWellKnownType(WellKnownType type)
        {
            MetadataTypeName emittedName = MetadataTypeName.FromFullName(type.GetMetadataName(), useCLSCompliantNameArityEncoding: true);
            // First, check this module
            NamedTypeSymbol currentModuleResult = this.LookupTopLevelMetadataType(ref emittedName);

            if (IsAcceptableSystemTypeSymbol(currentModuleResult))
            {
                // It doesn't matter if there's another of this type in a referenced assembly -
                // we prefer the one in the current module.
                return currentModuleResult;
            }

            // If we didn't find it in this module, check the referenced assemblies
            NamedTypeSymbol referencedAssemblyResult = null;
            foreach (AssemblySymbol assembly in this.GetReferencedAssemblySymbols())
            {
                NamedTypeSymbol currResult = assembly.LookupTopLevelMetadataType(ref emittedName, digThroughForwardedTypes: true);
                if (IsAcceptableSystemTypeSymbol(currResult))
                {
                    if ((object)referencedAssemblyResult == null)
                    {
                        referencedAssemblyResult = currResult;
                    }
                    else
                    {
                        // CONSIDER: setting result to null will result in a MissingMetadataTypeSymbol 
                        // being returned.  Do we want to differentiate between no result and ambiguous
                        // results?  There doesn't seem to be an existing error code for "duplicate well-
                        // known type".
                        if (referencedAssemblyResult != currResult)
                        {
                            referencedAssemblyResult = null;
                        }
                        break;
                    }
                }
            }

            if ((object)referencedAssemblyResult != null)
            {
                Debug.Assert(IsAcceptableSystemTypeSymbol(referencedAssemblyResult));
                return referencedAssemblyResult;
            }

            Debug.Assert((object)currentModuleResult != null);
            return currentModuleResult;
        }

        private static bool IsAcceptableSystemTypeSymbol(NamedTypeSymbol candidate)
        {
            return candidate.Kind != SymbolKind.ErrorType || !(candidate is MissingMetadataTypeSymbol);
        }

        internal override bool HasAssemblyCompilationRelaxationsAttribute
        {
            get
            {
                var assemblyAttributes = GetAssemblyAttributes();
                return assemblyAttributes.IndexOfAttribute(this, AttributeDescription.CompilationRelaxationsAttribute) >= 0;
            }
        }

        internal override bool HasAssemblyRuntimeCompatibilityAttribute
        {
            get
            {
                var assemblyAttributes = GetAssemblyAttributes();
                return assemblyAttributes.IndexOfAttribute(this, AttributeDescription.RuntimeCompatibilityAttribute) >= 0;
            }
        }

        internal override CharSet? DefaultMarshallingCharSet
        {
            get
            {
                // not used by the compiler
                throw ExceptionUtilities.Unreachable;
            }
        }

        internal sealed override CSharpCompilation DeclaringCompilation // perf, not correctness
        {
            get { return null; }
        }

        internal NamedTypeSymbol LookupTopLevelMetadataType(ref MetadataTypeName emittedName, out bool isNoPiaLocalType)
        {
            NamedTypeSymbol result;
            PENamespaceSymbol scope = (PENamespaceSymbol)this.GlobalNamespace.LookupNestedNamespace(emittedName.NamespaceSegments);

            if ((object)scope == null)
            {
                // We failed to locate the namespace
                isNoPiaLocalType = false;
                result = new MissingMetadataTypeSymbol.TopLevel(this, ref emittedName);
            }
            else
            {
                result = scope.LookupMetadataType(ref emittedName, out isNoPiaLocalType);
                Debug.Assert((object)result != null);
            }

            return result;
        }

        /// <summary>
        /// If this module forwards the given type to another assembly, return that assembly;
        /// otherwise, return null.
        /// </summary>
        /// <param name="fullName">Type to look up.</param>
        /// <returns>Assembly symbol or null.</returns>
        /// <remarks>
        /// The returned assembly may also forward the type.
        /// </remarks>
        internal abstract AssemblySymbol GetAssemblyForForwardedType(ref MetadataTypeName fullName);

        internal abstract IEnumerable<NamedTypeSymbol> GetForwardedTypes();
    }
}
