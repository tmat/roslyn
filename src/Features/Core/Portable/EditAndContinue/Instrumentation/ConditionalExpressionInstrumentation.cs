// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Runtime.Serialization;
using Microsoft.CodeAnalysis.EditAndContinue.Contracts;

namespace Microsoft.CodeAnalysis.EditAndContinue;

[DataContract]
internal sealed class ManagedHotReloadConditionalExpressionInstrumentation : ManagedHotReloadInstrumentation
{
    // List of expressions to inject.
    [DataMember]
    public required ImmutableArray<ConditionalExpressionSource> Expressions { get; init; }
}

[DataContract]
internal sealed class ManagedHotReloadConditionalExpressionInstrumentationResults : ManagedHotReloadInstrumentationResults
{
    internal override void Log(TraceLog log)
    {
    }
}
