// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.Serialization;
using Microsoft.CodeAnalysis.EditAndContinue.Contracts;

namespace Microsoft.CodeAnalysis.EditAndContinue;

[DataContract]
internal readonly struct ConditionalExpressionSource
{
    /// <summary>
    /// Name of the containing module (w/o file extension).
    /// </summary>
    [DataMember]
    public required string ModuleName { get; init; }

    /// <summary>
    /// Method where to inject the expression.
    /// </summary>
    [DataMember]
    public required ManagedMethodId Method { get; init; }

    /// <summary>
    /// Span associated with the sequence point where to inject the expression within the document containing the <see cref="Method"/> body.
    /// </summary>
    [DataMember]
    public required SourceFileSpan Span { get; init; }

    /// <summary>
    /// Source code of the expression.
    /// </summary>
    [DataMember]
    public required string Condition { get; init; }

    /// <summary>
    /// Source code of the action expression to execute when the condition evaluates to true.
    /// E.g. <code>global::System.Diagnostics.Debugger.Break()</code>
    /// </summary>
    [DataMember]
    public required string Action { get; init; }
}
