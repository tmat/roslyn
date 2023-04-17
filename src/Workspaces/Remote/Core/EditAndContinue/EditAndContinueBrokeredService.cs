// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.ErrorReporting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Remote;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Debugger.Contracts.HotReload;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.EditAndContinue;

[ExportBrokeredService]
internal sealed class EditAndContinueBrokeredService : IManagedHotReloadLanguageService
{
    private sealed class PdbMatchingSourceTextProvider : IPdbMatchingSourceTextProvider
    {
        public static readonly PdbMatchingSourceTextProvider Instance = new();

        // Returning null will check the file on disk:
        public ValueTask<string?> TryGetMatchingSourceTextAsync(string filePath, ImmutableArray<byte> requiredChecksum, SourceHashAlgorithm checksumAlgorithm, CancellationToken cancellationToken)
            => ValueTaskFactory.FromResult<string?>(null);
    }

    public const string ServiceName = "ManagedHotReloadLanguageService";
    public static readonly ServiceDescriptor ServiceDescriptor = ServiceDescriptor.CreateInProcServiceDescriptor(ServiceDescriptors.ComponentName, ServiceName, suffix: "", ServiceDescriptors.GetFeatureDisplayName);

    private static readonly ActiveStatementSpanProvider s_emptyActiveStatementProvider =
        (_, _, _) => ValueTaskFactory.FromResult(ImmutableArray<ActiveStatementSpan>.Empty);

    private readonly SolutionServices _services;
    private readonly SolutionSnapshotRegistry _solutionChangeTracker;
    private readonly Lazy<ISolutionSnapshotProvider> _solutionSnapshotProvider;
    private readonly Lazy<IManagedHotReloadService> _debuggerService;

    private bool _disabled;
    private DebuggingSessionId? _debuggingSession;
    private Solution? _committedDesignTimeSolution;
    private Solution? _pendingUpdatedDesignTimeSolution;

    public EditAndContinueBrokeredService(
        SolutionServices services,
        SolutionSnapshotRegistry solutionChangeTracker,
        Lazy<ISolutionSnapshotProvider> solutionSnapshotProvider,
        Lazy<IManagedHotReloadService> debuggerService)
    {
        _services = services;
        _solutionChangeTracker = solutionChangeTracker;
        _solutionSnapshotProvider = solutionSnapshotProvider;
        _debuggerService = debuggerService;
    }

    private void Disable()
    {
        _disabled = true;
        _debuggingSession = null;
        _committedDesignTimeSolution = null;
        _pendingUpdatedDesignTimeSolution = null;
        _solutionChangeTracker.Clear();
    }

    private async ValueTask<Solution> GetCurrentDesignTimeSolutionAsync(CancellationToken cancellationToken)
    {
        // First, calls to the client to get the current snapshot id.
        // The client service calls the LSP client, which sends message to the LSP server, which in turn calls back to RegisterSolutionSnapshot.
        // Once complete the snapshot should be registered.
        var id = await _solutionSnapshotProvider.Value.CaptureCurrentSolutionSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return _solutionChangeTracker.GetRegisteredSolutionSnapshot(id);
    }

    private IEditAndContinueWorkspaceService GetService()
        => _services.GetRequiredService<IEditAndContinueWorkspaceService>();

    private Solution GetCurrentCompileTimeSolution(Solution currentDesignTimeSolution)
        => _services.GetRequiredService<ICompileTimeSolutionProvider>().GetCompileTimeSolution(currentDesignTimeSolution);

    public async ValueTask StartSessionAsync(CancellationToken cancellationToken)
    {
        if (_disabled)
        {
            return;
        }

        try
        {
            var currentDesignTimeSolution = await GetCurrentDesignTimeSolutionAsync(cancellationToken).ConfigureAwait(false);
            _committedDesignTimeSolution = currentDesignTimeSolution;
            var compileTimeSolution = GetCurrentCompileTimeSolution(currentDesignTimeSolution);

            // TODO: use remote proxy once we transition to pull diagnostics
            _debuggingSession = await GetService().StartDebuggingSessionAsync(
                compileTimeSolution,
                _debuggerService.Value,
                PdbMatchingSourceTextProvider.Instance,
                captureMatchingDocuments: ImmutableArray<DocumentId>.Empty,
                captureAllMatchingDocuments: false,
                reportDiagnostics: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (FatalError.ReportAndCatchUnlessCanceled(e, cancellationToken))
        {
            // the service failed, error has been reported - disable further operations
            Disable();
        }
    }

    private ValueTask BreakStateOrCapabilitiesChangedAsync(bool? inBreakState, CancellationToken cancellationToken)
    {
        if (_disabled)
        {
            return ValueTaskFactory.CompletedTask;
        }

        try
        {
            Contract.ThrowIfNull(_debuggingSession);
            GetService().BreakStateOrCapabilitiesChanged(_debuggingSession.Value, inBreakState, out _);
        }
        catch (Exception e) when (FatalError.ReportAndCatchUnlessCanceled(e, cancellationToken))
        {
            Disable();
        }

        return ValueTaskFactory.CompletedTask;
    }

    public ValueTask EnterBreakStateAsync(CancellationToken cancellationToken)
        => BreakStateOrCapabilitiesChangedAsync(inBreakState: true, cancellationToken);

    public ValueTask ExitBreakStateAsync(CancellationToken cancellationToken)
        => BreakStateOrCapabilitiesChangedAsync(inBreakState: false, cancellationToken);

    public ValueTask OnCapabilitiesChangedAsync(CancellationToken cancellationToken)
        => BreakStateOrCapabilitiesChangedAsync(inBreakState: null, cancellationToken);

    public ValueTask CommitUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_disabled)
        {
            return ValueTaskFactory.CompletedTask;
        }

        try
        {
            Contract.ThrowIfNull(_debuggingSession);
            var committedDesignTimeSolution = Interlocked.Exchange(ref _pendingUpdatedDesignTimeSolution, null);
            Contract.ThrowIfNull(committedDesignTimeSolution);

            _committedDesignTimeSolution = committedDesignTimeSolution;

            GetService().CommitSolutionUpdate(_debuggingSession.Value, out _);
        }
        catch (Exception e) when (FatalError.ReportAndCatchUnlessCanceled(e, cancellationToken))
        {
            Disable();
        }

        return ValueTaskFactory.CompletedTask;
    }

    public ValueTask DiscardUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_disabled)
        {
            return ValueTaskFactory.CompletedTask;
        }

        try
        {
            Contract.ThrowIfNull(_debuggingSession);
            Contract.ThrowIfNull(Interlocked.Exchange(ref _pendingUpdatedDesignTimeSolution, null));

            GetService().DiscardSolutionUpdate(_debuggingSession.Value);
        }
        catch (Exception e) when (FatalError.ReportAndCatchUnlessCanceled(e, cancellationToken))
        {
            Disable();
        }

        return ValueTaskFactory.CompletedTask;
    }

    public ValueTask EndSessionAsync(CancellationToken cancellationToken)
    {
        if (_disabled)
        {
            return ValueTaskFactory.CompletedTask;
        }

        try
        {
            Contract.ThrowIfNull(_debuggingSession);

            GetService().EndDebuggingSession(_debuggingSession.Value, out _);

            _debuggingSession = null;
            _committedDesignTimeSolution = null;
            _pendingUpdatedDesignTimeSolution = null;
        }
        catch (Exception e) when (FatalError.ReportAndCatchUnlessCanceled(e, cancellationToken))
        {
            Disable();
        }

        return ValueTaskFactory.CompletedTask;
    }

    /// <summary>
    /// Returns true if any changes have been made to the source since the last changes had been applied.
    /// For performance reasons it only implements a heuristic and may return both false positives and false negatives.
    /// If the result is a false negative the debugger will not apply the changes unless the user explicitly triggers apply change command.
    /// The background diagnostic analysis will still report rude edits for these ignored changes. It may also happen that these rude edits 
    /// will disappear once the debuggee is resumed - if they are caused by presence of active statements around the change.
    /// If the result is a false positive the debugger attempts to apply the changes, which will result in a delay but will correctly end up
    /// with no actual deltas to be applied.
    /// 
    /// If <paramref name="sourceFilePath"/> is specified checks for changes only in a document of the given path.
    /// This is not supported (returns false) for source-generated documents.
    /// </summary>
    public async ValueTask<bool> HasChangesAsync(string? sourceFilePath, CancellationToken cancellationToken)
    {
        try
        {
            var debuggingSession = _debuggingSession;
            if (debuggingSession == null)
            {
                return false;
            }

            Contract.ThrowIfNull(_committedDesignTimeSolution);
            var oldSolution = _committedDesignTimeSolution;

            var newSolution = await GetCurrentDesignTimeSolutionAsync(cancellationToken).ConfigureAwait(false);

            return (sourceFilePath != null)
                ? await EditSession.HasChangesAsync(oldSolution, newSolution, sourceFilePath, cancellationToken).ConfigureAwait(false)
                : await EditSession.HasChangesAsync(oldSolution, newSolution, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (FatalError.ReportAndCatchUnlessCanceled(e, cancellationToken))
        {
            return true;
        }
    }

    public async ValueTask<ManagedHotReloadUpdates> GetUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_disabled)
        {
            return new ManagedHotReloadUpdates(ImmutableArray<ManagedHotReloadUpdate>.Empty, ImmutableArray<ManagedHotReloadDiagnostic>.Empty);
        }

        try
        {
            Contract.ThrowIfNull(_debuggingSession);

            var designTimeSolution = await GetCurrentDesignTimeSolutionAsync(cancellationToken).ConfigureAwait(false);
            var solution = GetCurrentCompileTimeSolution(designTimeSolution);

            ModuleUpdates moduleUpdates;
            ImmutableArray<DiagnosticData> diagnosticData;
            ImmutableArray<(DocumentId DocumentId, ImmutableArray<RudeEditDiagnostic> Diagnostics)> rudeEdits;
            DiagnosticData? syntaxError;

            try
            {
                var results = await GetService().EmitSolutionUpdateAsync(_debuggingSession.Value, solution, s_emptyActiveStatementProvider, cancellationToken).ConfigureAwait(false);

                moduleUpdates = results.ModuleUpdates;
                diagnosticData = results.GetDiagnosticData(solution);
                rudeEdits = results.RudeEdits;
                syntaxError = results.GetSyntaxErrorData(solution);
            }
            catch (Exception e) when (FatalError.ReportAndCatchUnlessCanceled(e, cancellationToken))
            {
                var descriptor = EditAndContinueDiagnosticDescriptors.GetDescriptor(RudeEditKind.InternalError);

                var diagnostic = Diagnostic.Create(
                    descriptor,
                    Location.None,
                    string.Format(descriptor.MessageFormat.ToString(), "", e.Message));

                diagnosticData = ImmutableArray.Create(DiagnosticData.Create(designTimeSolution, diagnostic, project: null));
                rudeEdits = ImmutableArray<(DocumentId DocumentId, ImmutableArray<RudeEditDiagnostic> Diagnostics)>.Empty;
                moduleUpdates = new ModuleUpdates(ModuleUpdateStatus.RestartRequired, ImmutableArray<ManagedHotReloadUpdate>.Empty);
                syntaxError = null;
            }

            // Only store the solution if we have any changes to apply, otherwise CommitUpdatesAsync/DiscardUpdatesAsync won't be called.
            if (moduleUpdates.Status == ModuleUpdateStatus.Ready)
            {
                _pendingUpdatedDesignTimeSolution = designTimeSolution;
            }

            var diagnostics = await EmitSolutionUpdateResults.GetHotReloadDiagnosticsAsync(solution, diagnosticData, rudeEdits, syntaxError, moduleUpdates.Status, cancellationToken).ConfigureAwait(false);
            return new ManagedHotReloadUpdates(moduleUpdates.Updates, diagnostics);
        }
        catch (Exception e) when (FatalError.ReportAndCatchUnlessCanceled(e, cancellationToken))
        {
            Disable();
            return new ManagedHotReloadUpdates(ImmutableArray<ManagedHotReloadUpdate>.Empty, ImmutableArray<ManagedHotReloadDiagnostic>.Empty);
        }
    }
}
