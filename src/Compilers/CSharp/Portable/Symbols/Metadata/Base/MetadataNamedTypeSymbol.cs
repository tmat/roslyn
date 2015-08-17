// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Threading;
using Roslyn.Utilities;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Linq;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.CSharp.DocumentationComments;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    internal abstract class MetadataNamedTypeSymbol<TTypeDefinitionHandle, TNamedTypeSymbol, TFieldSymbol> : NamedTypeSymbol
        where TNamedTypeSymbol : MetadataNamedTypeSymbol<TTypeDefinitionHandle, TNamedTypeSymbol, TFieldSymbol>
    {
        private static readonly Dictionary<string, ImmutableArray<TNamedTypeSymbol>> s_emptyNestedTypes = new Dictionary<string, ImmutableArray<TNamedTypeSymbol>>();

        private readonly NamespaceOrTypeSymbol _container;
        private readonly string _name;
        private readonly SpecialType _specialType;

        internal readonly TypeAttributes Flags;

        /// <summary>
        /// A set of all the names of the members in this type.
        /// We can get names without getting members (which is a more expensive operation)
        /// </summary>
        private ICollection<string> _lazyMemberNames;

        /// <summary>
        /// We used to sort symbols on demand and relied on row ids to figure out the order between symbols of the same kind.
        /// However, that was fragile because, when map tables are used in metadata, row ids in the map table define the order
        /// and we don't have them.
        /// Members are grouped by kind. First we store fields, then methods, then properties, then events and finally nested types.
        /// Within groups, members are sorted based on declaration order.
        /// </summary>
        private ImmutableArray<Symbol> _lazyMembersInDeclarationOrder;

        /// <summary>
        /// A map of members immediately contained within this type 
        /// grouped by their name (case-sensitively).
        /// </summary>
        private Dictionary<string, ImmutableArray<Symbol>> _lazyMembersByName;

        /// <summary>
        /// A map of types immediately contained within this type 
        /// grouped by their name (case-sensitively).
        /// </summary>
        private Dictionary<string, ImmutableArray<TNamedTypeSymbol>> _lazyNestedTypes;

        /// <summary>
        /// Lazily initialized by TypeKind property.
        /// </summary>
        private TypeKind _lazyKind;

        private NamedTypeSymbol _lazyBaseType = ErrorTypeSymbol.UnknownResultType;
        private ImmutableArray<NamedTypeSymbol> _lazyInterfaces = default(ImmutableArray<NamedTypeSymbol>);
        private NamedTypeSymbol _lazyDeclaredBaseType = ErrorTypeSymbol.UnknownResultType;
        private ImmutableArray<NamedTypeSymbol> _lazyDeclaredInterfaces = default(ImmutableArray<NamedTypeSymbol>);

        private Tuple<CultureInfo, string> _lazyDocComment;

        protected DiagnosticInfo _lazyUseSiteDiagnostic = CSDiagnosticInfo.EmptyErrorInfo; // Indicates unknown state. 

        #region Uncommon properties

        // There is a bunch of type properties relevant only for enums or types with custom attributes.
        // It is fairly easy to check whether a type s is not "uncommon". So we store uncommon properties in 
        // a separate class with a noUncommonProperties singleton used for cases when type is "common".
        // this is done purely to save memory with expectation that "uncommon" cases are indeed uncommon. 

        private static readonly UncommonProperties s_noUncommonProperties = new UncommonProperties();
        private UncommonProperties _lazyUncommonProperties;

        private UncommonProperties GetUncommonProperties()
        {
            var result = _lazyUncommonProperties;
            if (result != null)
            {
                Debug.Assert(result != s_noUncommonProperties || result.IsDefaultValue(), "default value was modified");
                return result;
            }

            if (this.IsUncommon())
            {
                result = new UncommonProperties();
                return Interlocked.CompareExchange(ref _lazyUncommonProperties, result, null) ?? result;
            }

            _lazyUncommonProperties = result = s_noUncommonProperties;
            return result;
        }

        // enums and types with custom attributes are considered uncommon
        private bool IsUncommon()
        {
            return HasAnyCustomAttributes() || TypeKind == TypeKind.Enum;
        }

        private class UncommonProperties
        {
            /// <summary>
            /// Need to import them for an enum from a linked assembly, when we are embedding it. These symbols are not included into lazyMembersInDeclarationOrder.  
            /// </summary>
            internal ImmutableArray<TFieldSymbol> lazyInstanceEnumFields;
            internal NamedTypeSymbol lazyEnumUnderlyingType;

            // CONSIDER: Should we use a CustomAttributeBag for PE symbols?
            internal ImmutableArray<CSharpAttributeData> lazyCustomAttributes;
            internal ImmutableArray<string> lazyConditionalAttributeSymbols;
            internal ObsoleteAttributeData lazyObsoleteAttributeData = ObsoleteAttributeData.Uninitialized;
            internal AttributeUsageInfo lazyAttributeUsageInfo = AttributeUsageInfo.Null;
            internal ThreeState lazyContainsExtensionMethods;
            internal string lazyDefaultMemberName;
            internal NamedTypeSymbol lazyComImportCoClassType = ErrorTypeSymbol.UnknownResultType;

            internal bool IsDefaultValue()
            {
                return lazyInstanceEnumFields.IsDefault &&
                    (object)lazyEnumUnderlyingType == null &&
                    lazyCustomAttributes.IsDefault &&
                    lazyConditionalAttributeSymbols.IsDefault &&
                    lazyObsoleteAttributeData == ObsoleteAttributeData.Uninitialized &&
                    lazyAttributeUsageInfo.IsNull &&
                    !lazyContainsExtensionMethods.HasValue() &&
                    lazyDefaultMemberName == null &&
                    (object)lazyComImportCoClassType == (object)ErrorTypeSymbol.UnknownResultType;
            }
        }

        #endregion

        protected MetadataNamedTypeSymbol(
            NamespaceOrTypeSymbol container,
            string emittedNamespaceName,
            string metadataNameOpt,
            TypeAttributes? flags,
            ushort arity,
            out bool mangleName)
        {
            Debug.Assert((object)container != null);
            Debug.Assert(emittedNamespaceName != null);
            
            Flags = flags ?? default(TypeAttributes);
            _container = container;
            _name = GetNameFromMetadataName(metadataNameOpt, arity, out mangleName);

            // check if this is one of the COR library types
            if (emittedNamespaceName != null &&
                container.ContainingAssembly.KeepLookingForDeclaredSpecialTypes &&
                GetDeclaredAccessibility(Flags) == Accessibility.Public)
            {
                _specialType = SpecialTypes.GetTypeFromMetadataName(MetadataHelpers.BuildQualifiedName(emittedNamespaceName, metadataNameOpt));
            }

            if (flags == null || metadataNameOpt == null)
            {
                _lazyUseSiteDiagnostic = new CSDiagnosticInfo(ErrorCode.ERR_BogusType, this);
            }
        }

        private static string GetNameFromMetadataName(string metadataNameOpt, ushort arity, out bool mangleName)
        {
            if (metadataNameOpt != null)
            {
                string name;

                if (arity == 0)
                {
                    name = metadataNameOpt;
                    mangleName = false;
                }
                else
                {
                    // Unmangle name for a generic type.
                    name = MetadataHelpers.UnmangleMetadataNameForArity(metadataNameOpt, arity);
                    Debug.Assert(ReferenceEquals(name, metadataNameOpt) == (name == metadataNameOpt));
                    mangleName = !ReferenceEquals(name, metadataNameOpt);
                }

                return name;
            }
            else
            {
                mangleName = false;
                return string.Empty;
            }
        }

        internal MetadataModuleSymbol ContainingMetadataModule => (MetadataModuleSymbol)ContainingModule;

        public override Accessibility DeclaredAccessibility => GetDeclaredAccessibility(Flags);
        public override SpecialType SpecialType => _specialType;

        protected bool IsOrdinaryStruct => TypeKind == TypeKind.Struct && SpecialType == SpecialType.None;
        protected bool IsOrdinaryEmbeddableStruct => IsOrdinaryStruct && ContainingAssembly.IsLinked;

        internal static Accessibility GetDeclaredAccessibility(TypeAttributes attributes)
        {
            Accessibility access = Accessibility.Private;

            switch (attributes & TypeAttributes.VisibilityMask)
            {
                case TypeAttributes.NestedAssembly:
                    access = Accessibility.Internal;
                    break;

                case TypeAttributes.NestedFamORAssem:
                    access = Accessibility.ProtectedOrInternal;
                    break;

                case TypeAttributes.NestedFamANDAssem:
                    access = Accessibility.ProtectedAndInternal;
                    break;

                case TypeAttributes.NestedPrivate:
                    access = Accessibility.Private;
                    break;

                case TypeAttributes.Public:
                case TypeAttributes.NestedPublic:
                    access = Accessibility.Public;
                    break;

                case TypeAttributes.NestedFamily:
                    access = Accessibility.Protected;
                    break;

                case TypeAttributes.NotPublic:
                    access = Accessibility.Internal;
                    break;

                default:
                    throw ExceptionUtilities.UnexpectedValue(attributes & TypeAttributes.VisibilityMask);
            }

            return access;
        }

        internal sealed override ModuleSymbol ContainingModule
        {
            get
            {
                Symbol symbol = _container;

                while (symbol.Kind != SymbolKind.Namespace)
                {
                    symbol = symbol.ContainingSymbol;
                }

                return ((NamespaceSymbol)symbol).ContainingModule;
            }
        }

        public abstract override int Arity { get; }
        internal abstract override bool MangleName { get; }
        internal abstract int MetadataArity { get; }
        public override NamedTypeSymbol ConstructedFrom => this;
        public override Symbol ContainingSymbol => _container;
        public override NamedTypeSymbol ContainingType => _container as NamedTypeSymbol;

        internal override NamedTypeSymbol BaseTypeNoUseSiteDiagnostics
        {
            get
            {
                if (ReferenceEquals(_lazyBaseType, ErrorTypeSymbol.UnknownResultType))
                {
                    Interlocked.CompareExchange(ref _lazyBaseType, MakeAcyclicBaseType(), ErrorTypeSymbol.UnknownResultType);
                }

                return _lazyBaseType;
            }
        }

        internal override ImmutableArray<NamedTypeSymbol> InterfacesNoUseSiteDiagnostics(ConsList<Symbol> basesBeingResolved = null)
        {
            if (_lazyInterfaces.IsDefault)
            {
                ImmutableInterlocked.InterlockedCompareExchange(ref _lazyInterfaces, MakeAcyclicInterfaces(), default(ImmutableArray<NamedTypeSymbol>));
            }

            return _lazyInterfaces;
        }

        internal override ImmutableArray<NamedTypeSymbol> GetInterfacesToEmit()
        {
            return InterfacesNoUseSiteDiagnostics();
        }

        internal override NamedTypeSymbol GetDeclaredBaseType(ConsList<Symbol> basesBeingResolved)
        {
            if (ReferenceEquals(_lazyDeclaredBaseType, ErrorTypeSymbol.UnknownResultType))
            {
                Interlocked.CompareExchange(ref _lazyDeclaredBaseType, MakeDeclaredBaseType(), ErrorTypeSymbol.UnknownResultType);
            }

            return _lazyDeclaredBaseType;
        }

        internal override ImmutableArray<NamedTypeSymbol> GetDeclaredInterfaces(ConsList<Symbol> basesBeingResolved)
        {
            if (_lazyDeclaredInterfaces.IsDefault)
            {
                ImmutableInterlocked.InterlockedCompareExchange(ref _lazyDeclaredInterfaces, MakeDeclaredInterfaces(), default(ImmutableArray<NamedTypeSymbol>));
            }

            return _lazyDeclaredInterfaces;
        }

        protected abstract NamedTypeSymbol MakeDeclaredBaseType();
        protected abstract ImmutableArray<NamedTypeSymbol> MakeDeclaredInterfaces();

        public sealed override NamedTypeSymbol EnumUnderlyingType
        {
            get
            {
                var uncommon = GetUncommonProperties();
                if (uncommon == s_noUncommonProperties)
                {
                    return null;
                }

                this.EnsureEnumUnderlyingTypeIsLoaded(uncommon);
                return uncommon.lazyEnumUnderlyingType;
            }
        }

        public sealed override ImmutableArray<CSharpAttributeData> GetAttributes()
        {
            var uncommon = GetUncommonProperties();
            if (uncommon == s_noUncommonProperties)
            {
                return ImmutableArray<CSharpAttributeData>.Empty;
            }

            if (uncommon.lazyCustomAttributes.IsDefault)
            {
                LoadCustomAttributes(ref uncommon.lazyCustomAttributes);
            }

            return uncommon.lazyCustomAttributes;
        }

        protected abstract bool HasExtensionAttribute();
        protected abstract bool HasAnyCustomAttributes();
        protected abstract void LoadCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes);
        protected abstract void LoadObsoleteAttributeData(ref ObsoleteAttributeData lazyObsoleteAttributeData);

        internal override IEnumerable<CSharpAttributeData> GetCustomAttributesToEmit(ModuleCompilationState compilationState)
        {
            return GetAttributes();
        }

        public override IEnumerable<string> MemberNames
        {
            get
            {
                EnsureNonTypeMemberNamesAreLoaded();
                return _lazyMemberNames;
            }
        }

        private void EnsureNonTypeMemberNamesAreLoaded()
        {
            if (_lazyMemberNames == null)
            {
                HashSet<string> names = LoadNonTypeMemberNames();

                // From C#'s perspective, structs always have a public constructor
                // (even if it's not in metadata).  Add it unconditionally and let
                // the hash set de-dup.
                if (this.IsValueType)
                {
                    names.Add(WellKnownMemberNames.InstanceConstructorName);
                }

                Interlocked.CompareExchange(ref _lazyMemberNames, CreateReadOnlyMemberNames(names), null);
            }
        }

        protected abstract HashSet<string> LoadNonTypeMemberNames();

        private static ICollection<string> CreateReadOnlyMemberNames(HashSet<string> names)
        {
            switch (names.Count)
            {
                case 0:
                    return SpecializedCollections.EmptySet<string>();

                case 1:
                    return SpecializedCollections.SingletonCollection(names.First());

                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                    // PERF: Small collections can be implemented as ImmutableArray.
                    // While lookup is O(n), when n is small, the memory savings are more valuable.
                    // Size 6 was chosen because that represented 50% of the names generated in the Picasso end to end.
                    // This causes boxing, but that's still superior to a wrapped HashSet
                    return ImmutableArray.CreateRange(names);

                default:
                    return SpecializedCollections.ReadOnlySet(names);
            }
        }

        internal override ImmutableArray<Symbol> GetMembersUnordered()
        {
            ImmutableArray<Symbol> result = GetMembers();
#if DEBUG
            // In DEBUG, swap first and last elements so that use of Unordered in a place it isn't warranted is caught
            // more obviously.
            return result.DeOrder();
#else
            return result;
#endif
        }

        public override ImmutableArray<Symbol> GetMembers()
        {
            EnsureAllMembersAreLoaded();
            return _lazyMembersInDeclarationOrder;
        }

        private ImmutableArray<TFieldSymbol> GetInstanceEnumFields()
        {
            var uncommon = GetUncommonProperties();
            if (uncommon == s_noUncommonProperties)
            {
                return ImmutableArray<TFieldSymbol>.Empty;
            }

            if (uncommon.lazyInstanceEnumFields.IsDefault)
            {
                var builder = ArrayBuilder<TFieldSymbol>.GetInstance();
                PopulateEnumInstanceFields(builder);
                ImmutableInterlocked.InterlockedInitialize(ref uncommon.lazyInstanceEnumFields, builder.ToImmutableAndFree());
            }

            return uncommon.lazyInstanceEnumFields;
        }

        protected abstract void PopulateEnumInstanceFields(ArrayBuilder<TFieldSymbol> fields);

        protected abstract IEnumerable<FieldSymbol> MergeEnumFieldsToEmit(ImmutableArray<Symbol> allMembers, ImmutableArray<TFieldSymbol> instanceEnumFields);

        internal sealed override IEnumerable<FieldSymbol> GetFieldsToEmit()
        {
            if (this.TypeKind == TypeKind.Enum)
            {
                var instanceEnumFields = GetInstanceEnumFields();
                if (instanceEnumFields.IsEmpty)
                {
                    return SpecializedCollections.EmptyEnumerable<FieldSymbol>();
                }

                return MergeEnumFieldsToEmit(GetMembers(), instanceEnumFields);
            }
            else
            {
                return GetNonEnumFieldsToEmit();
            }
        }

        private IEnumerable<FieldSymbol> GetNonEnumFieldsToEmit()
        {
            IEnumerable<FieldSymbol> nonEventFields = GetNonEventFieldsToEmit();

            ArrayBuilder<FieldSymbol> eventFields = GetEventFieldsToEmit();
            if (eventFields == null)
            {
                // Simple case
                return nonEventFields;
            }

            Debug.Assert(eventFields.All(f => !nonEventFields.Contains(f)));

            // Event backing fields are not part of the set returned by GetMembers. Let's add them manually.
            return MergeNonEnumFieldsToEmit(nonEventFields, eventFields);
        }

        private IEnumerable<FieldSymbol> GetNonEventFieldsToEmit()
        {
            // If there are any non-event fields, they are at the very beginning.
            return GetMembers<FieldSymbol>(this.GetMembers(), SymbolKind.Field, offset: 0);
        }

        private ArrayBuilder<FieldSymbol> GetEventFieldsToEmit()
        {
            ArrayBuilder<FieldSymbol> eventFields = null;

            foreach (var eventSymbol in GetEventsToEmit())
            {
                FieldSymbol associatedField = eventSymbol.AssociatedField;
                if ((object)associatedField != null)
                {
                    Debug.Assert((object)associatedField.AssociatedSymbol != null);

                    if (eventFields == null)
                    {
                        eventFields = ArrayBuilder<FieldSymbol>.GetInstance();
                    }

                    eventFields.Add(associatedField);
                }
            }

            return eventFields;
        }

        protected abstract IEnumerable<FieldSymbol> MergeNonEnumFieldsToEmit(IEnumerable<FieldSymbol> nonEventFields, ArrayBuilder<FieldSymbol> eventFields);

        internal sealed override IEnumerable<PropertySymbol> GetPropertiesToEmit()
        {
            return GetMembers<PropertySymbol>(this.GetMembers(), SymbolKind.Property);
        }

        internal sealed override IEnumerable<EventSymbol> GetEventsToEmit()
        {
            return GetMembers<EventSymbol>(this.GetMembers(), SymbolKind.Event);
        }

        internal sealed override ImmutableArray<Symbol> GetEarlyAttributeDecodingMembers()
        {
            return this.GetMembersUnordered();
        }

        internal sealed override ImmutableArray<Symbol> GetEarlyAttributeDecodingMembers(string name)
        {
            return this.GetMembers(name);
        }

        private void EnsureEnumUnderlyingTypeIsLoaded(UncommonProperties uncommon)
        {
            if ((object)uncommon.lazyEnumUnderlyingType == null && TypeKind == TypeKind.Enum)
            {
                Interlocked.CompareExchange(ref uncommon.lazyEnumUnderlyingType, DecodeEnumUnderlyingType(), null);
            }
        }

        protected abstract NamedTypeSymbol DecodeEnumUnderlyingType();

        private void EnsureAllMembersAreLoaded()
        {
            if (_lazyMembersByName == null)
            {
                LoadMembers();
            }
        }

        private void LoadMembers()
        {
            ArrayBuilder<Symbol> members = null;

            if (_lazyMembersInDeclarationOrder.IsDefault)
            {
                members = ArrayBuilder<Symbol>.GetInstance();

                EnsureNestedTypesAreLoaded();
                EnsureEnumUnderlyingTypeIsLoaded(GetUncommonProperties());

                PopulateMembersInDeclarationOrder(members);

                // Now add types to the end.
                int membersCount = members.Count;

                foreach (var typeArray in _lazyNestedTypes.Values)
                {
                    members.AddRange(typeArray);
                }

                // Sort the types based on row id.
                members.Sort(membersCount, DeclarationOrderComparer);

                var membersInDeclarationOrder = members.ToImmutable();

#if DEBUG
                ISymbol previous = null;

                foreach (var s in membersInDeclarationOrder)
                {
                    if (previous == null)
                    {
                        previous = s;
                    }
                    else
                    {
                        ISymbol current = s;
                        Debug.Assert(previous.Kind.ToSortOrder() <= current.Kind.ToSortOrder());
                        previous = current;
                    }
                }
#endif

                if (!ImmutableInterlocked.InterlockedInitialize(ref _lazyMembersInDeclarationOrder, membersInDeclarationOrder))
                {
                    members.Free();
                    members = null;
                }
                else
                {
                    // remove the types
                    members.Clip(membersCount);
                }
            }

            if (_lazyMembersByName == null)
            {
                if (members == null)
                {
                    members = ArrayBuilder<Symbol>.GetInstance();
                    foreach (var member in _lazyMembersInDeclarationOrder)
                    {
                        if (member.Kind == SymbolKind.NamedType)
                        {
                            break;
                        }
                        members.Add(member);
                    }
                }

                Dictionary<string, ImmutableArray<Symbol>> membersDict = GroupByName(members);

                var exchangeResult = Interlocked.CompareExchange(ref _lazyMembersByName, membersDict, null);
                if (exchangeResult == null)
                {
                    // we successfully swapped in the members dictionary.

                    // Now, use these as the canonical member names.  This saves us memory by not having
                    // two collections around at the same time with redundant data in them.
                    //
                    // NOTE(cyrusn): We must use an interlocked exchange here so that the full
                    // construction of this object will be seen from 'MemberNames'.  Also, doing a
                    // straight InterlockedExchange here is the right thing to do.  Consider the case
                    // where one thread is calling in through "MemberNames" while we are in the middle
                    // of this method.  Either that thread will compute the member names and store it
                    // first (in which case we overwrite it), or we will store first (in which case
                    // their CompareExchange(..., ..., null) will fail.  Either way, this will be certain
                    // to become the canonical set of member names.
                    //
                    // NOTE(cyrusn): This means that it is possible (and by design) for people to get a
                    // different object back when they call MemberNames multiple times.  However, outside
                    // of object identity, both collections should appear identical to the user.
                    var memberNames = SpecializedCollections.ReadOnlyCollection(membersDict.Keys);
                    Interlocked.Exchange(ref _lazyMemberNames, memberNames);
                }
            }

            if (members != null)
            {
                members.Free();
            }
        }

        protected abstract IComparer<Symbol> DeclarationOrderComparer { get; }
        protected abstract void PopulateMembersInDeclarationOrder(ArrayBuilder<Symbol> members);

        internal sealed override ImmutableArray<Symbol> GetSimpleNonTypeMembers(string name)
        {
            EnsureAllMembersAreLoaded();

            ImmutableArray<Symbol> members;
            if (!_lazyMembersByName.TryGetValue(name, out members))
            {
                members = ImmutableArray<Symbol>.Empty;
            }

            return members;
        }

        public sealed override ImmutableArray<Symbol> GetMembers(string name)
        {
            EnsureAllMembersAreLoaded();

            ImmutableArray<Symbol> members;
            if (!_lazyMembersByName.TryGetValue(name, out members))
            {
                members = ImmutableArray<Symbol>.Empty;
            }

            // nested types are not common, but we need to check just in case
            ImmutableArray<TNamedTypeSymbol> types;
            if (_lazyNestedTypes.TryGetValue(name, out types))
            {
                members = members.Concat(StaticCast<Symbol>.From(types));
            }

            return members;
        }

        internal sealed override FieldSymbol FixedElementField
        {
            get
            {
                FieldSymbol result = null;

                var candidates = this.GetMembers(FixedFieldImplementationType.FixedElementFieldName);
                if (!candidates.IsDefault && candidates.Length == 1)
                {
                    result = candidates[0] as FieldSymbol;
                }

                return result;
            }
        }

        internal sealed override ImmutableArray<NamedTypeSymbol> GetTypeMembersUnordered()
        {
            ImmutableArray<NamedTypeSymbol> result = GetTypeMembers();
#if DEBUG
            // In DEBUG, swap first and last elements so that use of Unordered in a place it isn't warranted is caught
            // more obviously.
            return result.DeOrder();
#else
            return result;
#endif
        }

        public sealed override ImmutableArray<NamedTypeSymbol> GetTypeMembers()
        {
            EnsureNestedTypesAreLoaded();
            return GetMemberTypesPrivate();
        }

        private ImmutableArray<NamedTypeSymbol> GetMemberTypesPrivate()
        {
            var builder = ArrayBuilder<NamedTypeSymbol>.GetInstance();
            foreach (var typeArray in _lazyNestedTypes.Values)
            {
                builder.AddRange(typeArray);
            }

            return builder.ToImmutableAndFree();
        }

        private void EnsureNestedTypesAreLoaded()
        {
            if (_lazyNestedTypes == null)
            {
                var types = ArrayBuilder<TNamedTypeSymbol>.GetInstance();
                types.AddRange(this.CreateNestedTypes());
                var typesDict = GroupByName(types);

                var exchangeResult = Interlocked.CompareExchange(ref _lazyNestedTypes, typesDict, null);
                if (exchangeResult == null)
                {
                    // Build cache of TypeDef Tokens
                    // Potentially this can be done in the background.
                    OnContainedNamedTypesCreated(typesDict);
                }

                types.Free();
            }
        }

        protected abstract void OnContainedNamedTypesCreated(Dictionary<string, ImmutableArray<TNamedTypeSymbol>> types);

        public sealed override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name)
        {
            EnsureNestedTypesAreLoaded();

            ImmutableArray<TNamedTypeSymbol> type;

            if (_lazyNestedTypes.TryGetValue(name, out type))
            {
                return StaticCast<NamedTypeSymbol>.From(type);
            }

            return ImmutableArray<NamedTypeSymbol>.Empty;
        }

        public sealed override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
        {
            return GetTypeMembers(name).WhereAsArray(type => type.Arity == arity);
        }

        public sealed override ImmutableArray<Location> Locations =>
            ContainingMetadataModule.MetadataLocation.Cast<MetadataLocation, Location>();

        public sealed override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => ImmutableArray<SyntaxReference>.Empty;

        public sealed override string Name => _name;
        internal override ImmutableArray<TypeSymbol> TypeArgumentsNoUseSiteDiagnostics => ImmutableArray<TypeSymbol>.Empty;
        public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;

        internal sealed override bool HasSpecialName => (Flags & TypeAttributes.SpecialName) != 0;
        public sealed override bool IsStatic => (Flags & TypeAttributes.Sealed) != 0 && (Flags & TypeAttributes.Abstract) != 0;
        public sealed override bool IsAbstract => (Flags & TypeAttributes.Abstract) != 0 && (Flags & TypeAttributes.Sealed) == 0;
        internal sealed override bool IsMetadataAbstract => (Flags & TypeAttributes.Abstract) != 0;
        public sealed override bool IsSealed => (Flags & TypeAttributes.Sealed) != 0 && (Flags & TypeAttributes.Abstract) == 0;
        internal sealed override bool IsMetadataSealed => (Flags & TypeAttributes.Sealed) != 0;

        public sealed override bool MightContainExtensionMethods
        {
            get
            {
                var uncommon = GetUncommonProperties();
                if (uncommon == s_noUncommonProperties)
                {
                    return false;
                }

                if (!uncommon.lazyContainsExtensionMethods.HasValue())
                {
                    var contains = ThreeState.False;
                    // Dev11 supports extension methods defined on non-static
                    // classes, structs, delegates, and generic types.
                    switch (this.TypeKind)
                    {
                        case TypeKind.Class:
                        case TypeKind.Struct:
                        case TypeKind.Delegate:
                            bool moduleHasExtension = HasExtensionAttribute();

                            var containingAssembly = this.ContainingAssembly as MetadataAssemblySymbol;
                            if ((object)containingAssembly != null)
                            {
                                contains = (moduleHasExtension && containingAssembly.MightContainExtensionMethods).ToThreeState();
                            }
                            else
                            {
                                contains = moduleHasExtension.ToThreeState();
                            }
                            break;
                    }

                    uncommon.lazyContainsExtensionMethods = contains;
                }

                return uncommon.lazyContainsExtensionMethods.Value();
            }
        }

        public sealed override TypeKind TypeKind
        {
            get
            {
                if (_lazyKind == TypeKind.Unknown)
                {
                    _lazyKind = CalculateTypeKind();
                }

                return _lazyKind;
            }
        }

        private TypeKind CalculateTypeKind()
        {
            if (Flags.IsInterface())
            {
                return TypeKind.Interface;
            }

            TypeSymbol @base = GetDeclaredBaseType(null);
            if ((object)@base != null)
            {
                SpecialType baseCorTypeId = @base.SpecialType;

                // Code is cloned from MetaImport::DoImportBaseAndImplements()
                if (baseCorTypeId == SpecialType.System_Enum)
                {
                    // Enum
                    return TypeKind.Enum;
                }

                if (baseCorTypeId == SpecialType.System_MulticastDelegate)
                {
                    // Delegate
                    return TypeKind.Delegate;
                }

                if (baseCorTypeId == SpecialType.System_ValueType && SpecialType != SpecialType.System_Enum)
                {
                    // Struct
                    return TypeKind.Struct;
                }
            }

            return TypeKind.Class;
        }

        private NamedTypeSymbol MakeAcyclicBaseType()
        {
            NamedTypeSymbol declaredBase = GetDeclaredBaseType(null);

            // implicit base is not interesting for metadata cycle detection
            if ((object)declaredBase == null)
            {
                return null;
            }

            if (BaseTypeAnalysis.ClassDependsOn(declaredBase, this))
            {
                return CyclicInheritanceError(this, declaredBase);
            }

            this.SetKnownToHaveNoDeclaredBaseCycles();
            return declaredBase;
        }

        private ImmutableArray<NamedTypeSymbol> MakeAcyclicInterfaces()
        {
            var declaredInterfaces = GetDeclaredInterfaces(null);
            if (!IsInterface)
            {
                // only interfaces needs to check for inheritance cycles via interfaces.
                return declaredInterfaces;
            }

            return declaredInterfaces
                .SelectAsArray(t => BaseTypeAnalysis.InterfaceDependsOn(t, this) ? CyclicInheritanceError(this, t) : t);
        }

        private static ExtendedErrorTypeSymbol CyclicInheritanceError(NamedTypeSymbol type, TypeSymbol declaredBase)
        {
            var info = new CSDiagnosticInfo(ErrorCode.ERR_ImportedCircularBase, declaredBase, type);
            return new ExtendedErrorTypeSymbol(declaredBase, LookupResultKind.NotReferencable, info, true);
        }

        public sealed override string GetDocumentationCommentXml(CultureInfo preferredCulture = null, bool expandIncludes = false, CancellationToken cancellationToken = default(CancellationToken))
        {
            return PEDocumentationCommentUtils.GetDocumentationComment(this, ContainingMetadataModule, preferredCulture, cancellationToken, ref _lazyDocComment);
        }

        protected abstract IEnumerable<TNamedTypeSymbol> CreateNestedTypes();

        private static Dictionary<string, ImmutableArray<Symbol>> GroupByName(ArrayBuilder<Symbol> symbols)
        {
            return symbols.ToDictionary(s => s.Name);
        }

        private static Dictionary<string, ImmutableArray<TNamedTypeSymbol>> GroupByName(ArrayBuilder<TNamedTypeSymbol> symbols)
        {
            if (symbols.Count == 0)
            {
                return s_emptyNestedTypes;
            }

            return symbols.ToDictionary(s => s.Name);
        }
        
        internal sealed override DiagnosticInfo GetUseSiteDiagnostic()
        {
            if (ReferenceEquals(_lazyUseSiteDiagnostic, CSDiagnosticInfo.EmptyErrorInfo))
            {
                _lazyUseSiteDiagnostic = GetUseSiteDiagnosticImpl();
            }

            return _lazyUseSiteDiagnostic;
        }

        protected virtual DiagnosticInfo GetUseSiteDiagnosticImpl()
        {
            DiagnosticInfo diagnostic = null;

            if (!MergeUseSiteDiagnostics(ref diagnostic, CalculateUseSiteDiagnostic()))
            {
                // Check if this type is marked by RequiredAttribute attribute.
                // If so mark the type as bad, because it relies upon semantics that are not understood by the C# compiler.
                if (HasRequiredAttributeAttribute())
                {
                    diagnostic = new CSDiagnosticInfo(ErrorCode.ERR_BogusType, this);
                }
            }

            return diagnostic;
        }

        protected abstract bool HasRequiredAttributeAttribute();

        internal string DefaultMemberName
        {
            get
            {
                var uncommon = GetUncommonProperties();
                if (uncommon == s_noUncommonProperties)
                {
                    return "";
                }

                if (uncommon.lazyDefaultMemberName == null)
                {
                    // NOTE: the default member name is frequently null (e.g. if there is not indexer in the type).
                    // Make sure we set a non-null value so that we don't recompute it repeatedly.
                    // CONSIDER: this makes it impossible to distinguish between not having the attribute and
                    // having the attribute with a value of "".
                    Interlocked.CompareExchange(ref uncommon.lazyDefaultMemberName, DecodeDefaultMemberNameAttribute() ?? "", null);
                }

                return uncommon.lazyDefaultMemberName;
            }
        }

        protected abstract string DecodeDefaultMemberNameAttribute();

        internal sealed override bool IsInterface => Flags.IsInterface();
        internal sealed override bool IsComImport => (Flags & TypeAttributes.Import) != 0;
        internal sealed override bool ShouldAddWinRTMembers => IsWindowsRuntimeImport;
        internal sealed override bool IsWindowsRuntimeImport => (Flags & TypeAttributes.WindowsRuntime) != 0;
        internal sealed override bool IsSerializable => (Flags & TypeAttributes.Serializable) != 0;
        internal sealed override bool HasDeclarativeSecurity => (Flags & TypeAttributes.HasSecurity) != 0;

        internal sealed override CharSet MarshallingCharSet
        {
            get
            {
                CharSet result = Flags.ToCharSet();

                if (result == 0)
                {
                    // TODO(tomat): report error
                    return CharSet.Ansi;
                }

                return result;
            }
        }

        internal sealed override IEnumerable<Microsoft.Cci.SecurityAttribute> GetSecurityInformation()
        {
            throw ExceptionUtilities.Unreachable;
        }

        internal sealed override NamedTypeSymbol ComImportCoClass
        {
            get
            {
                if (!this.IsInterfaceType())
                {
                    return null;
                }

                var uncommon = GetUncommonProperties();
                if (uncommon == s_noUncommonProperties)
                {
                    return null;
                }

                if (ReferenceEquals(uncommon.lazyComImportCoClassType, ErrorTypeSymbol.UnknownResultType))
                {
                    var type = GetCoClassType();
                    var coClassType = ((object)type != null && (type.TypeKind == TypeKind.Class || type.IsErrorType())) ? (NamedTypeSymbol)type : null;

                    Interlocked.CompareExchange(ref uncommon.lazyComImportCoClassType, coClassType, ErrorTypeSymbol.UnknownResultType);
                }

                return uncommon.lazyComImportCoClassType;
            }
        }

        protected abstract TypeSymbol GetCoClassType();

        internal sealed override ImmutableArray<string> GetAppliedConditionalSymbols()
        {
            var uncommon = GetUncommonProperties();
            if (uncommon == s_noUncommonProperties)
            {
                return ImmutableArray<string>.Empty;
            }

            if (uncommon.lazyConditionalAttributeSymbols.IsDefault)
            {
                ImmutableArray<string> conditionalSymbols = GetConditionalAttributeValues();
                Debug.Assert(!conditionalSymbols.IsDefault);
                ImmutableInterlocked.InterlockedCompareExchange(ref uncommon.lazyConditionalAttributeSymbols, conditionalSymbols, default(ImmutableArray<string>));
            }

            return uncommon.lazyConditionalAttributeSymbols;
        }

        protected abstract ImmutableArray<string> GetConditionalAttributeValues();

        internal sealed override ObsoleteAttributeData ObsoleteAttributeData
        {
            get
            {
                var uncommon = GetUncommonProperties();
                if (uncommon == s_noUncommonProperties)
                {
                    return null;
                }

                LoadObsoleteAttributeData(ref uncommon.lazyObsoleteAttributeData);
                return uncommon.lazyObsoleteAttributeData;
            }
        }

        internal sealed override AttributeUsageInfo GetAttributeUsageInfo()
        {
            var uncommon = GetUncommonProperties();
            if (uncommon == s_noUncommonProperties)
            {
                return ((object)this.BaseTypeNoUseSiteDiagnostics != null) ? this.BaseTypeNoUseSiteDiagnostics.GetAttributeUsageInfo() : AttributeUsageInfo.Default;
            }

            if (uncommon.lazyAttributeUsageInfo.IsNull)
            {
                uncommon.lazyAttributeUsageInfo =
                    GetDeclaredAttributeUsageInfo() ?? 
                    BaseTypeNoUseSiteDiagnostics?.GetAttributeUsageInfo() ??
                    AttributeUsageInfo.Default;
            }

            return uncommon.lazyAttributeUsageInfo;
        }

        protected abstract AttributeUsageInfo? GetDeclaredAttributeUsageInfo();

        // perf, not correctness
        internal sealed override CSharpCompilation DeclaringCompilation => null;

        /// <summary>
        /// Returns the index of the first member of the specific kind.
        /// Returns the number of members if not found.
        /// </summary>
        protected static int GetIndexOfFirstMember(ImmutableArray<Symbol> members, SymbolKind kind)
        {
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i].Kind == kind)
                {
                    return i;
                }
            }

            return members.Length;
        }

        /// <summary>
        /// Returns all members of the specific kind, starting at the optional offset.
        /// Members of the same kind are assumed to be contiguous.
        /// </summary>
        private static IEnumerable<TSymbol> GetMembers<TSymbol>(ImmutableArray<Symbol> members, SymbolKind kind, int offset = -1)
            where TSymbol : Symbol
        {
            if (offset < 0)
            {
                offset = GetIndexOfFirstMember(members, kind);
            }

            int n = members.Length;
            for (int i = offset; i < n; i++)
            {
                var member = members[i];
                if (member.Kind != kind)
                {
                    yield break;
                }

                yield return (TSymbol)member;
            }
        }
    }
}
