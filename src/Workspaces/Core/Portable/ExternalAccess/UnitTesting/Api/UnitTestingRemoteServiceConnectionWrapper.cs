// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Remote;

namespace Microsoft.CodeAnalysis.ExternalAccess.UnitTesting.Api
{
    [Obsolete]
    internal readonly struct UnitTestingRemoteServiceConnectionWrapper : IDisposable
    {
        internal RemoteServiceConnection UnderlyingObject { get; }

        internal UnitTestingRemoteServiceConnectionWrapper(RemoteServiceConnection underlyingObject)
            => UnderlyingObject = underlyingObject;

        public bool IsDefault => UnderlyingObject == null;

        public async Task<bool> TryRunRemoteAsync(string targetName, Solution? solution, IReadOnlyList<object?> arguments, CancellationToken cancellationToken)
        {
            await UnderlyingObject.RunRemoteAsync(targetName, solution, arguments, cancellationToken).ConfigureAwait(false);
            return true;
        }

        public async Task<Optional<T>> TryRunRemoteAsync<T>(string targetName, Solution? solution, IReadOnlyList<object?> arguments, CancellationToken cancellationToken)
            => await UnderlyingObject.RunRemoteAsync<T>(targetName, solution, arguments, cancellationToken).ConfigureAwait(false);

        public void Dispose() => UnderlyingObject?.Dispose();
    }

    internal readonly struct UnitTestingRemoteServiceConnectionWrapper<TService> : IDisposable
        where TService : class
    {
        internal RemoteServiceConnection<TService> UnderlyingObject { get; }

        internal UnitTestingRemoteServiceConnectionWrapper(RemoteServiceConnection<TService> underlyingObject)
            => UnderlyingObject = underlyingObject;

        public void Dispose() => UnderlyingObject.Dispose();

        public ValueTask<bool> TryInvokeAsync(
            Func<TService, CancellationToken, ValueTask> invocation,
            CancellationToken cancellationToken)
            => UnderlyingObject.TryInvokeAsync(invocation, cancellationToken);

        public ValueTask<Optional<TResult>> TryInvokeAsync<TResult>(
            Func<TService, CancellationToken, ValueTask<TResult>> invocation,
            CancellationToken cancellationToken)
            => UnderlyingObject.TryInvokeAsync(invocation, cancellationToken);

        public ValueTask<Optional<TResult>> TryInvokeAsync<TResult>(
            Func<TService, Stream, CancellationToken, ValueTask> invocation,
            Func<Stream, CancellationToken, ValueTask<TResult>> reader,
            CancellationToken cancellationToken)
            => UnderlyingObject.TryInvokeAsync(invocation, reader, cancellationToken);

        public ValueTask<bool> TryInvokeAsync(
            Solution solution,
            Func<TService, PinnedSolutionInfo, CancellationToken, ValueTask> invocation,
            CancellationToken cancellationToken)
            => UnderlyingObject.TryInvokeAsync(solution, invocation, cancellationToken);

        public ValueTask<Optional<TResult>> TryInvokeAsync<TResult>(
            Solution solution,
            Func<TService, PinnedSolutionInfo, CancellationToken, ValueTask<TResult>> invocation,
            CancellationToken cancellationToken)
            => UnderlyingObject.TryInvokeAsync(solution, invocation, cancellationToken);

        public ValueTask<Optional<TResult>> TryInvokeAsync<TResult>(
            Solution solution,
            Func<TService, PinnedSolutionInfo, Stream, CancellationToken, ValueTask> invocation,
            Func<Stream, CancellationToken, ValueTask<TResult>> reader,
            CancellationToken cancellationToken)
            => UnderlyingObject.TryInvokeAsync(solution, invocation, reader, cancellationToken);
    }
}
