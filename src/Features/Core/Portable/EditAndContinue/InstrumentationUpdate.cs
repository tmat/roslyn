// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Runtime.Serialization;
using Microsoft.CodeAnalysis.EditAndContinue.Contracts;

namespace Microsoft.CodeAnalysis.EditAndContinue;

internal readonly struct InstrumentationUpdate
{
    /// <summary>
    /// Instrumentation updates to modules that contain valid instrumentation changes.
    /// Locations that were requested to be instrumented but can't be for any reason are filtered out.
    /// 
    /// <see cref="ModuleUpdates.Status"/> is 
    /// - <see cref="ModuleUpdateStatus.Blocked"/> is there are any source changes in the current solution,
    /// - <see cref="ModuleUpdateStatus.Ready"/> if there are any instrumentation deltas to be applied,
    /// - <see cref="ModuleUpdateStatus.None"/> if no locations for which instrumentation was requested can't be instrumented.
    /// </summary>
    public required ModuleUpdates ModuleUpdates { get; init; }

    /// <summary>
    /// Baselines of updated projects used for the next generation.
    /// </summary>
    public required ImmutableArray<ProjectBaseline> ProjectBaselines { get; init; }

    /// <summary>
    /// Diagnostics.
    /// </summary>
    public required ImmutableArray<ProjectDiagnostics> Diagnostics { get; init; }

    /// <summary>
    /// Instrumentation resuts, including diagnostics and status of each requested instrumentation location.
    /// Not <see langword="null"/> unless <see cref="ModuleUpdates.Status"/> is <see cref="ModuleUpdateStatus.None"/>.
    /// </summary>
    public ManagedHotReloadInstrumentationResults? Results { get; init; }

    internal void Log(TraceLog log, UpdateId updateId)
    {
        log.Write("Instrumentation update {0}.{1} status={2} results={3}",
            updateId.SessionId.Ordinal,
            updateId.Ordinal,
            ModuleUpdates.Status,
            Results is null ? "none" : "available");

        foreach (var moduleUpdate in ModuleUpdates.Updates)
        {
            log.Write("Module update: capabilities=[{0}], types=[{1}], methods=[{2}]",
                moduleUpdate.RequiredCapabilities,
                moduleUpdate.UpdatedTypes,
                moduleUpdate.UpdatedMethods);
        }

        Results?.Log(log);
    }
}
