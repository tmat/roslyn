// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace Microsoft.CodeAnalysis.CSharp.Lowering.Instrumentation
{
    internal sealed class LocalStateTrackingInstrumenter : CompoundInstrumenter
    {
        private readonly MethodSymbol _method;
        private readonly SyntheticBoundNodeFactory _factory;
        private readonly BindingDiagnosticBag _diagnostics;

        private LocalStateTrackingInstrumenter(
            MethodSymbol method,
            SyntheticBoundNodeFactory boundNodeFactory,
            BindingDiagnosticBag diagnostics,
            Instrumenter previous)
            : base(previous)
        {
            _method = method;
            _factory = boundNodeFactory;
            _diagnostics = diagnostics;
        }

        public static bool TryCreate(
            MethodSymbol method,
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

            instrumenter = new LocalStateTrackingInstrumenter(method, boundNodeFactory, diagnostics, previous);
            return true;
        }

        private MethodSymbol GetLogLocalStoreSymbol(TypeSymbol variableType)
        {
            var overload = variableType.SpecialType switch
            {
                SpecialType.System_Int32 => WellKnownMember.Microsoft_CodeAnalysis_Runtime_Instrumentation__LogLocalStoreInt32,
                _ => WellKnownMember.Microsoft_CodeAnalysis_Runtime_Instrumentation__LogLocalStoreObject,
            };

            return (MethodSymbol)Binder.GetWellKnownTypeMember(_factory.Compilation, overload, _diagnostics);
        }

        public override BoundExpression InstrumentAssignment(BoundAssignmentOperator original, BoundExpression rewritten)
        {
            Debug.Assert(!original.WasCompilerGenerated);
            Debug.Assert(!rewritten.WasCompilerGenerated);

            var result = base.InstrumentAssignment(original, rewritten);

            if (original is { IsRef: false, Left: BoundLocal { LocalSymbol: { SynthesizedKind: SynthesizedLocalKind.UserDefined } localSymbol } })
            {
                result = _factory.Call(receiver: null, GetLogLocalStoreSymbol(localSymbol.Type), new[]
                {
                    result,
                    _factory.MethodDefIndex(_method),
                    _factory.LocalDefIndex(localSymbol)
                });
            }
            // TODO:
            //else if (original is BoundParameter { ParameterSymbol: var parameterSymbol } ||
            //{
            //    result = _factory.StaticCall(GetLogParameterStoreSymbol(parameterSymbol.Type), result, _factory.MethodDefIndex(_method), _factory.ParamDefIndex(localSymbol));
            //}

            return result;
        }
    }
}
