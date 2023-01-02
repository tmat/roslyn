// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CodeAnalysis.Options;

internal interface IPublicOption
#if !CODE_STYLE
    : IOption
#endif
{
    /// <summary>
    /// Associated internal option, or null if the option is defined externally.
    /// </summary>
    IOption2? InternalOption { get; }
}
