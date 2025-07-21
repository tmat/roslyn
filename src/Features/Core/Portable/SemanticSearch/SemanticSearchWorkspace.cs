// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.SemanticSearch;

internal abstract class SemanticSearchWorkspace(HostServices services)
    : Workspace(services, WorkspaceKind.SemanticSearch)
{
    public override bool CanOpenDocuments
        => true;

    public override bool CanApplyChange(ApplyChangesKind feature)
        => feature == ApplyChangesKind.ChangeDocument;

    public async Task<Document> UpdateQueryDocumentAsync(string? query, CancellationToken cancellationToken)
    {
        var projectBuilder = Services.GetRequiredService<ISemanticSearchProjectBuilder>();

        SourceText? newText = null;

        var (_, newSolution) = await SetCurrentSolutionAsync(
            useAsync: true,
            transformation: oldSolution => projectBuilder.SetQueryText(oldSolution, query, out newText),
            changeKind: projectBuilder.GetWorkspaceChangeKind,
            onBeforeUpdate: null,
            onAfterUpdate: null,
            cancellationToken).ConfigureAwait(false);

        var queryDocument = SemanticSearchUtilities.GetQueryDocument(newSolution);

        if (newText != null)
        {
            ApplyQueryDocumentTextChanged(newText);
        }

        return queryDocument;
    }

    protected virtual void ApplyQueryDocumentTextChanged(SourceText newText)
    {
    }
}
