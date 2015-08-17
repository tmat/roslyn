// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using Roslyn.Utilities;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    /// <summary>
    /// Represents an assembly imported from metadata.
    /// </summary>
    internal abstract class MetadataAssemblySymbol : MetadataOrSourceAssemblySymbol
    {
        /// <summary>
        /// A DocumentationProvider that provides XML documentation comments for this assembly.
        /// </summary>
        private readonly DocumentationProvider _documentationProvider;

        /// <summary>
        /// An array of assemblies involved in canonical type resolution of
        /// NoPia local types defined within this assembly. In other words, all 
        /// references used by a compilation referencing this assembly.
        /// The array and its content is provided by ReferenceManager and must not be modified.
        /// </summary>
        private ImmutableArray<AssemblySymbol> _noPiaResolutionAssemblies;

        /// <summary>
        /// An array of assemblies referenced by this assembly, which are linked (/l-ed) by 
        /// each compilation that is using this AssemblySymbol as a reference. 
        /// If this AssemblySymbol is linked too, it will be in this array too.
        /// The array and its content is provided by ReferenceManager and must not be modified.
        /// </summary>
        private ImmutableArray<AssemblySymbol> _linkedReferencedAssemblies;

        /// <summary>
        /// Assembly is /l-ed by compilation that is using it as a reference.
        /// </summary>
        private readonly bool _isLinked;

        /// <summary>
        /// Assembly's custom attributes
        /// </summary>
        private ImmutableArray<CSharpAttributeData> _lazyCustomAttributes;

        internal MetadataAssemblySymbol(DocumentationProvider documentationProvider, bool isLinked, MetadataImportOptions importOptions)
        {
            Debug.Assert(documentationProvider != null);

            _documentationProvider = documentationProvider;
            _isLinked = isLinked;
        }

        internal sealed override ImmutableArray<byte> PublicKey => Identity.PublicKey;

        public sealed override ImmutableArray<Location> Locations => 
            PrimaryModule.MetadataLocation.Cast<MetadataLocation, Location>();

        public sealed override ImmutableArray<CSharpAttributeData> GetAttributes()
        {
            if (_lazyCustomAttributes.IsDefault)
            {
                LoadAssemblyCustomAttributes(ref _lazyCustomAttributes);
            }

            return _lazyCustomAttributes;
        }

        protected abstract void LoadAssemblyCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes);

        /// <summary>
        /// Look up the assembly to which the given metadata type is forwarded.
        /// </summary>
        /// <param name="emittedName"></param>
        /// <returns>
        /// The assembly to which the given type is forwarded or null, if there isn't one.
        /// </returns>
        /// <remarks>
        /// The returned assembly may also forward the type.
        /// </remarks>
        internal AssemblySymbol LookupAssemblyForForwardedMetadataType(ref MetadataTypeName emittedName)
        {
            // Look in the type forwarders of the primary module of this assembly, clr does not honor type forwarder
            // in non-primary modules.

            // Examine the type forwarders, but only from the primary module.
            return this.PrimaryModule.GetAssemblyForForwardedType(ref emittedName);
        }

        internal sealed override NamedTypeSymbol TryLookupForwardedMetadataTypeWithCycleDetection(ref MetadataTypeName emittedName, ConsList<AssemblySymbol> visitedAssemblies)
        {
            // Check if it is a forwarded type.
            var forwardedToAssembly = LookupAssemblyForForwardedMetadataType(ref emittedName);
            if ((object)forwardedToAssembly != null)
            {
                // Don't bother to check the forwarded-to assembly if we've already seen it.
                if (visitedAssemblies != null && visitedAssemblies.Contains(forwardedToAssembly))
                {
                    return CreateCycleInTypeForwarderErrorTypeSymbol(ref emittedName);
                }
                else
                {
                    visitedAssemblies = new ConsList<AssemblySymbol>(this, visitedAssemblies ?? ConsList<AssemblySymbol>.Empty);
                    return forwardedToAssembly.LookupTopLevelMetadataTypeWithCycleDetection(ref emittedName, visitedAssemblies, digThroughForwardedTypes: true);
                }
            }

            return null;
        }

        internal sealed override ImmutableArray<AssemblySymbol> GetNoPiaResolutionAssemblies()
        {
            return _noPiaResolutionAssemblies;
        }

        internal sealed override void SetNoPiaResolutionAssemblies(ImmutableArray<AssemblySymbol> assemblies)
        {
            _noPiaResolutionAssemblies = assemblies;
        }

        internal sealed override void SetLinkedReferencedAssemblies(ImmutableArray<AssemblySymbol> assemblies)
        {
            _linkedReferencedAssemblies = assemblies;
        }

        internal sealed override ImmutableArray<AssemblySymbol> GetLinkedReferencedAssemblies()
        {
            return _linkedReferencedAssemblies;
        }

        internal sealed override bool AreInternalsVisibleToThisAssembly(AssemblySymbol potentialGiverOfAccess)
        {
            IVTConclusion conclusion = MakeFinalIVTDetermination(potentialGiverOfAccess);
            return conclusion == IVTConclusion.Match || conclusion == IVTConclusion.OneSignedOneNot;
        }

        internal DocumentationProvider DocumentationProvider => _documentationProvider;

        internal sealed override bool IsLinked => _isLinked;

        // While the specification for ExtensionAttribute requires that the containing assembly
        // have the attribute if any type in the assembly has the attribute, some compilers do
        // not properly follow that spec. Therefore we pessimistically assume every assembly
        // may contain extension methods.
        public sealed override bool MightContainExtensionMethods => true;

        internal PEModuleSymbol PrimaryModule => (PEModuleSymbol)Modules[0];

        // perf, not correctness
        internal sealed override CSharpCompilation DeclaringCompilation => null;
    }
}
