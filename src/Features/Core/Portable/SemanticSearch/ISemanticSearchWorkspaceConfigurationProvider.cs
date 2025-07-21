// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Composition;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.CodeAnalysis.SemanticSearch;

internal interface ISemanticSearchProjectBuilder : IWorkspaceService
{
    DocumentId GetQueryDocumentId(Solution solution);
    (WorkspaceChangeKind changeKind, ProjectId? projectId, DocumentId? documentId) GetWorkspaceChangeKind(Solution oldSolution, Solution newSolution);
    Solution SetQueryText(Solution solution, string? query, out SourceText? newText);
}

[ExportWorkspaceService(typeof(ISemanticSearchProjectBuilder), workspaceKinds: [WorkspaceKind.SemanticSearch]), Shared]
[method: ImportingConstructor]
[method: Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
internal sealed class SemanticSearchProjectBuilder() : ISemanticSearchProjectBuilder
{
    public DocumentId GetQueryDocumentId(Solution solution)
        => SemanticSearchUtilities.GetQueryDocumentId(solution);

    public (WorkspaceChangeKind changeKind, ProjectId? projectId, DocumentId? documentId) GetWorkspaceChangeKind(Solution oldSolution, Solution newSolution)
        => oldSolution.Projects.Any()
        ? (WorkspaceChangeKind.DocumentChanged, projectId: null, documentId: SemanticSearchUtilities.GetQueryDocumentId(newSolution))
        : (WorkspaceChangeKind.ProjectAdded, projectId: SemanticSearchUtilities.GetQueryProjectId(newSolution), documentId: null);

    public Solution SetQueryText(Solution solution, string? query, out SourceText? newText)
    {
        if (solution.Projects.Any())
        {
            if (query == null)
            {
                // already have a content, don't reset it to default:
                newText = null;
                return solution;
            }

            newText = SemanticSearchUtilities.CreateSourceText(query);
            return solution.WithDocumentText(SemanticSearchUtilities.GetQueryDocumentId(solution), newText);
        }

        newText = SemanticSearchUtilities.CreateSourceText(query ?? config.Query);
        var metadataService = solution.Services.GetRequiredService<IMetadataService>();

        return solution
            .AddProject(name: SemanticSearchUtilities.QueryProjectName, assemblyName: SemanticSearchUtilities.QueryProjectName, config.Language)
            .WithCompilationOptions(config.CompilationOptions)
            .WithParseOptions(config.ParseOptions)
            .AddMetadataReferences(SemanticSearchUtilities.GetMetadataReferences(metadataService, SemanticSearchUtilities.ReferenceAssembliesDirectory))
            .AddDocument(name: SemanticSearchUtilities.QueryDocumentName, newText, filePath: SemanticSearchUtilities.GetDocumentFilePath(config.Language)).Project
            .AddDocument(name: SemanticSearchUtilities.GlobalUsingsAndToolsDocumentName, SemanticSearchUtilities.CreateSourceText(config.GlobalUsingsAndTools), filePath: null).Project
            .AddAnalyzerConfigDocument(name: SemanticSearchUtilities.ConfigDocumentName, SemanticSearchUtilities.CreateSourceText(config.EditorConfig), filePath: SemanticSearchUtilities.GetConfigDocumentFilePath()).Project.Solution;
    }
}
