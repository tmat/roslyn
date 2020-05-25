// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.ErrorReporting;
using Nerdbank.Streams;
using Newtonsoft.Json;
using Roslyn.Utilities;
using StreamJsonRpc;

namespace Microsoft.CodeAnalysis.Remote
{
    /// <summary>
    /// Helper type that abstract out JsonRpc communication with extra capability of
    /// using raw stream to move over big chunk of data
    /// </summary>
    internal sealed class RemoteEndPoint : IDisposable
    {
        private const string UnexpectedExceptionLogMessage = "Unexpected exception from JSON-RPC";

        private static readonly JsonRpcTargetOptions s_jsonRpcTargetOptions = new JsonRpcTargetOptions()
        {
            // Do not allow JSON-RPC to automatically subscribe to events and remote their calls.
            NotifyClientOfEvents = false,

            // Only allow public methods (may be on internal types) to be invoked remotely.
            AllowNonPublicInvocation = false
        };

        private static int s_id;

        private readonly int _id;
        private readonly TraceSource _logger;
        private readonly JsonRpc _rpc;

        private bool _startedListening;
        private JsonRpcDisconnectedEventArgs? _disconnectedReason;

        public event Action<JsonRpcDisconnectedEventArgs>? Disconnected;
        public event Action<Exception>? UnexpectedExceptionThrown;

        public RemoteEndPoint(Stream stream, TraceSource logger, object? incomingCallTarget, IEnumerable<JsonConverter>? jsonConverters = null)
        {
            RoslynDebug.Assert(stream != null);
            RoslynDebug.Assert(logger != null);

            _id = Interlocked.Increment(ref s_id);
            _logger = logger;

            var jsonFormatter = new JsonMessageFormatter();

            if (jsonConverters != null)
            {
                jsonFormatter.JsonSerializer.Converters.AddRange(jsonConverters);
            }

            jsonFormatter.JsonSerializer.Converters.Add(AggregateJsonConverter.Instance);

            _rpc = new JsonRpc(new HeaderDelimitedMessageHandler(stream, jsonFormatter))
            {
                CancelLocallyInvokedMethodsWhenConnectionIsClosed = true,
                TraceSource = logger
            };

            if (incomingCallTarget != null)
            {
                _rpc.AddLocalRpcTarget(incomingCallTarget, s_jsonRpcTargetOptions);
            }

            _rpc.Disconnected += OnDisconnected;
        }

        /// <summary>
        /// Must be called before any communication commences.
        /// See https://github.com/dotnet/roslyn/issues/16900#issuecomment-277378950.
        /// </summary>
        public void StartListening()
        {
            _rpc.StartListening();
            _startedListening = true;
        }

        public bool IsDisposed
            => _rpc.IsDisposed;

        public void Dispose()
        {
            _rpc.Disconnected -= OnDisconnected;
            _rpc.Dispose();
        }

        public async Task InvokeAsync(string targetName, IReadOnlyList<object?> arguments, CancellationToken cancellationToken)
        {
            Contract.ThrowIfFalse(_startedListening);

            // if this end-point is already disconnected do not log more errors:
            var logError = _disconnectedReason == null;

            try
            {
                await _rpc.InvokeWithCancellationAsync(targetName, arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!logError || ReportUnlessCanceled(ex, cancellationToken))
            {
                // Remote call may fail with different exception even when our cancellation token is signaled
                // (e.g. on shutdown if the connection is dropped):
                cancellationToken.ThrowIfCancellationRequested();

                throw CreateSoftCrashException(ex, cancellationToken);
            }
        }

        public async Task<T> InvokeAsync<T>(string targetName, IReadOnlyList<object?> arguments, CancellationToken cancellationToken)
        {
            Contract.ThrowIfFalse(_startedListening);

            // if this end-point is already disconnected do not log more errors:
            var logError = _disconnectedReason == null;

            try
            {
                return await _rpc.InvokeWithCancellationAsync<T>(targetName, arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!logError || ReportUnlessCanceled(ex, cancellationToken))
            {
                // Remote call may fail with different exception even when our cancellation token is signaled
                // (e.g. on shutdown if the connection is dropped):
                cancellationToken.ThrowIfCancellationRequested();

                throw CreateSoftCrashException(ex, cancellationToken);
            }
        }

        /// <summary>
        /// Invokes a remote method <paramref name="targetName"/> with specified <paramref name="arguments"/> and
        /// establishes a stream through which the target method may transfer large binary data back to the caller. 
        /// The stream is passed to the target method as an additional argument following the specified <paramref name="arguments"/>.
        /// The target method is expected to use
        /// <see cref="WriteDataToNamedPipeAsync{TData}(Stream, TData, Func{Stream, TData, CancellationToken, Task}, CancellationToken)"/>
        /// to write the data to the pipe stream.
        /// </summary>
        public async Task<T> InvokeAsync<T>(string targetName, IReadOnlyList<object?> arguments, Func<Stream, CancellationToken, Task<T>> dataReader, CancellationToken cancellationToken)
        {
            Contract.ThrowIfFalse(_startedListening);

            // if this end-point is already disconnected do not log more errors:
            var logError = _disconnectedReason == null;

            try
            {
                var (clientStream, serverStream) = FullDuplexStream.CreatePair();
                await _rpc.InvokeWithCancellationAsync(targetName, arguments.Concat(serverStream).ToArray(), cancellationToken).ConfigureAwait(false);
                var result = await dataReader(clientStream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!logError || ReportUnlessCanceled(ex, cancellationToken))
            {
                // Remote call may fail with different exception even when our cancellation token is signaled
                // (e.g. on shutdown if the connection is dropped):
                cancellationToken.ThrowIfCancellationRequested();

                throw CreateSoftCrashException(ex, cancellationToken);
            }
        }

        public static Task WriteDataToNamedPipeAsync<TData>(Stream outputStream, TData data, Func<ObjectWriter, TData, CancellationToken, Task> dataWriter, CancellationToken cancellationToken)
            => WriteDataToNamedPipeAsync(outputStream, data,
                async (stream, data, cancellationToken) =>
                {
                    using var objectWriter = new ObjectWriter(stream, leaveOpen: true, cancellationToken);
                    await dataWriter(objectWriter, data, cancellationToken).ConfigureAwait(false);
                }, cancellationToken);

        public static async Task WriteDataToNamedPipeAsync<TData>(Stream outputStream, TData data, Func<Stream, TData, CancellationToken, Task> dataWriter, CancellationToken cancellationToken)
        {
            try
            {
                await dataWriter(outputStream, data, cancellationToken).ConfigureAwait(false);

                // stream must be disposed once all data have been written:
                outputStream.Dispose();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // The stream has closed before we had chance to check cancellation.
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private static bool ReportUnlessCanceled(Exception ex, CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ReportNonFatalWatson(ex);
            }

            return true;
        }

        private static void ReportNonFatalWatson(Exception exception)
        {
            FatalError.ReportWithoutCrash(exception);
        }

        private SoftCrashException CreateSoftCrashException(Exception ex, CancellationToken cancellationToken)
        {
            // TODO: revisit https://github.com/dotnet/roslyn/issues/40476
            // We are getting unexpected exception from service hub. Rather than doing hard crash on unexpected exception,
            // we decided to do soft crash where we show info bar to users saying "VS got corrupted and users should save
            // their works and close VS"

            UnexpectedExceptionThrown?.Invoke(ex);

            // throw soft crash exception
            return new SoftCrashException(UnexpectedExceptionLogMessage, ex, cancellationToken);
        }

        private void LogError(string message)
        {
            var currentProcess = Process.GetCurrentProcess();
            _logger.TraceEvent(TraceEventType.Error, _id, $" [{currentProcess.ProcessName}:{currentProcess.Id}] {message}");
        }

        private void LogDisconnectInfo(JsonRpcDisconnectedEventArgs? e)
        {
            if (e != null)
            {
                LogError($@"Stream disconnected unexpectedly:  {e.Reason}, '{e.Description}', LastMessage: {e.LastMessage}, Exception: {e.Exception?.Message}");
            }
        }

        /// <summary>
        /// Handle disconnection event, so that we detect disconnection as soon as it happens
        /// without waiting for the next failing remote call. The remote call may not happen 
        /// if there is an issue with the connection. E.g. the client end point might not receive
        /// a callback from server, or the server end point might not receive a call from client.
        /// </summary>
        private void OnDisconnected(object sender, JsonRpcDisconnectedEventArgs e)
        {
            _disconnectedReason = e;

            // Don't log info in cases that are common - such as if we dispose the connection or the remote host process shuts down.
            if (e.Reason != DisconnectedReason.LocallyDisposed &&
                e.Reason != DisconnectedReason.RemotePartyTerminated)
            {
                LogDisconnectInfo(e);
            }

            Disconnected?.Invoke(e);
        }
    }
}
