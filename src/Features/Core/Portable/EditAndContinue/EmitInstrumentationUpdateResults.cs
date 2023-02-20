// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Runtime.Serialization;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.CodeAnalysis.EditAndContinue;

internal readonly struct EmitInstrumentationUpdateResults
{
    [DataContract]
    internal readonly struct Data
    {
        [DataMember]
        public required ModuleUpdates ModuleUpdates { get; init; }

        [DataMember]
        public required ImmutableArray<DiagnosticData> Diagnostics { get; init; }
    }

    public static readonly EmitInstrumentationUpdateResults Empty = new()
    {
        ModuleUpdates = new ModuleUpdates(ModuleUpdateStatus.None, ImmutableArray<ModuleUpdate>.Empty),
        Diagnostics = ImmutableArray<ProjectDiagnostics>.Empty,
    };

    public required ModuleUpdates ModuleUpdates { get; init; }
    public required ImmutableArray<ProjectDiagnostics> Diagnostics { get; init; }

    public Data Dehydrate(Solution solution)
        => new()
        {
            ModuleUpdates = ModuleUpdates,
            Diagnostics = Diagnostics.ToDiagnosticData(solution),
        };
}
