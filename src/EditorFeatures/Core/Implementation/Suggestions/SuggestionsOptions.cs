// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.Editor.Implementation.Suggestions
{
    [ExportGlobalOptionProvider, Shared]
    internal sealed class SuggestionsOptions : IGlobalOptionProvider
    {
        private const string FeatureName = "SuggestionsOptions";

        public static readonly Option2<bool?> Asynchronous = new(FeatureName, nameof(Asynchronous), defaultValue: null,
            new RoamingProfileStorageLocation("TextEditor.Specific.Suggestions.Asynchronous2"));

        public static readonly Option2<bool> AsynchronousFeatureFlag = new(FeatureName, nameof(AsynchronousFeatureFlag), defaultValue: false,
            new FeatureFlagStorageLocation("Roslyn.AsynchronousQuickActions"));

        public ImmutableArray<IOption> Options { get; } = ImmutableArray.Create<IOption>(
            Asynchronous,
            AsynchronousFeatureFlag);

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public SuggestionsOptions()
        {
        }
    }
}
