// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis.Host;

namespace Microsoft.CodeAnalysis.EditAndContinue;

internal interface IMethodBodyInstrumentationSyntaxTreeFactory : ILanguageService
{
    SyntaxTree GetLocalStoreTrackerSourceTree(CancellationToken cancellationToken);

    /// <summary>
    /// Update compilation options with settings required for compiling the tracker.
    /// </summary>
    CompilationOptions UpdateCompilationOptions(CompilationOptions options);
}
