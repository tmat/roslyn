// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.Emit
{
    /// <summary>
    /// The result of the Compilation.Emit method.
    /// </summary>
    [DebuggerDisplay("{GetDebuggerDisplay(), nq}")]
    public class EmitResult
    {
        /// <summary>
        /// True if the compilation successfully produced an executable.
        /// If false then the diagnostics should include at least one error diagnostic
        /// indicating the cause of the failure.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// A list of all the diagnostics associated with compilations. This include parse errors, declaration errors,
        /// compilation errors, and emitting errors.
        /// </summary>
        public ImmutableArray<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// If <see cref="Success"/> is true, returns metadata tokens emitted for the symbols specified in
        /// metadataTokenRequests parameter of the <see cref="Compilation.Emit(System.IO.Stream, System.IO.Stream?, System.IO.Stream?, System.IO.Stream?, System.IO.Stream?, System.Collections.Generic.IEnumerable{ResourceDescription}?, EmitOptions?, IMethodSymbol?, System.IO.Stream?, System.Collections.Generic.IEnumerable{EmbeddedText}?, System.Collections.Generic.IEnumerable{ISymbol}, RebuildData?, CodeGen.CompilationTestData?, System.Threading.CancellationToken)"/>
        /// method. If the specified symbol does not have a token in the emitted image the corresponding item in this array will be 0.
        /// </summary>
        public ImmutableArray<int> RequestedMetadataTokens { get; }

        internal EmitResult(bool success, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<int> requestedMetadataTokens)
        {
            Success = success;
            Diagnostics = diagnostics;
            RequestedMetadataTokens = requestedMetadataTokens;
        }

        protected virtual string GetDebuggerDisplay()
        {
            string result = "Success = " + (Success ? "true" : "false");
            if (Diagnostics != null)
            {
                result += ", Diagnostics.Count = " + Diagnostics.Length;
            }
            else
            {
                result += ", Diagnostics = null";
            }

            return result;
        }
    }
}
