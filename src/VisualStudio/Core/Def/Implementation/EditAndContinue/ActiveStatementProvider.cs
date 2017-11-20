// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Debugging;
using Microsoft.CodeAnalysis.EditAndContinue;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Clr;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Roslyn.Utilities;
using System.Linq;
using Microsoft.VisualStudio.Debugger.Symbols;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.VisualStudio.LanguageServices.EditAndContinue
{
    internal sealed class ActiveStatementProvider : IActiveStatementProvider
    {
        public Task<ImmutableArray<ActiveStatement>> GetActiveStatementsAsync(CancellationToken cancellationToken)
        {
            var workList = DkmWorkList.Create(CompletionRoutine: null);
            var completion = new TaskCompletionSource<ImmutableArray<ActiveStatement>>();
            var results = ArrayBuilder<DkmGetActiveStatementsAsyncResult>.GetInstance();
            int pendingWorkCount = 0;

            try
            {
                foreach (var process in DkmProcess.GetProcesses())
                {
                    foreach (var runtimeInstance in process.GetRuntimeInstances())
                    {
                        if (runtimeInstance.TagValue == DkmRuntimeInstance.Tag.ClrRuntimeInstance)
                        {
                            int resultIndex = results.Count;
                            results.Add(default);

                            var clrRuntimeInstance = (DkmClrRuntimeInstance)runtimeInstance;
                            clrRuntimeInstance.GetActiveStatements(workList, result =>
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    workList.Cancel();
                                    completion.SetCanceled();
                                }

                                results[resultIndex] = result;

                                // signal completion:
                                if (Interlocked.Decrement(ref pendingWorkCount) == 0)
                                {
                                    if (TryGetActiveStatementsFromResults(results, out var activeStatements, out var exception))
                                    {
                                        completion.SetResult(activeStatements);
                                    }
                                    else
                                    {
                                        completion.SetException(exception);
                                    }
                                }
                            });
                        }
                    }
                }

                pendingWorkCount = results.Count;

                // Start execution of the Concord work items.
                workList.BeginExecution();

                return completion.Task;
            }
            finally
            {
                results.Free();
            }
        }

        private bool TryGetActiveStatementsFromResults(ArrayBuilder<DkmGetActiveStatementsAsyncResult> results, out ImmutableArray<ActiveStatement> activeStatements, out Exception exception)
        {
            activeStatements = default;
            exception = null;

            int count = 0;
            foreach (var result in results)
            {
                if ((exception = Marshal.GetExceptionForHR(result.ErrorCode)) != null)
                {
                    return false;
                }

                count += result.ActiveStatements.Length;
            }

            var builder = ArrayBuilder<ActiveStatement>.GetInstance(count);

            foreach (var result in results)
            {
                foreach (var dkmStatement in result.ActiveStatements)
                {
                    // TODO: call this async:
                    var p = dkmStatement.InstructionSymbol.GetSourcePosition(
                        DkmSourcePositionFlags.None,
                        InspectionSession: null, 
                        StartOfLine: out _);

                    builder.Add(new ActiveStatement(
                        dkmStatement.Id,
                        (ActiveStatementFlags)dkmStatement.Flags),
                        p.DocumentName,
                        ToLinePositionSpan(p.TextSpan));
                }
            }

            activeStatements = builder.ToImmutableAndFree();
            return true;
        }

        private static LinePositionSpan ToLinePositionSpan(DkmTextSpan span)
            => new LinePositionSpan(new LinePosition(span.StartLine, span.StartColumn), new LinePosition(span.EndLine, span.EndColumn));
    }
}
