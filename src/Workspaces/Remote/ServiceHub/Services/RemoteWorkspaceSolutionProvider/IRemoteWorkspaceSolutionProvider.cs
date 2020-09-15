// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis.Remote
{
    /// <summary>
    /// This service is only available in ServiceHub process.
    /// </summary>
    internal interface IRemoteWorkspaceSolutionProvider
    {
        /// <summary>
        /// Returns solution snapshot that corresponds to <see cref="PinnedSolutionInfo"/> sent from in-proc workspace.
        /// Note that since the service only available in the ServiceHub process the result is not serialized.
        /// </summary>
        ValueTask<Solution> GetSolutionAsync(PinnedSolutionInfo solutionInfo, CancellationToken cancellationToken);
    }
}
