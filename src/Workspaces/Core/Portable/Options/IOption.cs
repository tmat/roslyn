// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeStyle;

namespace Microsoft.CodeAnalysis.Options
{
    public interface IOption
    {
        string Feature { get; }
        string Name { get; }
        Type Type { get; }
        object? DefaultValue { get; }
        bool IsPerLanguage { get; }

        ImmutableArray<OptionStorageLocation> StorageLocations { get; }
    }

    internal static partial class Extensions
    {
        // The following are used only to implement equality/ToString of public Option<T> and PerLanguageOption<T> options.
        // Public options can be instantiated with non-unique config name and thus we need to include default value in the equality
        // to avoid collisions among them.

        public static string PublicOptionDefinitionToString(this IOption option)
            => $"{option.Feature} - {option.Name}";

        public static bool PublicOptionDefinitionEquals(this IOption self, IOption other)
        {
            var equals = self.Name == other.Name && self.Feature == other.Feature;

            // DefaultValue and Type can differ between different but equivalent implementations of "ICodeStyleOption".
            // So, we skip these fields for equality checks of code style options.
            if (equals && self.DefaultValue is not ICodeStyleOption)
            {
                equals = Equals(self.DefaultValue, other.DefaultValue) && self.Type == other.Type;
            }

            return equals;
        }
    }
}
