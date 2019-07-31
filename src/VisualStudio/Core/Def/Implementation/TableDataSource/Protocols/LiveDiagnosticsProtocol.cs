// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.LanguageServices.Implementation.TaskList;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    internal abstract class LiveDiagnosticsProtocol
    {
        private readonly Workspace _workspace;
        private readonly ExternalErrorDiagnosticUpdateSource _externalErrorSource;
        private readonly IDiagnosticService _diagnosticService;

        public LiveDiagnosticsProtocol(Workspace workspace, ExternalErrorDiagnosticUpdateSource externalErrorSource, IDiagnosticService diagnosticService)
        {
            _workspace = workspace;
            _externalErrorSource = externalErrorSource;
            _diagnosticService = diagnosticService;
        }
    }

    [Export(typeof(VisualStudioLiveDiagnosticsProtocol))]
    internal sealed class VisualStudioLiveDiagnosticsProtocol : LiveDiagnosticsProtocol
    {
        [ImportingConstructor]
        public VisualStudioLiveDiagnosticsProtocol(VisualStudioWorkspace workspace, ExternalErrorDiagnosticUpdateSource externalErrorSource, IDiagnosticService diagnosticService)
            : base(workspace, externalErrorSource, diagnosticService)
        {

        }
    }
}
