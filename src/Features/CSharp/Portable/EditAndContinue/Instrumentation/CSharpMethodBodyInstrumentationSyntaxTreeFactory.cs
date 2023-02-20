// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis.EditAndContinue;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.EditAndContinue;

[ExportLanguageService(typeof(IMethodBodyInstrumentationSyntaxTreeFactory), LanguageNames.CSharp), Shared]
internal class CSharpMethodBodyInstrumentationSyntaxTreeFactory : IMethodBodyInstrumentationSyntaxTreeFactory
{
    private static readonly CSharpParseOptions s_parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [ImportingConstructor]
    [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
    public CSharpMethodBodyInstrumentationSyntaxTreeFactory()
    {
    }

    public SyntaxTree GetLocalStoreTrackerSourceTree(CancellationToken cancellationToken)
    {
        var assembly = typeof(CSharpMethodBodyInstrumentationSyntaxTreeFactory).Assembly;

        using var stream = assembly.GetManifestResourceStream("Microsoft.CodeAnalysis.CSharp.LocalStoreTracker.cs");
        Contract.ThrowIfNull(stream);

        var sourceText = SourceText.From(stream, Encoding.UTF8, SourceHashAlgorithm.Sha256, throwIfBinaryDetected: false);
        return SyntaxFactory.ParseSyntaxTree(sourceText, s_parseOptions, path: "", cancellationToken);
    }

    public CompilationOptions UpdateCompilationOptions(CompilationOptions options)
        => ((CSharpCompilationOptions)options).WithAllowUnsafe(true);
}
