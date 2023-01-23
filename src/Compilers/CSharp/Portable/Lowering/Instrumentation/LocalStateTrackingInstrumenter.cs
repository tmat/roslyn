// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Collections;
using Microsoft.CodeAnalysis.Symbols;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal sealed class LocalStateTrackingInstrumenter : CompoundInstrumenter
    {
        private readonly LocalSymbol _contextVariable;
        private readonly MethodSymbol _method;
        private readonly SyntheticBoundNodeFactory _factory;
        private readonly BindingDiagnosticBag _diagnostics;

        /// <summary>
        /// All locals defined in the method that have a writable address taken
        /// associated with a generic logger used to log their address.
        /// </summary>
        private ImmutableHashSet<LocalSymbol> _userLocalsWithWritableAddress = ImmutableHashSet<LocalSymbol>.Empty;

        /// <summary>
        /// Parameters of the method that have a writable address taken.
        /// </summary>
        private readonly HashSet<ParameterSymbol> _parametersWithWritableAddress = new();

        private MethodSymbol? _lazyLocalAddressGenericLogger;
        private MethodSymbol? _lazyParameterAddressGenericLogger;

        private LocalStateTrackingInstrumenter(
            TypeSymbol instrumentationType,
            MethodSymbol method,
            BoundStatement methodBody,
            SyntheticBoundNodeFactory boundNodeFactory,
            BindingDiagnosticBag diagnostics,
            Instrumenter previous)
            : base(previous)
        {
            _method = method;
            _factory = boundNodeFactory;
            _diagnostics = diagnostics;

            _contextVariable = boundNodeFactory.SynthesizedLocal(instrumentationType, methodBody.Syntax, kind: SynthesizedLocalKind.LocalStoreTracker);
        }

        public static bool TryCreate(
            MethodSymbol method,
            BoundStatement methodBody,
            SyntheticBoundNodeFactory boundNodeFactory,
            BindingDiagnosticBag diagnostics,
            Instrumenter previous,
            [NotNullWhen(true)] out LocalStateTrackingInstrumenter? instrumenter)
        {
            instrumenter = null;

            // Do not instrument implicitly-declared methods, except for constructors.
            // Instrument implicit constructors in order to instrument member initializers.
            if (method.IsImplicitlyDeclared && !method.IsImplicitConstructor)
            {
                return false;
            }

            // Method has no user-defined body
            if (method is SourceMemberMethodSymbol { Bodies: { arrowBody: null, blockBody: null } })
            {
                return false;
            }

            var instrumentationType = boundNodeFactory.Compilation.GetWellKnownType(WellKnownType.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker);
            if (method.ContainingType.Equals(instrumentationType))
            {
                return false;
            }

            instrumenter = new LocalStateTrackingInstrumenter(instrumentationType, method, methodBody, boundNodeFactory, diagnostics, previous);
            return true;
        }

        private MethodSymbol? GetLocalOrParameterStoreLogger(TypeSymbol variableType, Symbol targetSymbol, bool isRefAssignment, SyntaxNode syntax)
        {
            // TODO: Nullable<T>
            // TODO: debugger display?

            var enumDelta = (targetSymbol.Kind == SymbolKind.Parameter) ?
                WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogParameterStoreBoolean - WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreBoolean : 0;

            var overload =
                isRefAssignment ? WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreAddress :
                variableType.EnumUnderlyingTypeOrSelf().SpecialType switch
                {
                    SpecialType.System_Boolean
                        => WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreBoolean,
                    SpecialType.System_SByte or SpecialType.System_Byte
                        => WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreByte,
                    SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char
                        => WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreUInt16,
                    SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single
                        => WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreUInt32,
                    SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double
                        => WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreUInt64,
                    SpecialType.System_String
                        => WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreString,
                    _ when !variableType.IsManagedTypeNoUseSiteDiagnostics && !variableType.IsNullableType() // TODO: for some reason Nullable<T> does not satisfy unmanaged constraint
                        => WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreUnmanaged,
                    _ when variableType.TypeKind is TypeKind.Struct
                        // well emit ToString constrained virtcall to avoid boxing the struct
                        => WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreString,
                    _
                        => WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreObject,
                };

            overload += enumDelta;

            var symbol = GetWellKnownMethodSymbol(overload, syntax);
            if (symbol is not null)
            {
                return symbol.IsGenericMethod ? symbol.Construct(variableType) : symbol;
            }

            var objectOverload = enumDelta + WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalStoreObject;

            if (isRefAssignment || variableType.IsRefLikeType || variableType.IsPointerType() || overload == objectOverload)
            {
                return null;
            }

            // fall back to Object overload if the specialized one is not present
            return GetWellKnownMethodSymbol(objectOverload, syntax);
        }

        private MethodSymbol? GetWellKnownMethodSymbol(WellKnownMember overload, SyntaxNode syntax)
            => (MethodSymbol?)Binder.GetWellKnownTypeMember(_factory.Compilation, overload, _diagnostics, syntax: syntax, isOptional: false);

        public override void InstrumentBlock(BoundBlock original, MethodSymbol method, BoundNode originalMethodBody, ref TemporaryArray<LocalSymbol> additionalLocals, out BoundStatement? prologue, out BoundStatement? epilogue)
        {
            base.InstrumentBlock(original, method, originalMethodBody, ref additionalLocals, out var previousPrologue, out epilogue);

            // Don't instrument blocks that are not a method body
            if (originalMethodBody != original)
            {
                prologue = previousPrologue;
                return;
            }

            var builder = ArrayBuilder<BoundStatement>.GetInstance((previousPrologue != null ? 1 : 0) + 1 + _method.ParameterCount);

            if (previousPrologue != null)
            {
                builder.Add(previousPrologue);
            }

            WellKnownMember methodEntryOverload;
            BoundExpression[] methodEntryArguments;

            var addressCount = _userLocalsWithWritableAddress.Count + _parametersWithWritableAddress.Count;
            if (addressCount > 0)
            {
                methodEntryOverload = WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogMethodEntryWithAddresses;
                methodEntryArguments = new[] { _factory.MethodDefIndex(_method), _factory.Literal(addressCount) };
            }
            else
            {
                methodEntryOverload = WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogMethodEntry;
                methodEntryArguments = new[] { _factory.MethodDefIndex(_method) };
            }

            var methodEntryLogger = GetWellKnownMethodSymbol(methodEntryOverload, _factory.Syntax);
            if (methodEntryLogger != null)
            {
                builder.Add(_factory.Assignment(_factory.Local(_contextVariable), _factory.Call(receiver: null, methodEntryLogger, methodEntryArguments)));
            }

            if (_userLocalsWithWritableAddress.Count > 0)
            {
                _lazyLocalAddressGenericLogger ??= GetWellKnownMethodSymbol(WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogLocalLoadAddress, _factory.Syntax);
                if (_lazyLocalAddressGenericLogger != null)
                {
                    var label = new LocalTrackerLabelSymbol(_contextVariable, _lazyLocalAddressGenericLogger, _userLocalsWithWritableAddress);
                    builder.Add(_factory.Label(label));
                    builder.Add(_factory.NoOp(NoOpStatementFlavor.Default));
                }
            }

            if (_parametersWithWritableAddress.Count > 0)
            {
                _lazyParameterAddressGenericLogger ??= GetWellKnownMethodSymbol(WellKnownMember.Microsoft_CodeAnalysis_Runtime_LocalStoreTracker__LogParameterLoadAddress, _factory.Syntax);
                if (_lazyParameterAddressGenericLogger != null)
                {
                    foreach (var parameterSymbol in _parametersWithWritableAddress.OrderBy(p => p.Ordinal))
                    {
                        var logger = _lazyParameterAddressGenericLogger.Construct(parameterSymbol.Type);

                        builder.Add(_factory.ExpressionStatement(_factory.Call(receiver: _factory.Local(_contextVariable), logger, new BoundExpression[]
                        {
                            _factory.Parameter(parameterSymbol),
                            _factory.Literal((ushort)parameterSymbol.Ordinal)
                        })));
                    }
                }
            }

            foreach (var parameter in _method.Parameters)
            {
                var isRefAssignment = parameter.RefKind is RefKind.Out or RefKind.Ref;
                var parameterLogger = GetLocalOrParameterStoreLogger(parameter.Type, parameter, isRefAssignment, _factory.Syntax);
                if (parameterLogger != null)
                {
                    builder.Add(_factory.ExpressionStatement(_factory.Call(receiver: _factory.Local(_contextVariable), parameterLogger, new[]
                    {
                        MakeArgument(parameterLogger.Parameters[0], _factory.Parameter(parameter), parameter, parameter.Type, isRefAssignment),
                        _factory.Literal((ushort)parameter.Ordinal)
                    })));
                }
            }

            additionalLocals.Add(_contextVariable);
            prologue = _factory.StatementList(builder.ToImmutableAndFree());
        }

        public override BoundStatement InstrumentFieldOrPropertyInitializer(BoundStatement original, BoundStatement rewritten)
        {
            // TODO: if original is block it may define variables
            return base.InstrumentFieldOrPropertyInitializer(original, rewritten);
        }

        public override BoundExpression InstrumentUserDefinedLocalAssignment(BoundAssignmentOperator original)
        {
            Debug.Assert(!original.WasCompilerGenerated);
            Debug.Assert(original.Left is BoundLocal { LocalSymbol.SynthesizedKind: SynthesizedLocalKind.UserDefined } or BoundParameter);

            var assignment = base.InstrumentUserDefinedLocalAssignment(original);

            TypeSymbol targetType;
            BoundExpression targetIndex;
            Symbol targetSymbol;

            if (original.Left is BoundLocal local)
            {
                var localSymbol = local.LocalSymbol;
                targetSymbol = localSymbol;
                targetIndex = _factory.LocalDefIndex(localSymbol);
                targetType = localSymbol.Type;
            }
            else
            {
                var parameterSymbol = ((BoundParameter)original.Left).ParameterSymbol;
                targetSymbol = parameterSymbol;
                targetIndex = _factory.Literal((ushort)parameterSymbol.Ordinal);
                targetType = parameterSymbol.Type;
            }

            var logger = GetLocalOrParameterStoreLogger(targetType, targetSymbol, original.IsRef, original.Syntax);
            if (logger is null)
            {
                return assignment;
            }

            return _factory.Sequence(new[]
            {
                _factory.Call(receiver: _factory.Local(_contextVariable), logger, new[] { MakeArgument(logger.Parameters[0], assignment, targetSymbol, targetType, original.IsRef), targetIndex })
            }, VariableRead(targetSymbol));
        }

        private BoundExpression MakeArgument(ParameterSymbol parameter, BoundExpression value, Symbol targetSymbol, TypeSymbol targetType, bool isRefAssignment)
        {
            if (isRefAssignment)
            {
                Debug.Assert(parameter.RefKind == RefKind.Ref);
                return value;
            }

            if (parameter.RefKind == RefKind.None)
            {
                if (parameter.Type.SpecialType == SpecialType.System_String && targetType.SpecialType != SpecialType.System_String)
                {
                    var toString = GetWellKnownMethodSymbol(WellKnownMember.System_Object__ToString, value.Syntax);
                    if (toString is null)
                    {
                        // arbitrary string, won't happen in practice
                        return _factory.Literal("");
                    }

                    return _factory.Call(value, toString);
                }

                return _factory.Convert(parameter.Type, value);
            }

            // address of assigned value:
            Debug.Assert(parameter.RefKind == RefKind.Ref);
            if (value is BoundLocal or BoundParameter)
            {
                return value;
            }

            return _factory.Sequence(new[] { value }, VariableRead(targetSymbol));
        }

        private BoundExpression VariableRead(Symbol localOrParameterSymbol)
            => localOrParameterSymbol switch
            {
                LocalSymbol local => _factory.Local(local),
                ParameterSymbol param => _factory.Parameter(param),
                _ => throw ExceptionUtilities.UnexpectedValue(localOrParameterSymbol)
            };

        public override BoundStatement InstrumentUserDefinedLocalInitialization(BoundLocalDeclaration original, BoundStatement rewritten)
        {
            Debug.Assert(original.LocalSymbol.SynthesizedKind == SynthesizedLocalKind.UserDefined);
            Debug.Assert(original.InitializerOpt != null);

            if (original.LocalSymbol.RefKind is RefKind.Ref)
            {
                RecordStorageWithWritableAddress(original.InitializerOpt);
            }

            return base.InstrumentUserDefinedLocalInitialization(original, rewritten);
        }

        public override BoundExpression InstrumentArgument(BoundExpression original, BoundExpression rewritten, RefKind refKind)
        {
            if (refKind is RefKind.Ref or RefKind.Out)
            {
                RecordStorageWithWritableAddress(original);
            }

            return base.InstrumentArgument(original, rewritten, refKind);
        }

        private void RecordStorageWithWritableAddress(BoundExpression expression)
        {
            if (expression is BoundLocal { LocalSymbol: { SynthesizedKind: SynthesizedLocalKind.UserDefined } localSymbol })
            {
                if (localSymbol.RefKind == RefKind.None)
                {
                    _userLocalsWithWritableAddress = _userLocalsWithWritableAddress.Add(localSymbol);
                }
            }
            else if (expression is BoundParameter { ParameterSymbol: var parameterSymbol })
            {
                if (parameterSymbol.RefKind == RefKind.None)
                {
                    _parametersWithWritableAddress.Add(parameterSymbol);
                }
            }
        }
    }
}
