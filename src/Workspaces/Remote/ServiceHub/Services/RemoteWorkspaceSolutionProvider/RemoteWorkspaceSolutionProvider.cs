// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceHub.Framework;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Remote
{
    /// <summary>
    /// This service is only available in Roslyn ServiceHub process.
    /// </summary>
    internal sealed class RemoteWorkspaceSolutionProvider : BrokeredServiceBase, IRemoteWorkspaceSolutionProvider
    {
        internal sealed class Factory : FactoryBase<IRemoteWorkspaceSolutionProvider>
        {
            protected override IRemoteWorkspaceSolutionProvider CreateService(in ServiceConstructionArguments arguments)
                => new RemoteWorkspaceSolutionProvider(arguments);
        }

        public const string ServiceName = ServiceDescriptors.ServiceNamePrefix + nameof(RemoteWorkspaceSolutionProvider);
        public static readonly ServiceDescriptor Descriptor = ServiceDescriptor.CreateInProcServiceDescriptor(ServiceName);

        public RemoteWorkspaceSolutionProvider(in ServiceConstructionArguments arguments)
            : base(arguments)
        {
        }

        public new async ValueTask<Solution> GetSolutionAsync(PinnedSolutionInfo solutionInfo, CancellationToken cancellationToken)
            => await base.GetSolutionAsync(solutionInfo, cancellationToken).ConfigureAwait(false);

        public static async ValueTask<Solution> GetSolutionAsync(ServiceBrokerClient serviceBroker, object solutionInfo, CancellationToken cancellationToken)
        {
            Contract.ThrowIfFalse(solutionInfo is PinnedSolutionInfo);

            Solution solution;
            using var rental = await serviceBroker.GetProxyAsync<IRemoteWorkspaceSolutionProvider>(Descriptor, cancellationToken).ConfigureAwait(false);
            Contract.ThrowIfNull(rental.Proxy);
            solution = await rental.Proxy.GetSolutionAsync((PinnedSolutionInfo)solutionInfo, cancellationToken).ConfigureAwait(false);

            return solution;
        }
    }
}
