// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.Copilot;

internal static class CopilotOptions
{
    // Default values must be in sync with values in ConversationsOptions in Copilot repo.
    public static readonly Option2<bool> EnableCodeAnalysis = new("copilot_enable_code_analysis", defaultValue: false);
    public static readonly Option2<bool> EnableRefineQuickActionSuggestion = new("copilot_enable_code_analysis", defaultValue: false);
    public static readonly Option2<bool> EnableOnTheFlyDocs = new("copilot_enable_on_the_fly_docs", defaultValue: true);
    public static readonly Option2<bool> EnableDocCommentGeneration = new("copilot_enable_doc_comment_generation", defaultValue: true);
}
