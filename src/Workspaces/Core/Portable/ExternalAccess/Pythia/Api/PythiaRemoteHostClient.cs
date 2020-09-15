// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Remote;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.ExternalAccess.Pythia.Api
{
    internal sealed class PythiaRemoteHostClient
    {
        private readonly RemoteHostClient _client;

        internal PythiaRemoteHostClient(RemoteHostClient client)
        {
            _client = client;
        }

        public static async Task<PythiaRemoteHostClient?> CreateAsync(Workspace workspace, CancellationToken cancellationToken = default)
        {
            var client = await RemoteHostClient.TryGetClientAsync(workspace.Services, cancellationToken).ConfigureAwait(false);
            return client == null ? null : new PythiaRemoteHostClient(client);
        }

        [Obsolete]
        public static Task<Optional<T>> TryRunRemoteAsync<T>(Workspace workspace, string serviceName, string targetName, Solution? solution, IReadOnlyList<object?> arguments, CancellationToken cancellationToken)
        {
            Contract.ThrowIfFalse(serviceName == "pythia");
            return TryRunRemoteAsync<T>(workspace, targetName, solution, arguments, cancellationToken);
        }

        [Obsolete]
        public static async Task<Optional<T>> TryRunRemoteAsync<T>(Workspace workspace, string targetName, Solution? solution, IReadOnlyList<object?> arguments, CancellationToken cancellationToken)
        {
            var client = await RemoteHostClient.TryGetClientAsync(workspace, cancellationToken).ConfigureAwait(false);
            if (client == null)
            {
                return default;
            }

            return await client.RunRemoteAsync<T>(WellKnownServiceHubService.IntelliCode, targetName, solution, arguments, callbackTarget: null, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<bool> TryInvokeAsync<TService>(
            Solution solution,
            Func<TService, object, CancellationToken, ValueTask> invocation,
            object? callbackTarget,
            CancellationToken cancellationToken)
            where TService : class
            => _client.TryInvokeAsync(solution, invocation, callbackTarget, cancellationToken);

        public ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(
            Solution solution,
            Func<TService, object, CancellationToken, ValueTask<TResult>> invocation,
            object? callbackTarget,
            CancellationToken cancellationToken)
            where TService : class
            => _client.TryInvokeAsync(solution, invocation, callbackTarget, cancellationToken);

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
            => _client.TryInvokeAsync(solution, invocation, reader, callbackTarget, cancellationToken);
    }
}
