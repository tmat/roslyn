﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.ErrorReporting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Diagnostics;

/// <summary>
/// Updates <see cref="Project.FallbackAnalyzerOptions"/> of all affected projects loaded in given workspaces.
/// </summary>
[Export]
[ExportEventListener(WellKnownEventListeners.Workspace, WorkspaceKind.Host, WorkspaceKind.Interactive, WorkspaceKind.SemanticSearch), Shared]
[method: ImportingConstructor]
[method: Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
internal sealed class VisualStudioAnalyzerOptionsWorkspaceUpdater() : IEventListener<object>
{
    private readonly IAsynchronousOperationListener _listener;

    public void StartListening(Workspace workspace, object serviceOpt)
    {
        //var setter = workspace.Services.GetService<ISolutionAnalyzerSetterWorkspaceService>();
        //if (setter != null)
        //{
        //    // fire and forget
        //    var token = _listener.BeginAsyncOperation(nameof(InitializeWorkspaceAsync));
        //    _ = Task.Run(() => InitializeWorkspaceAsync(setter)).CompletesAsyncOperation(token);
        //}
    }

    private async Task InitializeWorkspaceAsync(ISolutionAnalyzerSetterWorkspaceService setter)
    {
        try
        {
            var provider = await _providerFactory.GetOrCreateProviderAsync(CancellationToken.None).ConfigureAwait(false);

            var references = provider.GetAnalyzerReferencesInExtensions();
            LogWorkspaceAnalyzerCount(references.Length);
            setter.SetAnalyzerReferences(references.SelectAsArray(referenceAndId => (AnalyzerReference)referenceAndId.reference));
        }
        catch (Exception e) when (FatalError.ReportAndPropagate(e, ErrorSeverity.Diagnostic))
        {
            throw ExceptionUtilities.Unreachable();
        }
    }

    private static void LogWorkspaceAnalyzerCount(int analyzerCount)
    {
        Logger.Log(FunctionId.DiagnosticAnalyzerService_Analyzers, KeyValueLogMessage.Create(m => m["AnalyzerCount"] = analyzerCount, LogLevel.Debug));
    }
}
