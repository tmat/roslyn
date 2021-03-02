// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Roslyn.Utilities;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using Microsoft.CodeAnalysis.Symbols;

namespace Microsoft.CodeAnalysis.Emit
{
    internal abstract class SymbolChanges
    {
        /// <summary>
        /// Maps definitions being emitted to the corresponding definitions defined in the previous generation (metadata or source).
        /// </summary>
        private readonly DefinitionMap _definitionMap;

        /// <summary>
        /// Contains all symbols explicitly updated/added to the source and 
        /// their containing types and namespaces. 
        /// </summary>
        private readonly IReadOnlyDictionary<ISymbolInternal, SymbolChange> _changes;

        private readonly Func<ISymbol, bool> _isAddedSymbol;

        protected SymbolChanges(DefinitionMap definitionMap, IEnumerable<SemanticEdit> edits, Func<ISymbol, bool> isAddedSymbol)
        {
            _definitionMap = definitionMap;
            _isAddedSymbol = isAddedSymbol;
            _changes = CalculateChanges(edits);
        }

        /// <summary>
        /// True if the symbol is a source symbol added during EnC session. 
        /// The symbol may be declared in any source compilation in the current solution.
        /// </summary>
        public bool IsAdded(ISymbol symbol)
        {
            return _isAddedSymbol(symbol);
        }

        /// <summary>
        /// Returns true if the symbol or some child symbol has changed and needs to be compiled.
        /// </summary>
        public bool RequiresCompilation(ISymbolInternal symbol)
            => GetChange(symbol) != SymbolChange.None;

        public SymbolChange GetChange(Cci.IDefinition definition)
        {
            var symbol = definition.GetInternalSymbol();
            if (symbol is not null)
            {
                return GetChange(symbol);
            }

            // If the def existed in the previous generation, the def is unchanged
            // (although it may contain changed defs); otherwise, it was added.
            if (_definitionMap.DefinitionExists(definition))
            {
                return (definition is Cci.ITypeDefinition) ? SymbolChange.ContainsChanges : SymbolChange.None;
            }

            return SymbolChange.Added;
        }

        public SymbolChange GetChange(ISymbolInternal symbol)
        {
            if (_changes.TryGetValue(symbol, out var change))
            {
                return change;
            }

            if (symbol is ISynthesizedMethodBodyImplementationSymbol synthesizedDef)
            {
                Debug.Assert(synthesizedDef.Method != null);

                var generatorMethodChange = GetChange(synthesizedDef.Method);
                switch (generatorMethodChange)
                {
                    case SymbolChange.Updated:
                        // The generator has been updated. Some synthesized members should be reused, others updated or added.

                        // The container of the synthesized symbol doesn't exist, we need to add the symbol.
                        // This may happen e.g. for members of a state machine type when a non-iterator method is changed to an iterator.
                        if (!_definitionMap.DefinitionExists(synthesizedDef.ContainingType))
                        {
                            return SymbolChange.Added;
                        }

                        if (!_definitionMap.DefinitionExists(synthesizedDef))
                        {
                            // A method was changed to a method containing a lambda, to an iterator, or to an async method.
                            // The state machine or closure class has been added.
                            return SymbolChange.Added;
                        }

                        // The existing symbol should be reused when the generator is updated,
                        // not updated since it's form doesn't depend on the content of the generator.
                        // For example, when an iterator method changes all methods that implement IEnumerable 
                        // but MoveNext can be reused as they are.
                        if (!synthesizedDef.HasMethodBodyDependency)
                        {
                            return SymbolChange.None;
                        }

                        // If the type produced from the method body existed before then its members are updated.
                        if (synthesizedDef.Kind == SymbolKind.NamedType)
                        {
                            return SymbolChange.ContainsChanges;
                        }

                        if (synthesizedDef.Kind == SymbolKind.Method)
                        {
                            // The method body might have been updated.
                            return SymbolChange.Updated;
                        }

                        return SymbolChange.None;

                    case SymbolChange.Added:
                        // The method has been added - add the synthesized member as well, unless they already exist.
                        if (!_definitionMap.DefinitionExists(synthesizedDef))
                        {
                            return SymbolChange.Added;
                        }

                        // If the existing member is a type we need to add new members into it.
                        // An example is a shared static display class - an added method with static lambda will contribute
                        // the lambda and cache fields into the shared display class.
                        if (synthesizedDef.Kind == SymbolKind.NamedType)
                        {
                            return SymbolChange.ContainsChanges;
                        }

                        // Update method.
                        // An example is a constructor a shared display class - an added method with lambda will contribute
                        // cache field initialization code into the constructor.
                        if (synthesizedDef.Kind == SymbolKind.Method)
                        {
                            return SymbolChange.Updated;
                        }

                        // Otherwise, there is nothing to do.
                        // For example, a static lambda display class cache field.
                        return SymbolChange.None;

                    default:
                        // The method had to change, otherwise the synthesized symbol wouldn't be generated
                        throw ExceptionUtilities.UnexpectedValue(generatorMethodChange);
                }
            }

            // Calculate change based on change to container.
            var container = GetContainingSymbol(symbol);
            if (container == null)
            {
                return SymbolChange.None;
            }

            var containerChange = GetChange(container);
            switch (containerChange)
            {
                case SymbolChange.Added:
                    // If container is added then all its members have been added.
                    return SymbolChange.Added;

                case SymbolChange.None:
                    // If container has no changes then none of its members have any changes.
                    return SymbolChange.None;

                case SymbolChange.Updated:
                case SymbolChange.ContainsChanges:
                    if (symbol is INamespaceSymbolInternal internalNamespaceSymbol)
                    {
                        // If the namespace did not exist in the previous generation, it was added.
                        // Otherwise the namespace may contain changes.
                        return _definitionMap.NamespaceExists(internalNamespaceSymbol) ? SymbolChange.ContainsChanges : SymbolChange.Added;
                    }

                    // If the definition did not exist in the previous generation, it was added.
                    return _definitionMap.DefinitionExists(symbol) ? SymbolChange.None : SymbolChange.Added;

                default:
                    throw ExceptionUtilities.UnexpectedValue(containerChange);
            }
        }

        protected abstract ISymbolInternal? GetISymbolInternalOrNull(ISymbol symbol);

        public IEnumerable<Cci.INamespaceTypeDefinition> GetTopLevelSourceTypeDefinitions(EmitContext context)
        {
            foreach (var symbol in _changes.Keys)
            {
                var namespaceTypeDef = (symbol.GetCciAdapter() as Cci.ITypeDefinition)?.AsNamespaceTypeDefinition(context);
                if (namespaceTypeDef != null)
                {
                    yield return namespaceTypeDef;
                }
            }
        }

        /// <summary>
        /// Calculate the set of changes up to top-level types. The result
        /// will be used as a filter when traversing the module.
        /// 
        /// Note that these changes only include user-defined source symbols, not synthesized symbols since those will be 
        /// generated during lowering of the changed user-defined symbols.
        /// </summary>
        private IReadOnlyDictionary<ISymbolInternal, SymbolChange> CalculateChanges(IEnumerable<SemanticEdit> edits)
        {
            var changes = new Dictionary<ISymbolInternal, SymbolChange>();

            foreach (var edit in edits)
            {
                SymbolChange change;

                switch (edit.Kind)
                {
                    case SemanticEditKind.Update:
                        change = SymbolChange.Updated;
                        break;

                    case SemanticEditKind.Insert:
                        change = SymbolChange.Added;
                        break;

                    case SemanticEditKind.Delete:
                        // No work to do.
                        continue;

                    default:
                        throw ExceptionUtilities.UnexpectedValue(edit.Kind);
                }

                Debug.Assert(edit.NewSymbol is not null);
                var member = GetISymbolInternalOrNull(edit.NewSymbol);
                if (member is null)
                {
                    continue;
                }

                // Partial methods are supplied as implementations but recorded
                // internally as definitions since definitions are used in emit.
                if (member is IMethodSymbolInternal method)
                {
                    // Partial methods should be implementations, not definitions.
                    Debug.Assert(method.PartialImplementationPart == null);
                    Debug.Assert(edit.OldSymbol == null || ((IMethodSymbolInternal)GetISymbolInternalOrNull(edit.OldSymbol)!).PartialImplementationPart == null);

                    var definitionPart = method.PartialDefinitionPart;
                    if (definitionPart != null)
                    {
                        member = definitionPart;
                    }
                }

                AddContainingTypesAndNamespaces(changes, member);
                changes.Add(member, change);
            }

            return changes;
        }

        private static void AddContainingTypesAndNamespaces(Dictionary<ISymbolInternal, SymbolChange> changes, ISymbolInternal symbol)
        {
            while (true)
            {
                var containingSymbol = GetContainingSymbol(symbol);
                if (containingSymbol == null || changes.ContainsKey(containingSymbol))
                {
                    return;
                }

                var change = containingSymbol.Kind is SymbolKind.Property or SymbolKind.Event ?
                    SymbolChange.Updated : SymbolChange.ContainsChanges;

                changes.Add(containingSymbol, change);
                symbol = containingSymbol;
            }
        }

        /// <summary>
        /// Return the symbol that contains this symbol as far
        /// as changes are concerned. For instance, an auto property
        /// is considered the containing symbol for the backing
        /// field and the accessor methods. By default, the containing
        /// symbol is simply Symbol.ContainingSymbol.
        /// </summary>
        private static ISymbolInternal? GetContainingSymbol(ISymbolInternal symbol)
        {
            // This approach of walking up the symbol hierarchy towards the
            // root, rather than walking down to the leaf symbols, seems
            // unreliable. It may be better to walk down using the usual
            // emit traversal, but prune the traversal to those types and
            // members that are known to contain changes.
            switch (symbol.Kind)
            {
                case SymbolKind.Field:
                    {
                        var associated = ((IFieldSymbolInternal)symbol).AssociatedSymbol;
                        if (associated != null)
                        {
                            return associated;
                        }
                    }
                    break;

                case SymbolKind.Method:
                    {
                        var associated = ((IMethodSymbolInternal)symbol).AssociatedSymbol;
                        if (associated != null)
                        {
                            return associated;
                        }
                    }
                    break;
            }

            symbol = symbol.ContainingSymbol;
            if (symbol != null)
            {
                switch (symbol.Kind)
                {
                    case SymbolKind.NetModule:
                    case SymbolKind.Assembly:
                        // These symbols are never part of the changes collection.
                        return null;
                }
            }

            return symbol;
        }
    }
}
