// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Runtime.Serialization;
using Microsoft.CodeAnalysis.EditAndContinue.Contracts;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.EditAndContinue;

[DataContract]
internal sealed class MethodBodyInstrumentation : ManagedHotReloadInstrumentation
{
    /// <summary>
    /// Locations in source code that indicate which methods should be instrumented.
    /// The containing member method is instrumented.
    /// </summary>
    [DataMember]
    public required ImmutableArray<DocumentPosition> SourceLocations { get; init; }

    /// <summary>
    /// Kinds of instrumentations to be applied.
    /// </summary>
    [DataMember]
    public required ImmutableArray<InstrumentationKind> Kinds { get; init; }
}

internal readonly record struct DocumentPosition(DocumentId Document, int Position);

[DataContract]
internal sealed class MethodBodyInstrumentationResults : ManagedHotReloadInstrumentationResults
{
    [DataMember]
    public required ImmutableArray<MethodBodySourceLocationStatus> SourceLocationStatus { get; init; }

    internal override void Log(TraceLog log)
    {
        for (var i = 0; i < SourceLocationStatus.Length; i++)
        {
            log.Write("Status[{0}]: {1}", i, SourceLocationStatus[i]);
        }
    }
}

internal enum MethodBodySourceLocationStatus
{
    Success = 0,
    DocumentNotFound = 1,
    ProjectLanguageNotSupported = 2,
    ProjectNotLoaded = 3,
    ProjectNotBuilt = 4,
    ErrorReadingCompilationOutputs = 5,
    MethodNotFound = 6,
    RuntimeUnsupportedChanges = 7,
    CompilationErrors = 8,
}
