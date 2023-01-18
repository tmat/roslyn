// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.CodeAnalysis.Options;

/// <summary>
/// Interface implemented by public options (Option and PerLanguageOption)
/// to distinguish them from internal ones (<see cref="Option2{T}"/> and <see cref="PerLanguageOption2{T}"/>).
/// </summary>
internal interface IPublicOption
#if !CODE_STYLE
    : IOption, IEquatable<IOption?>
#endif
{
}

