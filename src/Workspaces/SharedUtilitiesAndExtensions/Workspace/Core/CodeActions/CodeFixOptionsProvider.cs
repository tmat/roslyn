// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.Extensions;

// to avoid excessive #ifdefs
#pragma warning disable CA1822 // Mark members as static
#pragma warning disable IDE0052 // Remove unread private members

namespace Microsoft.CodeAnalysis.CodeActions;

internal readonly struct CodeFixOptionsProvider(IOptionsReader options, HostLanguageServices languageServices)
{
    // LineFormattingOptions

    public string NewLine => GetOption(FormattingOptions2.NewLine);

    public LineFormattingOptions GetLineFormattingOptions()
        => options.GetLineFormattingOptions(languageServices.Language);

    // SyntaxFormattingOptions

    public SyntaxFormattingOptions GetFormattingOptions(ISyntaxFormatting formatting)
        => formatting.GetFormattingOptions(options);

    public AccessibilityModifiersRequired AccessibilityModifiersRequired => options.GetOptionValue(CodeStyleOptions2.AccessibilityModifiersRequired, languageServices.Language);

    private TValue GetOption<TValue>(PerLanguageOption2<TValue> option)
        => options.GetOption(option, languageServices.Language);
}

internal static class CodeFixOptionsProviders
{
    public static async ValueTask<CodeFixOptionsProvider> GetCodeFixOptionsAsync(this Document document, CancellationToken cancellationToken)
    {
        var configOptions = await document.GetAnalyzerConfigOptionsAsync(cancellationToken).ConfigureAwait(false);
        return new CodeFixOptionsProvider(configOptions.GetOptionsReader(), document.Project.GetExtendedLanguageServices());
    }
}
