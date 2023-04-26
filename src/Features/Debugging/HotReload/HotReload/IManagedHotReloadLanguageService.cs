// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis.Debugging.Contracts.HotReload
{
    internal interface IManagedHotReloadLanguageService
    {
        ValueTask StartSessionAsync(CancellationToken cancellationToken);

        ValueTask EnterBreakStateAsync(CancellationToken cancellationToken);

        ValueTask ExitBreakStateAsync(CancellationToken cancellationToken);

        ValueTask EndSessionAsync(CancellationToken cancellationToken);

        ValueTask<ManagedHotReloadUpdates> GetUpdatesAsync(CancellationToken cancellationToken);

        ValueTask CommitUpdatesAsync(CancellationToken cancellationToken);

        ValueTask DiscardUpdatesAsync(CancellationToken cancellationToken);

        ValueTask OnCapabilitiesChangedAsync(CancellationToken cancellationToken);

        ValueTask<bool> HasChangesAsync(string? sourceFilePath, CancellationToken cancellationToken);
    }
}
