// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.EditAndContinue;
using Microsoft.CodeAnalysis.EditAndContinue.Contracts;
using Microsoft.CodeAnalysis.Host;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.ExternalAccess.HotReload.Api
{
    internal sealed class HotReloadService
    {
        private sealed class DebuggerService : IManagedHotReloadService
        {
            private readonly Func<ValueTask<ImmutableArray<string>>> _capabilitiesProvider;

            public DebuggerService(Func<ValueTask<ImmutableArray<string>>> capabilitiesProvider)
                => _capabilitiesProvider = capabilitiesProvider;

            public ValueTask<ImmutableArray<ManagedActiveStatementDebugInfo>> GetActiveStatementsAsync(CancellationToken cancellationToken)
                => ValueTaskFactory.FromResult(ImmutableArray<ManagedActiveStatementDebugInfo>.Empty);

            public ValueTask<ManagedHotReloadAvailability> GetAvailabilityAsync(Guid module, CancellationToken cancellationToken)
                => ValueTaskFactory.FromResult(new ManagedHotReloadAvailability(ManagedHotReloadAvailabilityStatus.Available));

            public ValueTask<ImmutableArray<string>> GetCapabilitiesAsync(CancellationToken cancellationToken)
                => _capabilitiesProvider();

            public ValueTask PrepareModuleForUpdateAsync(Guid module, CancellationToken cancellationToken)
                => ValueTaskFactory.CompletedTask;
        }

        public readonly struct Update
        {
            public readonly Guid ModuleId;
            public readonly ImmutableArray<byte> ILDelta;
            public readonly ImmutableArray<byte> MetadataDelta;
            public readonly ImmutableArray<byte> PdbDelta;
            public readonly ImmutableArray<int> UpdatedTypes;

            public Update(Guid moduleId, ImmutableArray<byte> ilDelta, ImmutableArray<byte> metadataDelta, ImmutableArray<byte> pdbDelta, ImmutableArray<int> updatedTypes)
            {
                ModuleId = moduleId;
                ILDelta = ilDelta;
                MetadataDelta = metadataDelta;
                PdbDelta = pdbDelta;
                UpdatedTypes = updatedTypes;
            }
        }

        private static readonly ActiveStatementSpanProvider s_solutionActiveStatementSpanProvider =
            (_, _, _) => ValueTaskFactory.FromResult(ImmutableArray<ActiveStatementSpan>.Empty);

        private readonly IEditAndContinueWorkspaceService _encService;
        private readonly Func<ValueTask<ImmutableArray<string>>> _capabilitiesProvider;
        private DebuggingSessionId _sessionId;

        internal HotReloadService(SolutionServices services, Func<ValueTask<ImmutableArray<string>>> capabilitiesProvider)
        {
            _encService = services.GetRequiredService<IEditAndContinueWorkspaceService>();
            _capabilitiesProvider = capabilitiesProvider;
            AbstractEditAndContinueAnalyzer.EnableProjectLevelAnalysis = true;
        }

        public HotReloadService(HostWorkspaceServices services, ImmutableArray<string> capabilities)
            : this(services.SolutionServices, () => ValueTaskFactory.FromResult(capabilities))
        {
        }

        /// <summary>
        /// Starts the watcher.
        /// </summary>
        /// <param name="solution">Solution that represents sources that match the built binaries on disk.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task StartSessionAsync(Solution solution, CancellationToken cancellationToken)
        {
            var newSessionId = await _encService.StartDebuggingSessionAsync(
                solution,
                new DebuggerService(_capabilitiesProvider),
                NullPdbMatchingSourceTextProvider.Instance,
                captureMatchingDocuments: ImmutableArray<DocumentId>.Empty,
                captureAllMatchingDocuments: true,
                reportDiagnostics: false,
                cancellationToken).ConfigureAwait(false);
            Contract.ThrowIfFalse(_sessionId == default, "Session already started");
            _sessionId = newSessionId;
        }

        /// <summary>
        /// Invoke when capabilities have changed.
        /// </summary>
        public void CapabilitiesChanged()
        {
            _encService.BreakStateOrCapabilitiesChanged(GetDebuggingSession(), inBreakState: null, out _);
        }

        /// <summary>
        /// Emits updates for all projects that differ between the given <paramref name="solution"/> snapshot and the one given to the previous successful call or
        /// the one passed to <see cref="StartSessionAsync(Solution, CancellationToken)"/> for the first invocation.
        /// </summary>
        /// <param name="solution">Solution snapshot.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// Updates (one for each changed project) and Rude Edit diagnostics. Does not include syntax or semantic diagnostics.
        /// </returns>
        public async Task<(ImmutableArray<Update> updates, ImmutableArray<Diagnostic> diagnostics)> EmitSolutionUpdateAsync(Solution solution, CancellationToken cancellationToken)
        {
            var sessionId = GetDebuggingSession();

            var results = await _encService.EmitSolutionUpdateAsync(sessionId, solution, s_solutionActiveStatementSpanProvider, cancellationToken).ConfigureAwait(false);

            if (results.ModuleUpdates.Status == ModuleUpdateStatus.Ready)
            {
                _encService.CommitSolutionUpdate(sessionId, out _);
            }

            var updates = results.ModuleUpdates.Updates.SelectAsArray(
                update => new Update(update.Module, update.ILDelta, update.MetadataDelta, update.PdbDelta, update.UpdatedTypes));

            var diagnostics = await results.GetAllDiagnosticsAsync(solution, cancellationToken).ConfigureAwait(false);

            return (updates, diagnostics);
        }

        public void EndSession()
        {
            _encService.EndDebuggingSession(GetDebuggingSession(), out _);
            _sessionId = default;
        }

        private DebuggingSessionId GetDebuggingSession()
        {
            var sessionId = _sessionId;
            Contract.ThrowIfFalse(sessionId != default, "Session has not started");
            return sessionId;
        }

        internal TestAccessor GetTestAccessor()
            => new(this);

        internal readonly struct TestAccessor
        {
            private readonly HotReloadService _instance;

            internal TestAccessor(HotReloadService instance)
                => _instance = instance;

            public DebuggingSessionId SessionId
                => _instance._sessionId;
        }
    }
}
