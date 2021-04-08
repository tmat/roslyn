// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.EditAndContinue;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Debugger.Contracts.EditAndContinue;

namespace Microsoft.CodeAnalysis.CSharp.EditAndContinue.UnitTests
{
    internal class TestActiveStatementSpanProvider : IActiveStatementSpanProvider
    {
        private readonly IEditAndContinueWorkspaceService _encService;

        public TestActiveStatementSpanProvider(IEditAndContinueWorkspaceService encService)
        {
            _encService = encService;
        }

        public ValueTask<ImmutableArray<(LinePositionSpan, ActiveStatementFlags)>> GetAdjustedActiveStatementSpansAsync(Document document, DocumentActiveStatementSpanProvider activeStatementSpanProvider, CancellationToken cancellationToken)
            => _encService.GetAdjustedActiveStatementSpansAsync(document, activeStatementSpanProvider, cancellationToken);

        public ValueTask<ImmutableArray<ImmutableArray<(LinePositionSpan, ActiveStatementFlags)>>> GetBaseActiveStatementSpansAsync(Solution solution, ImmutableArray<DocumentId> documentIds, CancellationToken cancellationToken)
            => _encService.GetBaseActiveStatementSpansAsync(new RuntimeSolution(solution), documentIds, cancellationToken);
    }
}
