// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Indentation;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.VisualStudio.LanguageServices.Indentation
{
    [ExportWorkspaceService(typeof(IInferredIndentationService), ServiceLayer.Editor), Shared]
    internal sealed class EditorLayerInferredIndentationService : IInferredIndentationService
    {
        private readonly IIndentationManagerService _indentationManagerService;

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public EditorLayerInferredIndentationService(IIndentationManagerService indentationManagerService)
        {
            _indentationManagerService = indentationManagerService;
        }

        public async Task<InferredIndentationOptions?> TryInferIndentationAsync(Document document, bool explicitFormat, CancellationToken cancellationToken)
        {
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = text.FindCorrespondingEditorTextSnapshot();
            if (snapshot == null)
            {
                return null;
            }

            _indentationManagerService.GetIndentation(snapshot.TextBuffer, explicitFormat, out var convertTabsToSpaces, out var tabSize, out var indentSize);

            return new InferredIndentationOptions(
                IndentationSize: indentSize,
                TabSize: tabSize,
                UseTabs: !convertTabsToSpaces);
        }
    }
}
