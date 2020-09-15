// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Remote;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.ExternalAccess.UnitTesting.Api
{
    internal readonly struct UnitTestingRemoteHostClientWrapper
    {
        internal UnitTestingRemoteHostClientWrapper(RemoteHostClient underlyingObject)
            => UnderlyingObject = underlyingObject;

        internal RemoteHostClient UnderlyingObject { get; }

        [Obsolete]
        public bool IsDefault => UnderlyingObject == null;

        public static async Task<UnitTestingRemoteHostClientWrapper?> TryGetClientAsync(HostWorkspaceServices services, CancellationToken cancellationToken = default)
        {
            var client = await RemoteHostClient.TryGetClientAsync(services, cancellationToken).ConfigureAwait(false);
            if (client is null)
                return null;

            return new UnitTestingRemoteHostClientWrapper(client);
        }

        public ValueTask<bool> TryInvokeAsync<TService>(
            Solution solution,
            Func<TService, object, CancellationToken, ValueTask> invocation,
            object? callbackTarget,
            CancellationToken cancellationToken)
            where TService : class
            => UnderlyingObject.TryInvokeAsync(solution, invocation, callbackTarget, cancellationToken);

        public ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(
            Solution solution,
            Func<TService, object, CancellationToken, ValueTask<TResult>> invocation,
            object? callbackTarget,
            CancellationToken cancellationToken)
            where TService : class
            => UnderlyingObject.TryInvokeAsync(solution, invocation, callbackTarget, cancellationToken);

        /// <summary>
        /// Invokes a remote API that streams results back to the caller.
        /// </summary>
        public ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(
            Solution solution,
            Func<TService, object, Stream, CancellationToken, ValueTask> invocation,
            Func<Stream, CancellationToken, ValueTask<TResult>> reader,
            object? callbackTarget,
            CancellationToken cancellationToken)
            where TService : class
            => UnderlyingObject.TryInvokeAsync(solution, invocation, reader, callbackTarget, cancellationToken);

        public async ValueTask<UnitTestingRemoteServiceConnectionWrapper<TService>> CreateConnectionAsync<TService>(object? callbackTarget, CancellationToken cancellationToken)
            where TService : class
            => new UnitTestingRemoteServiceConnectionWrapper<TService>(await UnderlyingObject.CreateConnectionAsync<TService>(callbackTarget, cancellationToken).ConfigureAwait(false));

        [Obsolete]
        public async Task<bool> TryRunRemoteAsync(UnitTestingServiceHubService service, string targetName, Solution? solution, IReadOnlyList<object?> arguments, object? callbackTarget, CancellationToken cancellationToken)
        {
            Contract.ThrowIfTrue(IsDefault);
            await UnderlyingObject.RunRemoteAsync((WellKnownServiceHubService)service, targetName, solution, arguments, callbackTarget, cancellationToken).ConfigureAwait(false);
            return true;
        }

        [Obsolete]
        public async Task<Optional<T>> TryRunRemoteAsync<T>(UnitTestingServiceHubService service, string targetName, Solution? solution, IReadOnlyList<object?> arguments, object? callbackTarget, CancellationToken cancellationToken)
        {
            Contract.ThrowIfTrue(IsDefault);
            return await UnderlyingObject.RunRemoteAsync<T>((WellKnownServiceHubService)service, targetName, solution, arguments, callbackTarget, cancellationToken).ConfigureAwait(false);
        }

        [Obsolete]
        public async Task<UnitTestingRemoteServiceConnectionWrapper> CreateConnectionAsync(UnitTestingServiceHubService service, object? callbackTarget, CancellationToken cancellationToken)
        {
            Contract.ThrowIfTrue(IsDefault);
            return new UnitTestingRemoteServiceConnectionWrapper(await UnderlyingObject.CreateConnectionAsync((WellKnownServiceHubService)service, callbackTarget, cancellationToken).ConfigureAwait(false));
        }

        [Obsolete]
        public event EventHandler<bool> StatusChanged
        {
            add
            {
                Contract.ThrowIfTrue(IsDefault);
                UnderlyingObject.StatusChanged += value;
            }

            remove
            {
                Contract.ThrowIfTrue(IsDefault);
                UnderlyingObject.StatusChanged -= value;
            }
        }
    }
}
