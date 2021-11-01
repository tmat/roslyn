// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Indentation;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.SmartIndent
{
    internal partial class SmartIndent : ISmartIndent
    {
        private readonly ITextView _textView;

        public SmartIndent(ITextView textView)
            => _textView = textView ?? throw new ArgumentNullException(nameof(textView));

        public int? GetDesiredIndentation(ITextSnapshotLine line)
            => GetDesiredIndentation(line, CancellationToken.None);

        public void Dispose()
        {
        }

        private int? GetDesiredIndentation(ITextSnapshotLine line, CancellationToken cancellationToken)
        {
            if (line == null)
                throw new ArgumentNullException(nameof(line));

            using (Logger.LogBlock(FunctionId.SmartIndentation_Start, cancellationToken))
            {
                var document = line.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
                if (document == null)
                    return null;

                var indentationService = document.GetLanguageService<IIndentationService>();
                if (indentationService == null)
                    return null;

                var services = document.Project.Solution.Workspace.Services;
                var optionService = services.GetRequiredService<IOptionService>();
                var documentOptions = document.GetOptionsAsync(cancellationToken).WaitAndGetResult_CanCallOnBackground(cancellationToken);
                var formattingService = document.GetRequiredLanguageService<ISyntaxFormattingService>();
                var options = formattingService.GetOptions(documentOptions.AsAnalyzerConfigOptions(optionService, document.Project.Language));

                var result = indentationService.GetIndentation(document, line.LineNumber, options, cancellationToken);
                return result.GetIndentation(_textView, line);
            }
        }
    }
}
