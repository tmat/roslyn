// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Scripting.Hosting
{
    internal static class ReplUtilities
    {
        public static IEnumerable<ValueTuple<string, ConsoleColor>> FormatDiagnostics(IEnumerable<Diagnostic> diagnostics, int maxDisplayCount = 5)
        {
            // by severity, then by location
            var ordered = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning).OrderBy((d1, d2) =>
            {
                int delta = (int)d2.Severity - (int)d1.Severity;
                return (delta != 0) ? delta : d1.Location.SourceSpan.Start - d2.Location.SourceSpan.Start;
            });

            int errorCount = 0;
            int warningCount = 0;
            foreach (var diagnostic in ordered)
            {
                ConsoleColor color;
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    errorCount++;
                    color = ConsoleColor.Red;
                }
                else
                {
                    warningCount++;
                    color = ConsoleColor.Yellow;
                }

                if (errorCount + warningCount <= maxDisplayCount)
                {
                    yield return ValueTuple.Create(diagnostic.ToString(), color);
                }
            }

            int diagnosticsNotShown = errorCount + warningCount - maxDisplayCount;
            if (diagnosticsNotShown > 0)
            {
                if (errorCount > maxDisplayCount)
                {
                    // not all errors were displayed, ignore warnings they are not important:
                    yield return ValueTuple.Create(
                        string.Format(ScriptingResources.PlusAdditionalErrors, errorCount - maxDisplayCount), ConsoleColor.DarkRed);
                }
                else
                {
                    // there are no additional errors, report the number of warnigns not shown:
                    yield return ValueTuple.Create(
                        string.Format(ScriptingResources.PlusAdditionalWarnings, diagnosticsNotShown), ConsoleColor.DarkYellow);
                }
            }
        }
    }
}
