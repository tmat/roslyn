// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.Editor.CSharp.SplitStringLiteral
{
    [ExportGlobalOptionProvider(LanguageNames.CSharp), Shared]
    internal sealed class SplitStringLiteralOptions : IGlobalOptionProvider
    {
        public static PerLanguageOption2<bool> Enabled =
            new(nameof(SplitStringLiteralOptions), nameof(Enabled), defaultValue: true,
                storageLocation: new RoamingProfileStorageLocation("TextEditor.%LANGUAGE%.Specific.SplitStringLiterals"));

        public ImmutableArray<IOption> Options { get; } = ImmutableArray.Create<IOption>(
            Enabled);

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public SplitStringLiteralOptions()
        {
        }
    }
}
