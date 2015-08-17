// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.CSharp.DocumentationComments;
using Microsoft.CodeAnalysis.CSharp.Emit;
using Roslyn.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    internal abstract class MetadataFieldSymbol : FieldSymbol
    {
        private readonly NamedTypeSymbol _containingType;

        private ImmutableArray<CSharpAttributeData> _lazyCustomAttributes;
        private ConstantValue _lazyConstantValue = CodeAnalysis.ConstantValue.Unset; // Indicates an uninitialized ConstantValue
        private Tuple<CultureInfo, string> _lazyDocComment;
        protected DiagnosticInfo _lazyUseSiteDiagnostic = CSDiagnosticInfo.EmptyErrorInfo; // Indicates unknown state. 

        // signature:
        protected TypeSymbol _lazyType;
        protected bool _lazyIsVolatile;
        protected ImmutableArray<CustomModifier> _lazyCustomModifiers;
        protected int _lazyFixedSize;
        protected NamedTypeSymbol _lazyFixedImplementationType;

        private EventSymbol _associatedEventOpt;

        public MetadataFieldSymbol(NamedTypeSymbol containingType)
        {
            Debug.Assert((object)containingType != null);            
            _containingType = containingType;
        }

        public sealed override Symbol ContainingSymbol => _containingType;
        public sealed override NamedTypeSymbol ContainingType => _containingType;
        internal MetadataModuleSymbol ContainingMetadataModule => (MetadataModuleSymbol)ContainingModule;

        // the compiler doesn't need full marshalling information, just the unmanaged type or descriptor
        internal sealed override MarshalPseudoCustomAttributeData MarshallingInformation => null;

        /// <summary>
        /// Mark this field as the backing field of a field-like event.
        /// The caller will also ensure that it is excluded from the member list of
        /// the containing type (as it would be in source).
        /// </summary>
        internal void SetAssociatedEvent(EventSymbol eventSymbol)
        {
            Debug.Assert((object)eventSymbol != null);
            Debug.Assert(eventSymbol.ContainingType == ContainingType);

            // This should always be true in valid metadata - there should only
            // be one event with a given name in a given type.
            if ((object)_associatedEventOpt == null)
            {
                // No locking required since this method will only be called by the thread that created
                // the field symbol (and will be called before the field symbol is added to the containing 
                // type members and available to other threads).
                _associatedEventOpt = eventSymbol;
            }
        }

        public sealed override Symbol AssociatedSymbol => _associatedEventOpt;

        public sealed override bool IsVolatile
        {
            get
            {
                EnsureSignatureIsLoaded();
                return _lazyIsVolatile;
            }
        }

        public sealed override ImmutableArray<Location> Locations =>
            ContainingMetadataModule.MetadataLocation.Cast<MetadataLocation, Location>();

        protected void EnsureSignatureIsLoaded()
        {
            if ((object)_lazyType == null)
            {
                LoadSignature();
            }
        }

        protected abstract void LoadSignature();

        internal static Accessibility GetDeclaredAccessibility(FieldAttributes flags)
        {
            switch (flags & FieldAttributes.FieldAccessMask)
            {
                case FieldAttributes.Assembly:
                    return Accessibility.Internal;

                case FieldAttributes.FamORAssem:
                    return Accessibility.ProtectedOrInternal;

                case FieldAttributes.FamANDAssem:
                    return Accessibility.ProtectedAndInternal;

                case FieldAttributes.Private:
                case FieldAttributes.PrivateScope:
                    return Accessibility.Private;

                case FieldAttributes.Public:
                    return Accessibility.Public;

                case FieldAttributes.Family:
                    return Accessibility.Protected;

                default:
                    throw ExceptionUtilities.UnexpectedValue(flags & FieldAttributes.FieldAccessMask);
            }
        }

        internal sealed override TypeSymbol GetFieldType(ConsList<FieldSymbol> fieldsBeingBound)
        {
            EnsureSignatureIsLoaded();
            return _lazyType;
        }

        public sealed override bool IsFixed
        {
            get
            {
                EnsureSignatureIsLoaded();
                return (object)_lazyFixedImplementationType != null;
            }
        }

        public sealed override int FixedSize
        {
            get
            {
                EnsureSignatureIsLoaded();
                return _lazyFixedSize;
            }
        }

        internal sealed override NamedTypeSymbol FixedImplementationType(PEModuleBuilder emitModule)
        {
            EnsureSignatureIsLoaded();
            return _lazyFixedImplementationType;
        }

        public sealed override ImmutableArray<CustomModifier> CustomModifiers
        {
            get
            {
                EnsureSignatureIsLoaded();
                return _lazyCustomModifiers;
            }
        }

        internal sealed override ConstantValue GetConstantValue(ConstantFieldsInProgress inProgress, bool earlyDecodingWellKnownAttributes)
        {
            if (_lazyConstantValue == Microsoft.CodeAnalysis.ConstantValue.Unset)
            {
                Interlocked.CompareExchange(ref _lazyConstantValue, DecodeConstantValue(), Microsoft.CodeAnalysis.ConstantValue.Unset);
            }

            return _lazyConstantValue;
        }

        protected abstract ConstantValue DecodeConstantValue();

        public sealed override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => ImmutableArray<SyntaxReference>.Empty;

        private bool OmitDecimalConstantAttribute()
        {
            ConstantValue value;
            return this.Type.SpecialType == SpecialType.System_Decimal &&
                   (object)(value = GetConstantValue(ConstantFieldsInProgress.Empty, earlyDecodingWellKnownAttributes: false)) != null &&
                   value.Discriminator == ConstantValueTypeDiscriminator.Decimal;
        }

        public sealed override ImmutableArray<CSharpAttributeData> GetAttributes()
        {
            if (_lazyCustomAttributes.IsDefault)
            {
                LoadCustomAttributes(ref _lazyCustomAttributes, OmitDecimalConstantAttribute());
            }

            return _lazyCustomAttributes;
        }

        protected abstract void LoadCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes, bool omitDecimalConstantAttribute);

        internal sealed override IEnumerable<CSharpAttributeData> GetCustomAttributesToEmit(ModuleCompilationState compilationState)
        {
            foreach (CSharpAttributeData attribute in GetAttributes())
            {
                yield return attribute;
            }

            // Yield hidden attributes last, order might be important.
            if (OmitDecimalConstantAttribute())
            {
                yield return CreateDecimalConstantAttributeData();
            }
        }

        protected abstract CSharpAttributeData CreateDecimalConstantAttributeData();

        public sealed override string GetDocumentationCommentXml(CultureInfo preferredCulture = null, bool expandIncludes = false, CancellationToken cancellationToken = default(CancellationToken))
        {
            return PEDocumentationCommentUtils.GetDocumentationComment(this, ContainingMetadataModule, preferredCulture, cancellationToken, ref _lazyDocComment);
        }

        internal sealed override DiagnosticInfo GetUseSiteDiagnostic()
        {
            if (ReferenceEquals(_lazyUseSiteDiagnostic, CSDiagnosticInfo.EmptyErrorInfo))
            {
                DiagnosticInfo result = null;
                CalculateUseSiteDiagnostic(ref result);
                _lazyUseSiteDiagnostic = result;
            }

            return _lazyUseSiteDiagnostic;
        }

        // perf, not correctness
        internal sealed override CSharpCompilation DeclaringCompilation => null;
    }
}
