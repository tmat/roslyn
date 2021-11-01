' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Threading
Imports Microsoft.CodeAnalysis.Formatting
Imports Microsoft.CodeAnalysis.Formatting.Rules
Imports Microsoft.CodeAnalysis.Host
Imports Microsoft.CodeAnalysis.Indentation
Imports Microsoft.CodeAnalysis.Options
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.Indentation
    Friend Class VisualBasicSmartTokenFormatter
        Implements ISmartTokenFormatter

        Private ReadOnly _options As FormatterOptions
        Private ReadOnly _formattingRules As IEnumerable(Of AbstractFormattingRule)

        Private ReadOnly _root As CompilationUnitSyntax

        Public Sub New(options As FormatterOptions,
                       formattingRules As IEnumerable(Of AbstractFormattingRule),
                       root As CompilationUnitSyntax)
            Contract.ThrowIfNull(options)
            Contract.ThrowIfNull(formattingRules)
            Contract.ThrowIfNull(root)

            _options = options
            _formattingRules = formattingRules

            Me._root = root
        End Sub

        Public Function FormatTokenAsync(workspaceServices As HostWorkspaceServices, token As SyntaxToken, cancellationToken As CancellationToken) As Tasks.Task(Of IList(Of TextChange)) Implements ISmartTokenFormatter.FormatTokenAsync
            Contract.ThrowIfTrue(token.Kind = SyntaxKind.None OrElse token.Kind = SyntaxKind.EndOfFileToken)

            ' get previous token
            Dim previousToken = token.GetPreviousToken()

            Dim languageFormatter = workspaceServices.GetLanguageServices(_root.Language).GetRequiredService(Of ISyntaxFormattingService)()
            Return Task.FromResult(languageFormatter.Format(
                _root,
                spans:={TextSpan.FromBounds(previousToken.SpanStart, token.Span.End)},
                _options,
                _formattingRules,
                cancellationToken).GetTextChanges(cancellationToken))
        End Function
    End Class
End Namespace
