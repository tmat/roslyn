// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Reflection.Metadata;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.DocumentationComments;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis.CSharp.Emit;
using Microsoft.CodeAnalysis.Collections;

namespace Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE
{
    /// <summary>
    /// The class to represent all types imported from a PE/module.
    /// </summary>
    internal abstract class PENamedTypeSymbol : MetadataNamedTypeSymbol<TypeDefinitionHandle, PENamedTypeSymbol, PEFieldSymbol>
    {
        internal readonly TypeDefinitionHandle Handle;

        internal static PENamedTypeSymbol Create(
            PEModuleSymbol moduleSymbol,
            PENamespaceSymbol containingNamespace,
            TypeDefinitionHandle handle,
            string emittedNamespaceName)
        {
            GenericParameterHandleCollection genericParameterHandles;
            ushort arity;
            BadImageFormatException mrEx = null;

            GetGenericInfo(moduleSymbol, handle, out genericParameterHandles, out arity, out mrEx);

            bool mangleName;
            PENamedTypeSymbol result;

            if (arity == 0)
            {
                result = new NonGeneric(moduleSymbol, containingNamespace, handle, emittedNamespaceName, out mangleName);
            }
            else
            {
                result = new Generic(
                    moduleSymbol,
                    containingNamespace,
                    handle,
                    emittedNamespaceName,
                    genericParameterHandles,
                    arity,
                    out mangleName);
            }

            if (mrEx != null)
            {
                result._lazyUseSiteDiagnostic = new CSDiagnosticInfo(ErrorCode.ERR_BogusType, result);
            }

            return result;
        }

        private static void GetGenericInfo(PEModuleSymbol moduleSymbol, TypeDefinitionHandle handle, out GenericParameterHandleCollection genericParameterHandles, out ushort arity, out BadImageFormatException mrEx)
        {
            try
            {
                genericParameterHandles = moduleSymbol.Module.GetTypeDefGenericParamsOrThrow(handle);
                arity = (ushort)genericParameterHandles.Count;
                mrEx = null;
            }
            catch (BadImageFormatException e)
            {
                arity = 0;
                genericParameterHandles = default(GenericParameterHandleCollection);
                mrEx = e;
            }
        }

        internal static PENamedTypeSymbol Create(
            PEModuleSymbol moduleSymbol,
            PENamedTypeSymbol containingType,
            TypeDefinitionHandle handle)
        {
            GenericParameterHandleCollection genericParameterHandles;
            ushort metadataArity;
            BadImageFormatException mrEx = null;

            GetGenericInfo(moduleSymbol, handle, out genericParameterHandles, out metadataArity, out mrEx);

            ushort arity = 0;
            var containerMetadataArity = containingType.MetadataArity;

            if (metadataArity > containerMetadataArity)
            {
                arity = (ushort)(metadataArity - containerMetadataArity);
            }

            bool mangleName;
            PENamedTypeSymbol result;

            if (metadataArity == 0)
            {
                result = new NonGeneric(moduleSymbol, containingType, handle, null, out mangleName);
            }
            else
            {
                result = new Generic(
                    moduleSymbol,
                    containingType,
                    handle,
                    null,
                    genericParameterHandles,
                    arity,
                    out mangleName);
            }

            if (mrEx != null || metadataArity < containerMetadataArity)
            {
                result._lazyUseSiteDiagnostic = new CSDiagnosticInfo(ErrorCode.ERR_BogusType, result);
            }

            return result;
        }

        private PENamedTypeSymbol(
            PEModuleSymbol moduleSymbol,
            NamespaceOrTypeSymbol container,
            TypeDefinitionHandle handle,
            string emittedNamespaceName,
            ushort arity,
            out bool mangleName)
            : base(
                  container,
                  emittedNamespaceName,
                  moduleSymbol.Module.TryGetTypeDefName(handle),
                  moduleSymbol.Module.TryGetTypeDefFlags(handle),
                  arity,
                  out mangleName)
        {
            Debug.Assert(!handle.IsNil);
            Debug.Assert(arity == 0 || this is Generic);

            Handle = handle;
        }

        internal PEModuleSymbol ContainingPEModule => (PEModuleSymbol)ContainingModule;

        protected override NamedTypeSymbol MakeDeclaredBaseType()
        {
            if (!Flags.IsInterface())
            {
                try
                {
                    var moduleSymbol = ContainingPEModule;
                    EntityHandle token = moduleSymbol.Module.GetBaseTypeOfTypeOrThrow(Handle);

                    if (!token.IsNil)
                    {
                        TypeSymbol decodedType = new MetadataDecoder(moduleSymbol, this).GetTypeOfToken(token);
                        return (NamedTypeSymbol)DynamicTypeDecoder.TransformType(decodedType, 0, Handle, moduleSymbol);
                    }
                }
                catch (BadImageFormatException mrEx)
                {
                    return new UnsupportedMetadataTypeSymbol(mrEx);
                }
            }

            return null;
        }

        protected override ImmutableArray<NamedTypeSymbol> MakeDeclaredInterfaces()
        {
            try
            {
                var moduleSymbol = ContainingPEModule;
                var interfaceImpls = moduleSymbol.Module.GetInterfaceImplementationsOrThrow(Handle);

                if (interfaceImpls.Count > 0)
                {
                    NamedTypeSymbol[] symbols = new NamedTypeSymbol[interfaceImpls.Count];
                    var tokenDecoder = new MetadataDecoder(moduleSymbol, this);

                    int i = 0;
                    foreach (var interfaceImpl in interfaceImpls)
                    {
                        EntityHandle interfaceHandle = moduleSymbol.Module.MetadataReader.GetInterfaceImplementation(interfaceImpl).Interface;
                        TypeSymbol typeSymbol = tokenDecoder.GetTypeOfToken(interfaceHandle);

                        var namedTypeSymbol = typeSymbol as NamedTypeSymbol;
                        symbols[i++] = (object)namedTypeSymbol != null ? namedTypeSymbol : new UnsupportedMetadataTypeSymbol(); // interface tmpList contains a bad type
                    }

                    return symbols.AsImmutableOrNull();
                }

                return ImmutableArray<NamedTypeSymbol>.Empty;
            }
            catch (BadImageFormatException mrEx)
            {
                return ImmutableArray.Create<NamedTypeSymbol>(new UnsupportedMetadataTypeSymbol(mrEx));
            }
        }

        protected override void LoadCustomAttributes(ref ImmutableArray<CSharpAttributeData> lazyCustomAttributes)
        {
            if (MightContainExtensionMethods)
            {
                this.ContainingPEModule.LoadCustomAttributesFilterExtensions(
                    this.Handle,
                    ref lazyCustomAttributes);
            }
            else
            {
                this.ContainingPEModule.LoadCustomAttributes(this.Handle,
                    ref lazyCustomAttributes);
            }
        }

        protected override void LoadObsoleteAttributeData(ref ObsoleteAttributeData lazyObsoleteAttributeData)
        {
            ObsoleteAttributeHelpers.InitializeObsoleteDataFromMetadata(ref lazyObsoleteAttributeData, Handle, ContainingPEModule);
        }

        protected override HashSet<string> LoadNonTypeMemberNames()
        {
            var moduleSymbol = ContainingPEModule;
            var module = moduleSymbol.Module;

            var names = new HashSet<string>();

            try
            {
                foreach (var methodDef in module.GetMethodsOfTypeOrThrow(Handle))
                {
                    try
                    {
                        names.Add(module.GetMethodDefNameOrThrow(methodDef));
                    }
                    catch (BadImageFormatException)
                    { }
                }
            }
            catch (BadImageFormatException)
            { }

            try
            {
                foreach (var propertyDef in module.GetPropertiesOfTypeOrThrow(Handle))
                {
                    try
                    {
                        names.Add(module.GetPropertyDefNameOrThrow(propertyDef));
                    }
                    catch (BadImageFormatException)
                    { }
                }
            }
            catch (BadImageFormatException)
            { }

            try
            {
                foreach (var eventDef in module.GetEventsOfTypeOrThrow(Handle))
                {
                    try
                    {
                        names.Add(module.GetEventDefNameOrThrow(eventDef));
                    }
                    catch (BadImageFormatException)
                    { }
                }
            }
            catch (BadImageFormatException)
            { }

            try
            {
                foreach (var fieldDef in module.GetFieldsOfTypeOrThrow(Handle))
                {
                    try
                    {
                        names.Add(module.GetFieldDefNameOrThrow(fieldDef));
                    }
                    catch (BadImageFormatException)
                    { }
                }
            }
            catch (BadImageFormatException)
            { }

            return names;
        }

        protected override IEnumerable<PENamedTypeSymbol> CreateNestedTypes()
        {
            var moduleSymbol = this.ContainingPEModule;
            var module = moduleSymbol.Module;

            ImmutableArray<TypeDefinitionHandle> nestedTypeDefs;

            try
            {
                nestedTypeDefs = module.GetNestedTypeDefsOrThrow(Handle);
            }
            catch (BadImageFormatException)
            {
                yield break;
            }

            foreach (var typeRid in nestedTypeDefs)
            {
                if (module.ShouldImportNestedType(typeRid))
                {
                    yield return PENamedTypeSymbol.Create(moduleSymbol, this, typeRid);
                }
            }
        }

        protected override NamedTypeSymbol DecodeEnumUnderlyingType()
        {
            // From §8.5.2
            // An enum is considerably more restricted than a true type, as
            // follows:
            // - It shall have exactly one instance field, and the type of that field defines the underlying type of
            // the enumeration.
            // - It shall not have any static fields unless they are literal. (see §8.6.1.2)

            // The underlying type shall be a built-in integer type. Enums shall derive from System.Enum, hence they are
            // value types. Like all value types, they shall be sealed (see §8.9.9).

            var moduleSymbol = this.ContainingPEModule;
            var module = moduleSymbol.Module;
            var decoder = new MetadataDecoder(moduleSymbol, this);
            NamedTypeSymbol underlyingType = null;

            try
            {
                foreach (var fieldDef in module.GetFieldsOfTypeOrThrow(Handle))
                {
                    FieldAttributes fieldFlags;

                    try
                    {
                        fieldFlags = module.GetFieldDefFlagsOrThrow(fieldDef);
                    }
                    catch (BadImageFormatException)
                    {
                        continue;
                    }

                    if ((fieldFlags & FieldAttributes.Static) == 0)
                    {
                        // Instance field used to determine underlying type.
                        bool isVolatile;
                        ImmutableArray<ModifierInfo<TypeSymbol>> customModifiers;
                        TypeSymbol type = decoder.DecodeFieldSignature(fieldDef, out isVolatile, out customModifiers);

                        if (type.SpecialType.IsValidEnumUnderlyingType())
                        {
                            if ((object)underlyingType == null)
                            {
                                underlyingType = (NamedTypeSymbol)type;
                            }
                            else
                            {
                                underlyingType = new UnsupportedMetadataTypeSymbol(); // ambiguous underlying type
                            }
                        }
                    }
                }

                if ((object)underlyingType == null)
                {
                    underlyingType = new UnsupportedMetadataTypeSymbol(); // undefined underlying type
                }
            }
            catch (BadImageFormatException mrEx)
            {
                if ((object)underlyingType == null)
                {
                    underlyingType = new UnsupportedMetadataTypeSymbol(mrEx);
                }
            }

            return underlyingType;
        }

        protected override void PopulateMembersInDeclarationOrder(ArrayBuilder<Symbol> members)
        {
            if (TypeKind == TypeKind.Enum)
            {
                PopulateMembersOfEnumType(members);
            }
            else
            {
                PopulateMembersOfNonEnumType(members);
            }
        }

        private void PopulateMembersOfEnumType(ArrayBuilder<Symbol> members)
        {
            var moduleSymbol = this.ContainingPEModule;
            var module = moduleSymbol.Module;

            try
            {
                foreach (var fieldDef in module.GetFieldsOfTypeOrThrow(Handle))
                {
                    FieldAttributes fieldFlags;

                    try
                    {
                        fieldFlags = module.GetFieldDefFlagsOrThrow(fieldDef);
                        if ((fieldFlags & FieldAttributes.Static) == 0)
                        {
                            continue;
                        }
                    }
                    catch (BadImageFormatException)
                    {
                        fieldFlags = 0;
                    }

                    if (ModuleExtensions.ShouldImportField(fieldFlags, moduleSymbol.ImportOptions))
                    {
                        var field = new PEFieldSymbol(moduleSymbol, this, fieldDef);
                        members.Add(field);
                    }
                }
            }
            catch (BadImageFormatException)
            { }

            var syntheticCtor = new SynthesizedInstanceConstructor(this);
            members.Add(syntheticCtor);
        }

        protected override void PopulateEnumInstanceFields(ArrayBuilder<PEFieldSymbol> fields)
        {
            var moduleSymbol = this.ContainingPEModule;
            var module = moduleSymbol.Module;

            try
            {
                foreach (var fieldDef in module.GetFieldsOfTypeOrThrow(Handle))
                {
                    try
                    {
                        FieldAttributes fieldFlags = module.GetFieldDefFlagsOrThrow(fieldDef);
                        if ((fieldFlags & FieldAttributes.Static) == 0 &&
                            ModuleExtensions.ShouldImportField(fieldFlags, moduleSymbol.ImportOptions))
                        {
                            fields.Add(new PEFieldSymbol(moduleSymbol, this, fieldDef));
                        }
                    }
                    catch (BadImageFormatException)
                    {
                    }
                }
            }
            catch (BadImageFormatException)
            {
                fields.Clear();
            }
        }

        private void PopulateMembersOfNonEnumType(ArrayBuilder<Symbol> members)
        {
            Debug.Assert(SymbolKind.Field.ToSortOrder() < SymbolKind.Method.ToSortOrder());
            Debug.Assert(SymbolKind.Method.ToSortOrder() < SymbolKind.Property.ToSortOrder());
            Debug.Assert(SymbolKind.Property.ToSortOrder() < SymbolKind.Event.ToSortOrder());
            Debug.Assert(SymbolKind.Event.ToSortOrder() < SymbolKind.NamedType.ToSortOrder());

            var fieldMembers = ArrayBuilder<PEFieldSymbol>.GetInstance();
            var nonFieldMembers = ArrayBuilder<Symbol>.GetInstance();
            var privateFieldNameToSymbols = new MultiDictionary<string, PEFieldSymbol>();
            var methodHandleToSymbol = PooledDictionary<MethodDefinitionHandle, PEMethodSymbol>.GetInstance();

            PopulateFields(fieldMembers, privateFieldNameToSymbols);

            // A method may be referenced as an accessor by one or more properties. And,
            // any of those properties may be "bogus" if one of the property accessors
            // does not match the property signature. If the method is referenced by at
            // least one non-bogus property, then the method is created as an accessor,
            // and (for purposes of error reporting if the method is referenced directly) the
            // associated property is set (arbitrarily) to the first non-bogus property found
            // in metadata. If the method is not referenced by any non-bogus properties,
            // then the method is created as a normal method rather than an accessor.

            // Create a dictionary of method symbols indexed by metadata handle
            // (to allow efficient lookup when matching property accessors).
            PopulateMethods(nonFieldMembers, methodHandleToSymbol);

            if (this.TypeKind == TypeKind.Struct)
            {
                bool haveParameterlessConstructor = false;
                foreach (MethodSymbol method in nonFieldMembers)
                {
                    if (method.IsParameterlessConstructor())
                    {
                        haveParameterlessConstructor = true;
                        break;
                    }
                }

                // Structs have an implicit parameterless constructor, even if it
                // does not appear in metadata (11.3.8)
                if (!haveParameterlessConstructor)
                {
                    nonFieldMembers.Insert(0, new SynthesizedInstanceConstructor(this));
                }
            }

            PopulateProperties(methodHandleToSymbol, nonFieldMembers);
            PopulateEvents(privateFieldNameToSymbols, methodHandleToSymbol, nonFieldMembers);

            foreach (PEFieldSymbol field in fieldMembers)
            {
                if ((object)field.AssociatedSymbol == null)
                {
                    members.Add(field);
                }
                else
                {
                    // As for source symbols, our public API presents the fiction that all
                    // operations are performed on the event, rather than on the backing field.  
                    // The backing field is not accessible through the API.  As an additional 
                    // bonus, lookup is easier when the names don't collide.
                    Debug.Assert(field.AssociatedSymbol.Kind == SymbolKind.Event);
                }
            }

            members.AddRange(nonFieldMembers);

            nonFieldMembers.Free();
            fieldMembers.Free();
            methodHandleToSymbol.Free();
        }

        private void PopulateFields(ArrayBuilder<PEFieldSymbol> fieldMembers, MultiDictionary<string, PEFieldSymbol> privateFieldNameToSymbols)
        {
            var moduleSymbol = this.ContainingPEModule;
            var module = moduleSymbol.Module;

            // for ordinary struct types we import private fields so that we can distinguish empty structs from non-empty structs
            var isOrdinaryStruct = IsOrdinaryStruct;

            // for ordinary embeddable struct types we import private members so that 
            // we can report appropriate errors if the structure is used 
            var isOrdinaryEmbeddableStruct = IsOrdinaryEmbeddableStruct;

            try
            {
                foreach (var fieldRid in module.GetFieldsOfTypeOrThrow(Handle))
                {
                    try
                    {
                        if (!(isOrdinaryEmbeddableStruct ||
                            (isOrdinaryStruct && (module.GetFieldDefFlagsOrThrow(fieldRid) & FieldAttributes.Static) == 0) ||
                            module.ShouldImportField(fieldRid, moduleSymbol.ImportOptions)))
                        {
                            continue;
                        }
                    }
                    catch (BadImageFormatException)
                    { }

                    var symbol = new PEFieldSymbol(moduleSymbol, this, fieldRid);
                    fieldMembers.Add(symbol);

                    // Only private fields are potentially backing fields for field-like events.
                    if (symbol.DeclaredAccessibility == Accessibility.Private)
                    {
                        var name = symbol.Name;
                        if (name.Length > 0)
                        {
                            privateFieldNameToSymbols.Add(name, symbol);
                        }
                    }
                }
            }
            catch (BadImageFormatException)
            { }
        }

        private void PopulateMethods(ArrayBuilder<Symbol> members, PooledDictionary<MethodDefinitionHandle, PEMethodSymbol> map)
        {
            var moduleSymbol = this.ContainingPEModule;
            var module = moduleSymbol.Module;

            // For ordinary embeddable struct types we import private members so that we 
            // can report appropriate errors if the structure is used 
            var isOrdinaryEmbeddableStruct = IsOrdinaryEmbeddableStruct;

            try
            {
                foreach (var methodHandle in module.GetMethodsOfTypeOrThrow(Handle))
                {
                    if (isOrdinaryEmbeddableStruct || module.ShouldImportMethod(methodHandle, moduleSymbol.ImportOptions))
                    {
                        var method = new PEMethodSymbol(moduleSymbol, this, methodHandle);
                        members.Add(method);
                        map.Add(methodHandle, method);
                    }
                }
            }
            catch (BadImageFormatException)
            { }
        }

        private void PopulateProperties(Dictionary<MethodDefinitionHandle, PEMethodSymbol> methodHandleToSymbol, ArrayBuilder<Symbol> members)
        {
            var moduleSymbol = this.ContainingPEModule;
            var module = moduleSymbol.Module;

            try
            {
                foreach (var propertyDef in module.GetPropertiesOfTypeOrThrow(Handle))
                {
                    try
                    {
                        var methods = module.GetPropertyMethodsOrThrow(propertyDef);

                        PEMethodSymbol getMethod = GetAccessorMethod(module, methodHandleToSymbol, methods.Getter);
                        PEMethodSymbol setMethod = GetAccessorMethod(module, methodHandleToSymbol, methods.Setter);

                        if (((object)getMethod != null) || ((object)setMethod != null))
                        {
                            members.Add(new PEPropertySymbol(moduleSymbol, this, propertyDef, getMethod, setMethod));
                        }
                    }
                    catch (BadImageFormatException)
                    { }
                }
            }
            catch (BadImageFormatException)
            { }
        }

        private void PopulateEvents(
            MultiDictionary<string, PEFieldSymbol> privateFieldNameToSymbols,
            Dictionary<MethodDefinitionHandle, PEMethodSymbol> methodHandleToSymbol,
            ArrayBuilder<Symbol> members)
        {
            var moduleSymbol = this.ContainingPEModule;
            var module = moduleSymbol.Module;

            try
            {
                foreach (var eventRid in module.GetEventsOfTypeOrThrow(Handle))
                {
                    try
                    {
                        var methods = module.GetEventMethodsOrThrow(eventRid);

                        // NOTE: C# ignores all other accessors (most notably, raise/fire).
                        PEMethodSymbol addMethod = GetAccessorMethod(module, methodHandleToSymbol, methods.Adder);
                        PEMethodSymbol removeMethod = GetAccessorMethod(module, methodHandleToSymbol, methods.Remover);

                        // NOTE: both accessors are required, but that will be reported separately.
                        // Create the symbol unless both accessors are missing.
                        if (((object)addMethod != null) || ((object)removeMethod != null))
                        {
                            members.Add(new PEEventSymbol(moduleSymbol, this, eventRid, addMethod, removeMethod, privateFieldNameToSymbols));
                        }
                    }
                    catch (BadImageFormatException)
                    { }
                }
            }
            catch (BadImageFormatException)
            { }
        }

        private sealed class DeclarationOrderTypeSymbolComparer : IComparer<Symbol>
        {
            public static readonly DeclarationOrderTypeSymbolComparer Instance = new DeclarationOrderTypeSymbolComparer();

            private DeclarationOrderTypeSymbolComparer() { }

            public int Compare(Symbol x, Symbol y)
            {
                return HandleComparer.Default.Compare(((PENamedTypeSymbol)x).Handle, ((PENamedTypeSymbol)y).Handle);
            }
        }

        protected override IComparer<Symbol> DeclarationOrderComparer => DeclarationOrderTypeSymbolComparer.Instance;

        private PEMethodSymbol GetAccessorMethod(PEModule module, Dictionary<MethodDefinitionHandle, PEMethodSymbol> methodHandleToSymbol, MethodDefinitionHandle methodDef)
        {
            if (methodDef.IsNil)
            {
                return null;
            }

            PEMethodSymbol method;
            bool found = methodHandleToSymbol.TryGetValue(methodDef, out method);
            Debug.Assert(found || !module.ShouldImportMethod(methodDef, this.ContainingPEModule.ImportOptions));
            return method;
        }

        internal override IEnumerable<MethodSymbol> GetMethodsToEmit()
        {
            ImmutableArray<Symbol> members = GetMembers();

            // Get to methods.
            int index = GetIndexOfFirstMember(members, SymbolKind.Method);

            if (!this.IsInterfaceType())
            {
                for (; index < members.Length; index++)
                {
                    if (members[index].Kind != SymbolKind.Method)
                    {
                        break;
                    }

                    var method = (MethodSymbol)members[index];

                    // Don't emit the default value type constructor - the runtime handles that
                    if (!method.IsDefaultValueTypeConstructor())
                    {
                        yield return method;
                    }
                }
            }
            else
            {
                // We do not create symbols for v-table gap methods, let's figure out where the gaps go.

                if (index >= members.Length || members[index].Kind != SymbolKind.Method)
                {
                    // We didn't import any methods, it is Ok to return an empty set.
                    yield break;
                }

                var method = (PEMethodSymbol)members[index];
                var module = this.ContainingPEModule.Module;

                var methodDefs = ArrayBuilder<MethodDefinitionHandle>.GetInstance();

                try
                {
                    foreach (var methodDef in module.GetMethodsOfTypeOrThrow(Handle))
                    {
                        methodDefs.Add(methodDef);
                    }
                }
                catch (BadImageFormatException)
                { }

                foreach (var methodDef in methodDefs)
                {
                    if (method.Handle == methodDef)
                    {
                        yield return method;
                        index++;

                        if (index == members.Length || members[index].Kind != SymbolKind.Method)
                        {
                            // no need to return any gaps at the end.
                            methodDefs.Free();
                            yield break;
                        }

                        method = (PEMethodSymbol)members[index];
                    }
                    else
                    {
                        // Encountered a gap.
                        int gapSize;

                        try
                        {
                            gapSize = ModuleExtensions.GetVTableGapSize(module.GetMethodDefNameOrThrow(methodDef));
                        }
                        catch (BadImageFormatException)
                        {
                            gapSize = 1;
                        }

                        // We don't have a symbol to return, so, even if the name doesn't represent a gap, we still have a gap.
                        do
                        {
                            yield return null;
                            gapSize--;
                        }
                        while (gapSize > 0);
                    }
                }

                // Ensure we explicitly returned from inside loop.
                throw ExceptionUtilities.Unreachable;
            }
        }

        protected override IEnumerable<FieldSymbol> MergeNonEnumFieldsToEmit(IEnumerable<FieldSymbol> nonEventFields, ArrayBuilder<FieldSymbol> eventFields)
        {
            // We need to merge non-event fields with event fields while preserving their relative declaration order
            var handleToFieldMap = new SmallDictionary<FieldDefinitionHandle, FieldSymbol>();
            int count = 0;

            foreach (PEFieldSymbol field in nonEventFields)
            {
                handleToFieldMap.Add(field.Handle, field);
                count++;
            }

            foreach (PEFieldSymbol field in eventFields)
            {
                handleToFieldMap.Add(field.Handle, field);
            }

            count += eventFields.Count;
            eventFields.Free();

            var result = ArrayBuilder<FieldSymbol>.GetInstance(count);

            try
            {
                foreach (var handle in this.ContainingPEModule.Module.GetFieldsOfTypeOrThrow(Handle))
                {
                    FieldSymbol field;
                    if (handleToFieldMap.TryGetValue(handle, out field))
                    {
                        result.Add(field);
                    }
                }
            }
            catch (BadImageFormatException)
            { }

            Debug.Assert(result.Count == count);

            return result.ToImmutableAndFree();
        }

        protected override IEnumerable<FieldSymbol> MergeEnumFieldsToEmit(ImmutableArray<Symbol> allMembers, ImmutableArray<PEFieldSymbol> instanceEnumFields)
        {
            var moduleSymbol = this.ContainingPEModule;
            var module = moduleSymbol.Module;

            // Non-static fields of enum types are not imported by default because they are not bindable,
            // but we need them for NoPia.

            var result = ArrayBuilder<FieldSymbol>.GetInstance();

            int memberIndex = 0;
            int instanceIndex = 0;

            try
            {
                foreach (var fieldDef in module.GetFieldsOfTypeOrThrow(Handle))
                {
                    if (instanceIndex < instanceEnumFields.Length && instanceEnumFields[instanceIndex].Handle == fieldDef)
                    {
                        result.Add(instanceEnumFields[instanceIndex]);
                        instanceIndex++;
                        continue;
                    }

                    if (memberIndex < allMembers.Length && allMembers[memberIndex].Kind == SymbolKind.Field)
                    {
                        var field = (PEFieldSymbol)allMembers[memberIndex];

                        if (field.Handle == fieldDef)
                        {
                            result.Add(field);
                            memberIndex++;
                            continue;
                        }
                    }
                }
            }
            catch (BadImageFormatException)
            {
                result.Clear();
            }

            Debug.Assert(instanceIndex == instanceEnumFields.Length);
            Debug.Assert(memberIndex == allMembers.Length || allMembers[memberIndex].Kind != SymbolKind.Field);

            foreach (var field in result)
            {
                yield return field;
            }

            result.Free();
        }

        internal override bool GetGuidString(out string guidString)
        {
            return ContainingPEModule.Module.HasGuidAttribute(Handle, out guidString);
        }

        internal override TypeLayout Layout
        {
            get
            {
                return this.ContainingPEModule.Module.GetTypeLayout(Handle);
            }
        }

        protected override AttributeUsageInfo? GetDeclaredAttributeUsageInfo()
        {
            var handle = this.ContainingPEModule.Module.GetAttributeUsageAttributeHandle(Handle);

            if (!handle.IsNil)
            {
                var decoder = new MetadataDecoder(ContainingPEModule);
                TypedConstant[] positionalArgs;
                KeyValuePair<string, TypedConstant>[] namedArgs;
                if (decoder.GetCustomAttribute(handle, out positionalArgs, out namedArgs))
                {
                    AttributeUsageInfo info = AttributeData.DecodeAttributeUsageAttribute(positionalArgs[0], namedArgs.AsImmutableOrNull());
                    return info.HasValidAttributeTargets ? info : AttributeUsageInfo.Default;
                }
            }

            return null;
        }

        protected override string DecodeDefaultMemberNameAttribute()
        {
            string defaultMemberName;
            ContainingPEModule.Module.HasDefaultMemberAttribute(Handle, out defaultMemberName);
            return defaultMemberName;
        }

        protected override bool HasAnyCustomAttributes()
        {
            return ContainingPEModule.HasAnyCustomAttributes(Handle);
        }

        protected override ImmutableArray<string> GetConditionalAttributeValues()
        {
            return ContainingPEModule.Module.GetConditionalAttributeValues(Handle);
        }

        protected override bool HasRequiredAttributeAttribute()
        {
            return ContainingPEModule.Module.HasRequiredAttributeAttribute(Handle);
        }

        protected override bool HasExtensionAttribute()
        {
            return ContainingPEModule.Module.HasExtensionAttribute(Handle, ignoreCase: false);
        }

        protected override TypeSymbol GetCoClassType()
        {
            return this.ContainingPEModule.TryDecodeAttributeWithTypeArgument(this.Handle, AttributeDescription.CoClassAttribute);
        }

        protected override void OnContainedNamedTypesCreated(Dictionary<string, ImmutableArray<PENamedTypeSymbol>> types)
        {
            ContainingPEModule.OnNewTypeDeclarationsLoaded(types);
        }

        /// <summary>
        /// Specialized PENamedTypeSymbol for types with no type parameters in
        /// metadata (no type parameters on this type and all containing types).
        /// </summary>
        private sealed class NonGeneric : PENamedTypeSymbol
        {
            internal NonGeneric(
                PEModuleSymbol moduleSymbol,
                NamespaceOrTypeSymbol container,
                TypeDefinitionHandle handle,
                string emittedNamespaceName,
                out bool mangleName) :
                base(moduleSymbol, container, handle, emittedNamespaceName, 0, out mangleName)
            {
            }

            public override int Arity => 0;
            internal override bool MangleName => false;
            internal override int MetadataArity => (ContainingSymbol as PENamedTypeSymbol)?.MetadataArity ?? 0;
        }

        /// <summary>
        /// Specialized PENamedTypeSymbol for types with type parameters in metadata.
        /// NOTE: the type may have Arity == 0 if it has same metadata arity as the metadata arity of the containing type.
        /// </summary>
        private sealed class Generic : PENamedTypeSymbol
        {
            private readonly GenericParameterHandleCollection _genericParameterHandles;
            private readonly ushort _arity;
            private readonly bool _mangleName;
            private ImmutableArray<TypeParameterSymbol> _lazyTypeParameters;

            internal Generic(
                    PEModuleSymbol moduleSymbol,
                    NamespaceOrTypeSymbol container,
                    TypeDefinitionHandle handle,
                    string emittedNamespaceName,
                    GenericParameterHandleCollection genericParameterHandles,
                    ushort arity,
                    out bool mangleName
                )
                : base(moduleSymbol,
                      container,
                      handle,
                      emittedNamespaceName,
                      arity,
                      out mangleName)
            {
                Debug.Assert(genericParameterHandles.Count > 0);
                _arity = arity;
                _genericParameterHandles = genericParameterHandles;
                _mangleName = mangleName;
            }

            public override int Arity => _arity;
            internal override bool MangleName => _mangleName;
            override internal int MetadataArity => _genericParameterHandles.Count;

            internal override ImmutableArray<TypeSymbol> TypeArgumentsNoUseSiteDiagnostics
            {
                get
                {
                    // This is always the instance type, so the type arguments are the same as the type parameters.
                    return this.TypeParameters.Cast<TypeParameterSymbol, TypeSymbol>();
                }
            }

            public override ImmutableArray<TypeParameterSymbol> TypeParameters
            {
                get
                {
                    EnsureTypeParametersAreLoaded();
                    return _lazyTypeParameters;
                }
            }

            private void EnsureTypeParametersAreLoaded()
            {
                if (_lazyTypeParameters.IsDefault)
                {
                    var moduleSymbol = ContainingPEModule;

                    // If this is a nested type generic parameters in metadata include generic parameters of the outer types.
                    int firstIndex = _genericParameterHandles.Count - _arity;

                    TypeParameterSymbol[] ownedParams = new TypeParameterSymbol[_arity];
                    for (int i = 0; i < ownedParams.Length; i++)
                    {
                        ownedParams[i] = new PETypeParameterSymbol(moduleSymbol, this, (ushort)i, _genericParameterHandles[firstIndex + i]);
                    }

                    ImmutableInterlocked.InterlockedInitialize(ref _lazyTypeParameters,
                        ImmutableArray.Create<TypeParameterSymbol>(ownedParams));
                }
            }

            protected override DiagnosticInfo GetUseSiteDiagnosticImpl()
            {
                DiagnosticInfo diagnostic = null;

                if (!MergeUseSiteDiagnostics(ref diagnostic, base.GetUseSiteDiagnosticImpl()))
                {
                    // Verify type parameters for containing types
                    // match those on the containing types.
                    if (!MatchesContainingTypeParameters())
                    {
                        diagnostic = new CSDiagnosticInfo(ErrorCode.ERR_BogusType, this);
                    }
                }

                return diagnostic;
            }

            /// <summary>
            /// Return true if the type parameters specified on the nested type (this),
            /// that represent the corresponding type parameters on the containing
            /// types, in fact match the actual type parameters on the containing types.
            /// </summary>
            private bool MatchesContainingTypeParameters()
            {
                var container = this.ContainingType;
                if ((object)container == null)
                {
                    return true;
                }

                var containingTypeParameters = container.GetAllTypeParameters();
                int n = containingTypeParameters.Length;

                if (n == 0)
                {
                    return true;
                }

                // Create an instance of PENamedTypeSymbol for the nested type, but
                // with all type parameters, from the nested type and all containing
                // types. The type parameters on this temporary type instance are used
                // for comparison with those on the actual containing types. The
                // containing symbol for the temporary type is the namespace directly.
                var nestedType = Create(this.ContainingPEModule, (PENamespaceSymbol)this.ContainingNamespace, Handle, null);
                var nestedTypeParameters = nestedType.TypeParameters;
                var containingTypeMap = new TypeMap(containingTypeParameters, IndexedTypeParameterSymbol.Take(n), allowAlpha: false);
                var nestedTypeMap = new TypeMap(nestedTypeParameters, IndexedTypeParameterSymbol.Take(nestedTypeParameters.Length), allowAlpha: false);

                for (int i = 0; i < n; i++)
                {
                    var containingTypeParameter = containingTypeParameters[i];
                    var nestedTypeParameter = nestedTypeParameters[i];
                    if (!MemberSignatureComparer.HaveSameConstraints(containingTypeParameter, containingTypeMap, nestedTypeParameter, nestedTypeMap))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
