// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Shared.Extensions;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.EditAndContinue
{
    /// <summary>
    /// The solution retrieved from primary workspace represents design-time view of the source.
    /// Edit and Continue however needs to operate on a runtime view - the same sources that the compiler compiles into the binaries.
    /// The difference is only significant for Razor projects. Razor source generator is only enabled at compile-time, while at design-time
    /// the Razor VS tooling injects design-time files into the workspace.
    /// 
    /// This class is created with the design-time solution and it presents its runtime version on demand. The class caches the latest runtime snapshot
    /// to avoid running source generators too often.
    /// 
    /// Operations on projects and regular documents are just passed through to the underlying solution.
    /// Operations on source-generated files ensure that these files are the runtime versions.
    /// 
    /// TODO: remove https://github.com/dotnet/roslyn/issues/51678
    /// </summary>
    internal sealed class RuntimeSolution
    {
        private Solution _solution;

        public RuntimeSolution(Solution solution)
        {
            _solution = solution;
        }

        public static DocumentId? GetRazorEncConfigDocumentId(Project? project)
            => project?.State.AnalyzerConfigDocumentStates.States
                .FirstOrDefault(state => PathUtilities.GetFileName(state.FilePath, includeExtension: true) == "RazorSourceGenerator.razorencconfig")?.Id;

        private Project? GetRuntimeProject(ProjectId projectId)
        {
            var project = _solution.GetProject(projectId);
            if (project == null)
            {
                return null;
            }

            var razorEncConfigId = GetRazorEncConfigDocumentId(project);
            if (razorEncConfigId == null)
            {
                return project;
            }

            if (ImmutableInterlocked.Update(ref _solution, solution =>
            {
                var razorEncConfigId = GetRazorEncConfigDocumentId(solution.GetProject(projectId));
                return (razorEncConfigId != null) ? solution.RemoveAnalyzerConfigDocument(razorEncConfigId) : solution;
            }))
            {
                return _solution.GetProject(projectId);
            }

            return project;
        }

        internal async ValueTask<Document?> GetDocumentAsync(DocumentId documentId, bool includeSourceGenerated, CancellationToken cancellationToken)
        {
            var document = _solution.GetDocument(documentId);
            if (document != null)
            {
                return document;
            }

            if (!includeSourceGenerated)
            {
                return null;
            }

            var project = GetRuntimeProject(documentId.ProjectId);
            if (project == null)
            {
                return null;
            }

            return await project.GetSourceGeneratedDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<TextDocumentStates<SourceGeneratedDocumentState>> GetSourceGeneratedDocumentStatesAsync(ProjectId projectId, CancellationToken cancellationToken)
        {
            var project = GetRuntimeProject(projectId);
            if (project == null)
            {
                return ValueTaskFactory.FromResult(TextDocumentStates<SourceGeneratedDocumentState>.Empty);
            }

            return project.Solution.State.GetSourceGeneratedDocumentStatesAsync(project.State, cancellationToken);
        }

        public ValueTask<SourceGeneratedDocument?> GetSourceGeneratedDocumentAsync(DocumentId documentId, CancellationToken cancellationToken)
        {
            var project = GetRuntimeProject(documentId.ProjectId);
            if (project == null)
            {
                return ValueTaskFactory.FromResult<SourceGeneratedDocument?>(null);
            }

            return project.GetSourceGeneratedDocumentAsync(documentId, cancellationToken);
        }

        public OptionSet Options
            => _solution.Options;

        public IEnumerable<Project> Projects
            => _solution.Projects;

        public Project? GetProject(ProjectId? projectId)
            => _solution.GetProject(projectId);

        public Project GetRequiredProject(ProjectId projectId)
            => _solution.GetRequiredProject(projectId);

        public ImmutableArray<DocumentId> GetDocumentIdsWithFilePath(string path)
            => _solution.GetDocumentIdsWithFilePath(path);

        public bool ContainsDocument(DocumentId documentId)
            => _solution.ContainsDocument(documentId);

        public Document? GetDocument(SyntaxTree? sourceTree)
            => _solution.GetDocument(sourceTree);

        public Document? GetDocument(DocumentId documentId)
            => _solution.GetDocument(documentId);

        public Document GetRequiredDocument(DocumentId documentId)
            => _solution.GetRequiredDocument(documentId);

        public RuntimeSolution WithDocumentText(DocumentId documentId, SourceText sourceText, PreservationMode preserveValue)
            => new(_solution.WithDocumentText(documentId, sourceText, preserveValue));

        public RuntimeSolution AddDocument(DocumentInfo documentInfo)
            => new(_solution.AddDocument(documentInfo));
    }
}
