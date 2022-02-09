// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal static class IdeAnalyzerOptionsStorage
    {
        public static IdeAnalyzerOptions GetIdeAnalyzerOptions(this IGlobalOptionService globalOptions, string language)
            => new(
                FadeOutUnusedImports: globalOptions.GetOption(FadeOutUnusedImports, language),
                FadeOutUnreachableCode: globalOptions.GetOption(FadeOutUnusedImports, language),
                ReportInvalidPlaceholdersInStringDotFormatCalls: globalOptions.GetOption(ReportInvalidPlaceholdersInStringDotFormatCalls, language),
                ReportInvalidRegexPatterns: globalOptions.GetOption(ReportInvalidRegexPatterns, language));

        public static readonly PerLanguageOption2<bool> FadeOutUnusedImports = new(
            "FadingOptions", "FadeOutUnusedImports", IdeAnalyzerOptions.Default.FadeOutUnusedImports,
            storageLocation: new RoamingProfileStorageLocation($"TextEditor.%LANGUAGE%.Specific.FadeOutUnusedImports"));

        public static readonly PerLanguageOption2<bool> FadeOutUnreachableCode = new(
            "FadingOptions", "FadeOutUnreachableCode", IdeAnalyzerOptions.Default.FadeOutUnreachableCode,
            storageLocation: new RoamingProfileStorageLocation($"TextEditor.%LANGUAGE%.Specific.FadeOutUnreachableCode"));

        public static readonly PerLanguageOption2<bool> ReportInvalidPlaceholdersInStringDotFormatCalls =
            new("ValidateFormatStringOption", "ReportInvalidPlaceholdersInStringDotFormatCalls", IdeAnalyzerOptions.Default.ReportInvalidPlaceholdersInStringDotFormatCalls,
                storageLocation: new RoamingProfileStorageLocation("TextEditor.%LANGUAGE%.Specific.WarnOnInvalidStringDotFormatCalls"));

        public static readonly PerLanguageOption2<bool> ReportInvalidRegexPatterns =
            new("RegularExpressionsOptions", "ReportInvalidRegexPatterns", IdeAnalyzerOptions.Default.ReportInvalidRegexPatterns,
                storageLocation: new RoamingProfileStorageLocation("TextEditor.%LANGUAGE%.Specific.ReportInvalidRegexPatterns"));

    }
}
