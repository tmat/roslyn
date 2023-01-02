// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma warning disable RS0030 // Do not used banned APIs: Option<T>

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Options
{
    /// <inheritdoc cref="Option2{T}"/>
    public class Option<T> : IPublicOption, IEquatable<IOption?>
    {
        private readonly OptionDefinition _optionDefinition;
        public string Feature { get; }
        public string Name { get; }
        public ImmutableArray<OptionStorageLocation> StorageLocations { get; }

        /// <summary>
        /// Set once when the corresponding internal option is created.
        /// </summary>
        internal IOption2? InternalOption { get; private set; }

        [Obsolete("Use a constructor that specifies an explicit default value.")]
        public Option(string feature, string name)
            : this(feature, name, default!)
        {
            // This constructor forwards to the next one; it exists to maintain source-level compatibility with older callers.
        }

        public Option(string feature, string name, T defaultValue)
            : this(feature ?? throw new ArgumentNullException(nameof(feature)),
                   OptionGroup.Default,
                   name ?? throw new ArgumentNullException(nameof(name)),
                   defaultValue,
                   storageLocations: ImmutableArray<OptionStorageLocation>.Empty,
                   isEditorConfigOption: false)
        {
        }

        public Option(string feature, string name, T defaultValue, params OptionStorageLocation[] storageLocations)
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

        private Option(string feature, OptionGroup group, string name, T defaultValue, ImmutableArray<OptionStorageLocation> storageLocations, bool isEditorConfigOption)
            : this(new OptionDefinition(group, feature + "_" + name, defaultValue, typeof(T), isEditorConfigOption), feature, name, storageLocations)
        {
        }

        internal Option(OptionDefinition optionDefinition, string feature, string name, ImmutableArray<OptionStorageLocation> storageLocations)
        {
            Feature = feature;
            Name = name;
            _optionDefinition = optionDefinition;
            StorageLocations = storageLocations;
        }

        internal void InitializeInternalOption(IOption2 option)
        {
            Contract.ThrowIfFalse(InternalOption is null);
            Contract.ThrowIfTrue(option.IsPerLanguage);

            InternalOption = option;
        }

        internal OptionGroup Group => _optionDefinition.Group;

        public T DefaultValue => (T)_optionDefinition.DefaultValue!;

        public Type Type => _optionDefinition.Type;

        IOption2? IPublicOption.InternalOption => InternalOption;

        object? IOption.DefaultValue => this.DefaultValue;

        bool IOption.IsPerLanguage => false;

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

        public static implicit operator OptionKey(Option<T> option)
            => new(option);
    }
}
