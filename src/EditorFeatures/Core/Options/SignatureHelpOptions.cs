// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.Editor.Options
{
    [ExportGlobalOptionProvider, Shared]
    internal sealed class SignatureHelpOptions : IGlobalOptionProvider
    {
        public static readonly PerLanguageOption2<bool> ShowSignatureHelp = new(nameof(SignatureHelpOptions), nameof(ShowSignatureHelp), defaultValue: true);

        public ImmutableArray<IOption> Options { get; } = ImmutableArray.Create<IOption>(
            ShowSignatureHelp);

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public SignatureHelpOptions()
        {
        }
    }
}
