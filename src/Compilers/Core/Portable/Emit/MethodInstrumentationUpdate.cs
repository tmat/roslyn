// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Emit
{
    public readonly struct MethodInstrumentation
    {
        internal static readonly MethodInstrumentation Empty = new()
        {
            Kinds = ImmutableArray<InstrumentationKind>.Empty,
            ConditionalExpressions = ImmutableArray<ConditionalExpressionInstrumentation>.Empty
        };

        /// <summary>
        /// Kinds of instrumentation to apply to the entire method body.
        /// Empty to remove all kinds of instrumentation.
        /// </summary>
        public ImmutableArray<InstrumentationKind> Kinds { get; init; }

        /// <summary>
        /// Conditional expressions to insert into the method body.
        /// </summary>
        public ImmutableArray<ConditionalExpressionInstrumentation> ConditionalExpressions { get; init; }

        internal bool IsDefaultOrEmpty
            => Kinds.IsDefaultOrEmpty && ConditionalExpressions.IsDefaultOrEmpty;
    }

    public readonly struct ConditionalExpressionInstrumentation
    {
        /// <summary>
        /// Sequence point span within the syntax tree containing the body.
        /// </summary>
        public readonly TextSpan Span { get; init; }

        public readonly SyntaxNode Condition { get; init; }
        public readonly SyntaxNode Action { get; init; }
    }
}
