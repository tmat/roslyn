// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Debugger.Contracts.EditAndContinue;

namespace Microsoft.CodeAnalysis.EditAndContinue
{
    internal interface IEditAndContinueWorkspaceService : IWorkspaceService
    {
        ValueTask<ImmutableArray<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, DocumentActiveStatementSpanProvider activeStatementSpanProvider, CancellationToken cancellationToken);
        ValueTask<bool> HasChangesAsync(RuntimeSolution solution, SolutionActiveStatementSpanProvider activeStatementSpanProvider, string? sourceFilePath, CancellationToken cancellationToken);
        ValueTask<EmitSolutionUpdateResults> EmitSolutionUpdateAsync(RuntimeSolution solution, SolutionActiveStatementSpanProvider activeStatementSpanProvider, CancellationToken cancellationToken);

        void CommitSolutionUpdate(out ImmutableArray<DocumentId> documentsToReanalyze);
        void DiscardSolutionUpdate();

        void OnSourceFileUpdated(Document document);

        ValueTask StartDebuggingSessionAsync(RuntimeSolution solution, IManagedEditAndContinueDebuggerService debuggerService, bool captureMatchingDocuments, CancellationToken cancellationToken);
        void BreakStateEntered(out ImmutableArray<DocumentId> documentsToReanalyze);
        void EndDebuggingSession(out ImmutableArray<DocumentId> documentsToReanalyze);

        ValueTask<bool?> IsActiveStatementInExceptionRegionAsync(RuntimeSolution solution, ManagedInstructionId instructionId, CancellationToken cancellationToken);
        ValueTask<LinePositionSpan?> GetCurrentActiveStatementPositionAsync(RuntimeSolution solution, SolutionActiveStatementSpanProvider activeStatementSpanProvider, ManagedInstructionId instructionId, CancellationToken cancellationToken);

        /// <summary>
        /// Returns base active statement spans contained in each specified document.
        /// </summary>
        /// <returns>
        /// <see langword="default"/> if called outside of an edit session.
        /// The length of the returned array matches the length of <paramref name="documentIds"/> otherwise.
        /// </returns>
        ValueTask<ImmutableArray<ImmutableArray<(LinePositionSpan, ActiveStatementFlags)>>> GetBaseActiveStatementSpansAsync(RuntimeSolution solution, ImmutableArray<DocumentId> documentIds, CancellationToken cancellationToken);

        /// <summary>
        /// Returns adjusted active statements in the specified <paramref name="document"/> snapshot.
        /// </summary>
        /// <returns>
        /// <see langword="default"/> if called outside of an edit session, or active statements for the document can't be determined for some reason
        /// (e.g. the document has syntax errors or is out-of-sync).
        /// </returns>
        ValueTask<ImmutableArray<(LinePositionSpan, ActiveStatementFlags)>> GetAdjustedActiveStatementSpansAsync(Document document, DocumentActiveStatementSpanProvider activeStatementSpanProvider, CancellationToken cancellationToken);
    }
}
