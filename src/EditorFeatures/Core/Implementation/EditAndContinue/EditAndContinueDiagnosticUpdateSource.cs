// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.Editor.Implementation.EditAndContinue
{
    [Export(typeof(EditAndContinueDiagnosticUpdateSource))]
    [Shared]
    internal sealed class EditAndContinueDiagnosticUpdateSource : IDiagnosticUpdateSource
    {
        private sealed class EncErrorId : BuildToolId.Base<object>
        {
            public EncErrorId(object errorId)
                : base(errorId)
            {
            }

            public override string BuildTool => PredefinedBuildTools.EnC;
        }

        internal static readonly BuildToolId InternalErrorId = new EncErrorId(new object());
        internal static readonly BuildToolId EmitErrorId = new EncErrorId(new object());

        [ImportingConstructor]
        public EditAndContinueDiagnosticUpdateSource(IDiagnosticUpdateSourceRegistrationService registrationService)
        {
            registrationService.Register(this);
        }

        public bool SupportGetDiagnostics => false;

        public event EventHandler<DiagnosticsUpdatedArgs> DiagnosticsUpdated;

        public ImmutableArray<DiagnosticData> GetDiagnostics(Workspace workspace, ProjectId projectId, DocumentId documentId, object id, bool includeSuppressedDiagnostics = false, CancellationToken cancellationToken = default)
        {
            return ImmutableArray<DiagnosticData>.Empty;
        }

        public void ClearDiagnostics(BuildToolId errorId, Solution solution, ProjectId projectId, ImmutableArray<DocumentId> documentIds)
        {
            // clear project diagnostics:
            ClearDiagnostics(errorId, solution, projectId, null);

            // clear document diagnostics:
            foreach (var documentIdOpt in documentIds)
            {
                ClearDiagnostics(errorId, solution, projectId, documentIdOpt);
            }
        }

        public void ClearDiagnostics(BuildToolId errorId, Solution solution, ProjectId projectId, DocumentId documentIdOpt)
        {
            DiagnosticsUpdated?.Invoke(this, DiagnosticsUpdatedArgs.DiagnosticsRemoved(
                errorId,
                solution.Workspace,
                solution: solution,
                projectId: projectId,
                documentId: documentIdOpt));
        }

        /// <summary>
        /// Reports given set of diagnostics. 
        /// Categorizes diagnostic into two groups - diagnostics associated with a docuemnt and diagnostics associated with a project or solution.
        /// </summary>
        /// <returns>Returns ids of documents that belong to <paramref name="projectIdOpt"/> and containing one or more diagnostics.</returns>
        public ImmutableArray<DocumentId> ReportDiagnostics(BuildToolId errorId, Solution solution, ProjectId projectIdOpt, IEnumerable<Diagnostic> diagnostics)
        {
            Debug.Assert(errorId != null);
            Debug.Assert(solution != null);

            var updateEvent = DiagnosticsUpdated;
            var documentIds = PooledHashSet<DocumentId>.GetInstance();
            var documentDiagnosticData = ArrayBuilder<DiagnosticData>.GetInstance();
            var nonDocumentDiagnosticData = ArrayBuilder<DiagnosticData>.GetInstance();
            var workspace = solution.Workspace;
            var project = (projectIdOpt != null) ? solution.GetProject(projectIdOpt) : null;

            foreach (var diagnostic in diagnostics)
            {
                var documentOpt = solution.GetDocument(diagnostic.Location.SourceTree);

                if (documentOpt != null)
                {
                    if (updateEvent != null)
                    {
                        documentDiagnosticData.Add(DiagnosticData.Create(documentOpt, diagnostic));
                    }

                    // only add documents from the current project:
                    if (documentOpt.Project.Id == projectIdOpt)
                    {
                        documentIds.Add(documentOpt.Id);
                    }
                }
                else if (updateEvent != null)
                {
                    if (project != null)
                    {
                        nonDocumentDiagnosticData.Add(DiagnosticData.Create(project, diagnostic));
                    }
                    else
                    {
                        nonDocumentDiagnosticData.Add(DiagnosticData.Create(workspace, diagnostic));
                    }
                }
            }

            if (documentDiagnosticData.Count > 0)
            {
                foreach (var documentDiagnostics in documentDiagnosticData.ToDictionary(data => data.DocumentId))
                {
                    updateEvent(this, DiagnosticsUpdatedArgs.DiagnosticsCreated(
                        errorId,
                        workspace,
                        solution,
                        projectIdOpt,
                        documentId: documentDiagnostics.Key,
                        diagnostics: documentDiagnostics.Value));
                }
            }

            if (nonDocumentDiagnosticData.Count > 0)
            {
                updateEvent(this, DiagnosticsUpdatedArgs.DiagnosticsCreated(
                    errorId,
                    workspace,
                    solution,
                    projectIdOpt,
                    documentId: null,
                    diagnostics: nonDocumentDiagnosticData.ToImmutable()));
            }

            var result = documentIds.AsImmutableOrEmpty();
            documentDiagnosticData.Free();
            nonDocumentDiagnosticData.Free();
            documentIds.Free();
            return result;
        }
    }
}
