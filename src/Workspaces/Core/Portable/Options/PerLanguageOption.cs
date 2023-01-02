// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma warning disable RS0030 // Do not used banned APIs: PerLanguageOption<T>

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Options
{
    /// <inheritdoc cref="PerLanguageOption2{T}"/>
    public class PerLanguageOption<T> : IPublicOption, IEquatable<IOption?>
    {
        private readonly OptionDefinition _optionDefinition;

        public string Feature { get; }
        public string Name { get; }

        public ImmutableArray<OptionStorageLocation> StorageLocations { get; }

        /// <summary>
        /// Set once when the corresponding internal option is created.
        /// </summary>
        internal IOption2? InternalOption { get; private set; }

        public PerLanguageOption(string feature, string name, T defaultValue)
            : this(feature ?? throw new ArgumentNullException(nameof(feature)),
                   OptionGroup.Default,
                   name ?? throw new ArgumentNullException(nameof(name)),
                   defaultValue,
                   storageLocations: ImmutableArray<OptionStorageLocation>.Empty,
                   isEditorConfigOption: false)
        {
        }

        public PerLanguageOption(string feature, string name, T defaultValue, params OptionStorageLocation[] storageLocations)
            : this(feature ?? throw new ArgumentNullException(nameof(feature)),
                   OptionGroup.Default,
                   name ?? throw new ArgumentNullException(nameof(name)),
                   defaultValue,
                   PublicContract.RequireNonNullItems(storageLocations, nameof(storageLocations)).ToImmutableArray(),
                   isEditorConfigOption: false)
        {
            // should not be used internally to create options
            Debug.Assert(storageLocations.All(l => l is not IEditorConfigStorageLocation));
        }

        private PerLanguageOption(string feature, OptionGroup group, string name, T defaultValue, ImmutableArray<OptionStorageLocation> storageLocations, bool isEditorConfigOption)
            : this(new OptionDefinition(group, feature + "_" + name, defaultValue, typeof(T), isEditorConfigOption), feature, name, storageLocations)
        {
        }

        internal PerLanguageOption(OptionDefinition optionDefinition, string feature, string name, ImmutableArray<OptionStorageLocation> storageLocations)
        {
            Feature = feature;
            Name = name;
            _optionDefinition = optionDefinition;
            StorageLocations = storageLocations;
        }

        internal void InitializeInternalOption(IOption2 option)
        {
            Contract.ThrowIfFalse(InternalOption is null);
            Contract.ThrowIfFalse(option.IsPerLanguage);

            InternalOption = option;
        }

        public Type Type => _optionDefinition.Type;

        public T DefaultValue => (T)_optionDefinition.DefaultValue!;

        IOption2? IPublicOption.InternalOption => InternalOption;

        object? IOption.DefaultValue => this.DefaultValue;

        bool IOption.IsPerLanguage => true;

        bool IEquatable<IOption?>.Equals(IOption? other) => Equals(other);

        public override string ToString() => this.PublicOptionDefinitionToString();

        public override int GetHashCode() => _optionDefinition.GetHashCode();

        public override bool Equals(object? obj) => Equals(obj as IOption);

        private bool Equals(IOption? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other is not null && this.PublicOptionDefinitionEquals(other);
        }
    }
}
