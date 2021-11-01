// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Formatting.Rules;
using Microsoft.CodeAnalysis.Shared.Collections;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Diagnostics;

#if !CODE_STYLE
using System;
using System.Composition;
using Microsoft.CodeAnalysis.Host.Mef;
#endif

namespace Microsoft.CodeAnalysis.CSharp.Formatting
{
#if !CODE_STYLE
    [ExportLanguageService(typeof(ISyntaxFormattingService), LanguageNames.CSharp), Shared]
#endif
    internal class CSharpSyntaxFormattingService : AbstractSyntaxFormattingService
    {
        private readonly ImmutableList<AbstractFormattingRule> _rules;

#if CODE_STYLE
        public static readonly CSharpSyntaxFormattingService Instance = new();

#else
        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
#endif
        public CSharpSyntaxFormattingService()
        {
            _rules = ImmutableList.Create<AbstractFormattingRule>(
                new WrappingFormattingRule(CSharpFormatterOptions.Default),
                new SpacingFormattingRule(CSharpFormatterOptions.Default),
                new NewLineUserSettingFormattingRule(CSharpFormatterOptions.Default),
                new IndentUserSettingsFormattingRule(CSharpFormatterOptions.Default),
                new ElasticTriviaFormattingRule(),
                new EndOfFileTokenFormattingRule(),
                new StructuredTriviaFormattingRule(),
                new IndentBlockFormattingRule(CSharpFormatterOptions.Default),
                new SuppressFormattingRule(),
                new AnchorIndentationFormattingRule(),
                new QueryExpressionFormattingRule(CSharpFormatterOptions.Default),
                new TokenBasedFormattingRule(CSharpFormatterOptions.Default),
                DefaultOperationProvider.Instance);
        }

        public override FormatterOptions GetOptions(AnalyzerConfigOptions config)
            => CSharpFormatterOptions.From(config);

        public override IEnumerable<AbstractFormattingRule> GetDefaultFormattingRules()
            => _rules;

        protected override IFormattingResult CreateAggregatedFormattingResult(SyntaxNode node, IList<AbstractFormattingResult> results, SimpleIntervalTree<TextSpan, TextSpanIntervalIntrospector>? formattingSpans = null)
            => new AggregatedFormattingResult(node, results, formattingSpans);

        protected override AbstractFormattingResult Format(SyntaxNode node, FormatterOptions options, IEnumerable<AbstractFormattingRule> formattingRules, SyntaxToken token1, SyntaxToken token2, CancellationToken cancellationToken)
            => new CSharpFormatEngine(node, (CSharpFormatterOptions)options, formattingRules, token1, token2).Format(cancellationToken);
    }
}
