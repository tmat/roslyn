// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using MessagePack.Formatters;
using Microsoft.CodeAnalysis.Remote;
using Microsoft.ServiceHub.Framework;

namespace Microsoft.CodeAnalysis.ExternalAccess.Razor.Api
{
    internal static class RazorRemoteUtilities
    {
        public static ImmutableArray<IMessagePackFormatter> Formatters
            => MessagePackFormatters.Formatters;

        public static ValueTask<Solution> GetSolutionAsync(ServiceBrokerClient serviceBroker, object solutionInfo, CancellationToken cancellationToken)
            => RemoteWorkspaceSolutionProvider.GetSolutionAsync(serviceBroker, solutionInfo, cancellationToken);
    }
}
