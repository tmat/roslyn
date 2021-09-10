// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options.Providers;
using Microsoft.CodeAnalysis.Remote;
using Microsoft.CodeAnalysis.Shared.Collections;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Options
{
    [Export(typeof(SolutionOptionsRegistry)), Shared]
    internal sealed class SolutionOptionsRegistry
    {
        private static readonly ImmutableDictionary<string, (IOption? option, IEditorConfigStorageLocation2? storageLocation)> s_emptyEditorConfigKeysToOptions
            = ImmutableDictionary.Create<string, (IOption? option, IEditorConfigStorageLocation2? storageLocation)>(AnalyzerConfigOptions.KeyComparer);

#pragma warning disable IDE0044 // Add readonly modifier - https://github.com/dotnet/roslyn/issues/46785
        private ImmutableDictionary<string, (IOption? option, IEditorConfigStorageLocation2? storageLocation)> _neutralEditorConfigKeysToOptions = s_emptyEditorConfigKeysToOptions;
        private ImmutableDictionary<string, (IOption? option, IEditorConfigStorageLocation2? storageLocation)> _csharpEditorConfigKeysToOptions = s_emptyEditorConfigKeysToOptions;
        private ImmutableDictionary<string, (IOption? option, IEditorConfigStorageLocation2? storageLocation)> _visualBasicEditorConfigKeysToOptions = s_emptyEditorConfigKeysToOptions;
#pragma warning restore IDE0044 // Add readonly modifier

        private static ImmutableDictionary<string, Lazy<ImmutableHashSet<IOption>>> CreateLazySerializableOptionsByLanguage(IEnumerable<Lazy<IOptionProvider, LanguageMetadata>> optionProviders)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, Lazy<ImmutableHashSet<IOption>>>();

            foreach (var (language, lazyProvidersAndMetadata) in optionProviders.ToPerLanguageMap())
            {
                builder.Add(language, new Lazy<ImmutableHashSet<IOption>>(() => ComputeSerializableOptionsFromProviders(lazyProvidersAndMetadata)));
            }

            return builder.ToImmutable();

            // Local functions
            static ImmutableHashSet<IOption> ComputeSerializableOptionsFromProviders(ImmutableArray<Lazy<IOptionProvider, LanguageMetadata>> lazyProvidersAndMetadata)
            {
                var builder = ImmutableHashSet.CreateBuilder<IOption>();

                foreach (var lazyProviderAndMetadata in lazyProvidersAndMetadata)
                {
                    var provider = lazyProviderAndMetadata.Value;
                    if (!IsSolutionOptionProvider(provider))
                    {
                        continue;
                    }

                    builder.AddRange(provider.Options);
                }

                return builder.ToImmutable();
            }
        }

        // We only consider the options defined in the the DefaultAssemblies (Workspaces and Features) as serializable.
        // This is due to the fact that other layers above are VS specific and do not execute in OOP.
        internal static bool IsSolutionOptionProvider(IOptionProvider provider)
            => MefHostServices.IsDefaultAssembly(provider.GetType().Assembly);
    }
}
