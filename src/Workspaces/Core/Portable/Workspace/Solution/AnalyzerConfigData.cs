// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.CodeAnalysis;

/// <summary>
/// Aggregate analyzer config options for a specific path.
/// </summary>
internal readonly struct AnalyzerConfigData(AnalyzerConfigOptionsResult result, ImmutableDictionary<string, string> fallbackOptions)
{
    private sealed class DictionaryConfigOptionsWithFallback(ImmutableDictionary<string, string> options, ImmutableDictionary<string, string> fallback) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            => options.TryGetValue(key, out value) || fallback.TryGetValue(key, out value);

        public override IEnumerable<string> Keys
            => options.Keys.Union(fallback.Keys);
    }

    public readonly StructuredAnalyzerConfigOptions ConfigOptions = StructuredAnalyzerConfigOptions.Create(
        new DictionaryConfigOptionsWithFallback(result.AnalyzerOptions, fallbackOptions));

    /// <summary>
    /// These options do not fall back.
    /// </summary>
    public readonly ImmutableDictionary<string, ReportDiagnostic> TreeOptions = result.TreeOptions;
}
