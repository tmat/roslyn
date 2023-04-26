// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.Serialization;
using System.Collections.Immutable;

namespace Microsoft.CodeAnalysis.Debugging.Contracts.HotReload;

[DataContract]
internal readonly struct ManagedHotReloadUpdates
{
    [DataMember(Order = 0)]
    public ImmutableArray<ManagedHotReloadUpdate> Updates { get; }

    [DataMember(Order = 1)]
    public ImmutableArray<ManagedHotReloadDiagnostic> Diagnostics { get; }

    public ManagedHotReloadUpdates(ImmutableArray<ManagedHotReloadUpdate> updates, ImmutableArray<ManagedHotReloadDiagnostic> diagnostics)
    {
        Updates = updates;
        Diagnostics = diagnostics;
    }
}
