// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Composition;

namespace Microsoft.CodeAnalysis.Options
{
    internal interface IGlobalOptionProvider : IOptionProvider
    {
    }

    [MetadataAttribute]
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class ExportGlobalOptionProviderAttribute : ExportAttribute
    {
        /// <summary>
        /// Optional source language for language specific option providers.  See <see cref="LanguageNames"/>.
        /// This will be empty string for language agnostic option providers.
        /// </summary>
        public string Language { get; }

        /// <summary>
        /// Constructor for language agnostic option providers.
        /// Use <see cref="ExportGlobalOptionProviderAttribute(string)"/> overload for language specific option providers.
        /// </summary>
        public ExportGlobalOptionProviderAttribute()
            : base(typeof(IGlobalOptionProvider))
        {
            Language = string.Empty;
        }

        /// <summary>
        /// Constructor for language specific option providers.
        /// Use <see cref="ExportGlobalOptionProviderAttribute()"/> overload for language agnostic option providers.
        /// </summary>
        public ExportGlobalOptionProviderAttribute(string language)
            : base(typeof(IGlobalOptionProvider))
        {
            Language = language ?? throw new ArgumentNullException(nameof(language));
        }
    }
}
