// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Composition;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;

namespace Microsoft.CodeAnalysis.CSharp;

internal partial class CSharpSyntaxTreeFactory
{
    [ExportLanguageServiceFactory(typeof(ISyntaxTreeFactory), LanguageNames.CSharp), Shared]
    internal partial class Factory : ILanguageServiceFactory
    {
        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public Factory()
        {
        }

        public ILanguageService CreateLanguageService(HostLanguageServices provider)
            => new CSharpSyntaxTreeFactory(provider.LanguageServices.SolutionServices);
    }
}
