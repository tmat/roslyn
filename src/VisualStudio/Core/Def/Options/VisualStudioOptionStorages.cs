// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Options;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.LanguageServices.Implementation.Options;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices;

internal abstract class VisualStudioOptionStorage
{
    public sealed class RoamingProfileStorage : VisualStudioOptionStorage
    {
        public string Key { get; }
        public string? VisualBasicKey { get; }

        public RoamingProfileStorage(string key, string? vbKey = null)
        {
            Key = key;
            VisualBasicKey = vbKey;
        }

        private string GetKey(string? language)
            => (VisualBasicKey != null && language == LanguageNames.VisualBasic) ? VisualBasicKey : SubstituteLanguage(Key, language);

        private static string SubstituteLanguage(string keyName, string? language)
            => keyName.Replace("%LANGUAGE%", language switch
            {
                LanguageNames.CSharp => "CSharp",
                LanguageNames.VisualBasic => "VisualBasic",
                _ => language // handles F#, TypeScript and Xaml
            });

        public bool TryPersist(VisualStudioSettingsOptionPersister persister, OptionKey optionKey, object? value)
            => persister.TryPersist(optionKey, GetKey(optionKey.Language), value);

        public bool TryFetch(VisualStudioSettingsOptionPersister persister, OptionKey optionKey, out object? value)
            => persister.TryFetch(optionKey, GetKey(optionKey.Language), out value);
    }

    public sealed class FeatureFlagStorage : VisualStudioOptionStorage
    {
        public string FlagName { get; }

        public FeatureFlagStorage(string flagName)
        {
            FlagName = flagName;
        }

        public bool TryPersist(FeatureFlagPersister persister, object? value)
            => persister.TryPersist(FlagName, value);

        public bool TryFetch(FeatureFlagPersister persister, OptionKey optionKey, out object? value)
            => persister.TryFetch(optionKey, FlagName, out value);
    }

    public sealed class LocalUserProfileStorage : VisualStudioOptionStorage
    {
        private readonly string _path;
        private readonly string _key;

        public LocalUserProfileStorage(string path, string key)
        {
            _path = path;
            _key = key;
        }

        public bool TryPersist(LocalUserRegistryOptionPersister persister, OptionKey optionKey, object? value)
            => persister.TryPersist(optionKey, _path, _key, value);

        public bool TryFetch(LocalUserRegistryOptionPersister persister, OptionKey optionKey, out object? value)
            => persister.TryFetch(optionKey, _path, _key, out value);
    }

    public sealed class CompositeStorage : VisualStudioOptionStorage
    {
        private readonly ImmutableArray<VisualStudioOptionStorage> _storages;

        public CompositeStorage(params VisualStudioOptionStorage[] storages)
        {
            _storages = storages.ToImmutableArray();
        }

        public bool TryPersist(VisualStudioOptionPersister persister, OptionKey optionKey, object? value)
            => persister.TryPersist(_storages[0], optionKey, value);

        public bool TryFetch(VisualStudioOptionPersister persister, OptionKey optionKey, out object? value)
        {
            foreach (var storage in _storages)
            {
                if (persister.TryFetch(storage, optionKey, out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
        }
    }

    public static bool TryGetPrimaryStorage(OptionKey optionKey, out VisualStudioOptionStorage storage)
        => s_storages.TryGetValue((optionKey.Option.Feature, optionKey.Option.Name), out storage);

    private static readonly Dictionary<(string feature, string name), VisualStudioOptionStorage> s_storages = new()
    {
        {("BlockStructureOptions", "CollapseEmptyMetadataImplementationsWhenFirstOpened"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.CollapseEmptyMetadataImplementationsWhenFirstOpened")},
        {("BlockStructureOptions", "CollapseImportsWhenFirstOpened"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.CollapseImportsWhenFirstOpened")},
        {("BlockStructureOptions", "CollapseMetadataImplementationsWhenFirstOpened"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.CollapseMetadataImplementationsWhenFirstOpened")},
        {("BlockStructureOptions", "CollapseRegionsWhenCollapsingToDefinitions"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.CollapseRegionsWhenCollapsingToDefinitions")},
        {("BlockStructureOptions", "CollapseRegionsWhenFirstOpened"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.CollapseRegionsWhenFirstOpened")},
        {("BlockStructureOptions", "MaximumBannerLength"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.MaximumBannerLength")},
        {("BlockStructureOptions", "ShowBlockStructureGuidesForCodeLevelConstructs"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowBlockStructureGuidesForCodeLevelConstructs")},
        {("BlockStructureOptions", "ShowBlockStructureGuidesForCommentsAndPreprocessorRegions"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowBlockStructureGuidesForCommentsAndPreprocessorRegions")},
        {("BlockStructureOptions", "ShowBlockStructureGuidesForDeclarationLevelConstructs"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowBlockStructureGuidesForDeclarationLevelConstructs")},
        {("BlockStructureOptions", "ShowOutliningForCodeLevelConstructs"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowOutliningForCodeLevelConstructs")},
        {("BlockStructureOptions", "ShowOutliningForCommentsAndPreprocessorRegions"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowOutliningForCommentsAndPreprocessorRegions")},
        {("BlockStructureOptions", "ShowOutliningForDeclarationLevelConstructs"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowOutliningForDeclarationLevelConstructs")},
        {("BraceCompletionOptions", "AutoFormattingOnCloseBrace"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Auto Formatting On Close Brace")},
        {("ClassificationOptions", "ClassifyReassignedVariables"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ClassificationOptions.ClassifyReassignedVariables")},
        {("CodeStyleOptions", "AllowMultipleBlankLines"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.AllowMultipleBlankLines")},
        {("CodeStyleOptions", "AllowStatementImmediatelyAfterBlock"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.AllowStatementImmediatelyAfterBlock")},
        {("CodeStyleOptions", "ArithmeticBinaryParentheses"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ArithmeticBinaryParenthesesPreference")},
        {("CodeStyleOptions", "OtherBinaryParentheses"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.OtherBinaryParenthesesPreference")},
        {("CodeStyleOptions", "OtherParentheses"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.OtherParenthesesPreference")},
        {("CodeStyleOptions", "PreferAutoProperties"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferAutoProperties")},
        {("CodeStyleOptions", "PreferCoalesceExpression"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferCoalesceExpression")},
        {("CodeStyleOptions", "PreferCollectionInitializer"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferCollectionInitializer")},
        {("CodeStyleOptions", "PreferCompoundAssignment"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferCompoundAssignment")},
        {("CodeStyleOptions", "PreferConditionalExpressionOverAssignment"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferConditionalExpressionOverAssignment")},
        {("CodeStyleOptions", "PreferConditionalExpressionOverReturn"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferConditionalExpressionOverReturn")},
        {("CodeStyleOptions", "PreferExplicitTupleNames"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferExplicitTupleNames")},
        {("CodeStyleOptions", "PreferInferredAnonymousTypeMemberNames"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferInferredAnonymousTypeMemberNames")},
        {("CodeStyleOptions", "PreferInferredTupleNames"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferInferredTupleNames")},
        {("CodeStyleOptions", "PreferIntrinsicPredefinedTypeKeywordInDeclaration"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferIntrinsicPredefinedTypeKeywordInDeclaration.CodeStyle")},
        {("CodeStyleOptions", "PreferIntrinsicPredefinedTypeKeywordInMemberAccess"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferIntrinsicPredefinedTypeKeywordInMemberAccess.CodeStyle")},
        {("CodeStyleOptions", "PreferIsNullCheckOverReferenceEqualityMethod"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferIsNullCheckOverReferenceEqualityMethod")},
        {("CodeStyleOptions", "PreferNamespaceAndFolderMatchStructure"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferNamespaceAndFolderMatchStructure")},
        {("CodeStyleOptions", "PreferNullPropagation"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferNullPropagation")},
        {("CodeStyleOptions", "PreferObjectInitializer"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferObjectInitializer")},
        {("CodeStyleOptions", "PreferReadonly"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferReadonly")},
        {("CodeStyleOptions", "PreferSimplifiedBooleanExpressions"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferSimplifiedBooleanExpressions")},
        {("CodeStyleOptions", "PreferSimplifiedInterpolation"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferSimplifiedInterpolation")},
        {("CodeStyleOptions", "PreferSystemHashCode"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferSystemHashCode")},
        {("CodeStyleOptions", "QualifyEventAccess"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.QualifyEventAccess")},
        {("CodeStyleOptions", "QualifyFieldAccess"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.QualifyFieldAccess")},
        {("CodeStyleOptions", "QualifyMethodAccess"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.QualifyMethodAccess")},
        {("CodeStyleOptions", "QualifyPropertyAccess"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.QualifyPropertyAccess")},
        {("CodeStyleOptions", "RelationalBinaryParentheses"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.RelationalBinaryParenthesesPreference")},
        {("CodeStyleOptions", "RemoveUnnecessarySuppressionExclusions"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.RemoveUnnecessarySuppressionExclusions")},
        {("CodeStyleOptions", "RequireAccessibilityModifiers"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.RequireAccessibilityModifiers")},
        {("CodeStyleOptions", "UnusedParameters"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.UnusedParametersPreference")},
        {("ColorSchemeOptions", "ColorSchemeName"), new RoamingProfileStorage("TextEditor.Roslyn.ColorSchemeName")},
        {("ColorSchemeOptions", "LegacyUseEnhancedColors"), new RoamingProfileStorage("WindowManagement.Options.UseEnhancedColorsForManagedLanguages")},
        {("CompletionOptions", "BlockForCompletionItems"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.BlockForCompletionItems")},
        {("CompletionOptions", "EnableArgumentCompletionSnippets"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.EnableArgumentCompletionSnippets")},
        {("CompletionOptions", "EnterKeyBehavior"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.EnterKeyBehavior")},
        {("CompletionOptions", "HideAdvancedMembers"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Hide Advanced Auto List Members")},
        {("CompletionOptions", "HighlightMatchingPortionsOfCompletionListItems"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.HighlightMatchingPortionsOfCompletionListItems")},
        {("CompletionOptions", "ShowCompletionItemFilters"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowCompletionItemFilters")},
        {("CompletionOptions", "ShowItemsFromUnimportedNamespaces"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowItemsFromUnimportedNamespaces")},
        {("CompletionOptions", "ShowNameSuggestions"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowNameSuggestions")},
        {("CompletionOptions", "ShowNewSnippetExperienceFeatureFlag"), new FeatureFlagStorage(@"Roslyn.SnippetCompletion")},
        {("CompletionOptions", "ShowNewSnippetExperienceUserOption"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowNewSnippetExperience")},
        {("CompletionOptions", "SnippetsBehavior"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.SnippetsBehavior")},
        {("CompletionOptions", "TriggerInArgumentLists"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.TriggerInArgumentLists")},
        {("CompletionOptions", "TriggerOnDeletion"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.TriggerOnDeletion")},
        {("CompletionOptions", "TriggerOnTyping"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Auto List Members")},
        {("CompletionOptions", "TriggerOnTypingLetters"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.TriggerOnTypingLetters")},
        {("CompletionOptions", "UnnamedSymbolCompletionDisabledFeatureFlag"), new FeatureFlagStorage(@"Roslyn.UnnamedSymbolCompletionDisabled")},
        {("CSharpCodeStyleOptions", "AllowBlankLineAfterColonInConstructorInitializer"), new RoamingProfileStorage("TextEditor.CSharp.Specific.AllowBlankLineAfterColonInConstructorInitializer")},
        {("CSharpCodeStyleOptions", "AllowBlankLineAfterTokenInArrowExpressionClause"), new RoamingProfileStorage("TextEditor.CSharp.Specific.AllowBlankLineAfterTokenInArrowExpressionClause")},
        {("CSharpCodeStyleOptions", "AllowBlankLineAfterTokenInConditionalExpression"), new RoamingProfileStorage("TextEditor.CSharp.Specific.AllowBlankLineAfterTokenInConditionalExpression")},
        {("CSharpCodeStyleOptions", "AllowBlankLinesBetweenConsecutiveBraces"), new RoamingProfileStorage("TextEditor.CSharp.Specific.AllowBlankLinesBetweenConsecutiveBraces")},
        {("CSharpCodeStyleOptions", "AllowEmbeddedStatementsOnSameLine"), new RoamingProfileStorage("TextEditor.CSharp.Specific.AllowEmbeddedStatementsOnSameLine")},
        {("CSharpCodeStyleOptions", "ImplicitObjectCreationWhenTypeIsApparent"), new RoamingProfileStorage("TextEditor.CSharp.Specific.ImplicitObjectCreationWhenTypeIsApparent")},
        {("CSharpCodeStyleOptions", "NamespaceDeclarations"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NamespaceDeclarations")},
        {("CSharpCodeStyleOptions", "PreferBraces"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferBraces")},
        {("CSharpCodeStyleOptions", "PreferConditionalDelegateCall"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferConditionalDelegateCall")},
        {("CSharpCodeStyleOptions", "PreferDeconstructedVariableDeclaration"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferDeconstructedVariableDeclaration")},
        {("CSharpCodeStyleOptions", "PreferExpressionBodiedAccessors"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferExpressionBodiedAccessors")},
        {("CSharpCodeStyleOptions", "PreferExpressionBodiedConstructors"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferExpressionBodiedConstructors")},
        {("CSharpCodeStyleOptions", "PreferExpressionBodiedIndexers"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferExpressionBodiedIndexers")},
        {("CSharpCodeStyleOptions", "PreferExpressionBodiedLambdas"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferExpressionBodiedLambdas")},
        {("CSharpCodeStyleOptions", "PreferExpressionBodiedLocalFunctions"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferExpressionBodiedLocalFunctions")},
        {("CSharpCodeStyleOptions", "PreferExpressionBodiedMethods"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferExpressionBodiedMethods")},
        {("CSharpCodeStyleOptions", "PreferExpressionBodiedOperators"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferExpressionBodiedOperators")},
        {("CSharpCodeStyleOptions", "PreferExpressionBodiedProperties"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferExpressionBodiedProperties")},
        {("CSharpCodeStyleOptions", "PreferExtendedPropertyPattern"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferExtendedPropertyPattern")},
        {("CSharpCodeStyleOptions", "PreferIndexOperator"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferIndexOperator")},
        {("CSharpCodeStyleOptions", "PreferInlinedVariableDeclaration"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferInlinedVariableDeclaration")},
        {("CSharpCodeStyleOptions", "PreferLocalOverAnonymousFunction"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferLocalOverAnonymousFunction")},
        {("CSharpCodeStyleOptions", "PreferMethodGroupConversion"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferMethodGroupConversion")},
        {("CSharpCodeStyleOptions", "PreferNotPattern"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferNotPattern")},
        {("CSharpCodeStyleOptions", "PreferNullCheckOverTypeCheck"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferNullCheckOverTypeCheck")},
        {("CSharpCodeStyleOptions", "PreferPatternMatching"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferPatternMatching")},
        {("CSharpCodeStyleOptions", "PreferPatternMatchingOverAsWithNullCheck"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferPatternMatchingOverAsWithNullCheck")},
        {("CSharpCodeStyleOptions", "PreferPatternMatchingOverIsWithCastCheck"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferPatternMatchingOverIsWithCastCheck")},
        {("CSharpCodeStyleOptions", "PreferRangeOperator"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferRangeOperator")},
        {("CSharpCodeStyleOptions", "PreferReadOnlyStruct"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferReadOnlyStruct")},
        {("CSharpCodeStyleOptions", "PreferredModifierOrder"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferredModifierOrder")},
        {("CSharpCodeStyleOptions", "PreferredUsingDirectivePlacement"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferredUsingDirectivePlacement")},
        {("CSharpCodeStyleOptions", "PreferSimpleDefaultExpression"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferSimpleDefaultExpression")},
        {("CSharpCodeStyleOptions", "PreferSimpleUsingStatement"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferSimpleUsingStatement")},
        {("CSharpCodeStyleOptions", "PreferStaticLocalFunction"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferStaticLocalFunction")},
        {("CSharpCodeStyleOptions", "PreferSwitchExpression"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferSwitchExpression")},
        {("CSharpCodeStyleOptions", "PreferThrowExpression"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferThrowExpression")},
        {("CSharpCodeStyleOptions", "PreferTopLevelStatements"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferTopLevelStatements")},
        {("CSharpCodeStyleOptions", "PreferTupleSwap"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferTupleSwap")},
        {("CSharpCodeStyleOptions", "PreferUtf8StringLiterals"), new RoamingProfileStorage("TextEditor.CSharp.Specific.PreferUtf8StringLiterals")},
        {("CSharpCodeStyleOptions", "UnusedValueAssignment"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.UnusedValueAssignmentPreference")},
        {("CSharpCodeStyleOptions", "UnusedValueExpressionStatement"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.UnusedValueExpressionStatementPreference")},
        {("CSharpCodeStyleOptions", "VarElsewhere"), new RoamingProfileStorage("TextEditor.CSharp.Specific.UseImplicitTypeWherePossible")},
        {("CSharpCodeStyleOptions", "VarForBuiltInTypes"), new RoamingProfileStorage("TextEditor.CSharp.Specific.UseImplicitTypeForIntrinsicTypes")},
        {("CSharpCodeStyleOptions", "VarWhenTypeIsApparent"), new RoamingProfileStorage("TextEditor.CSharp.Specific.UseImplicitTypeWhereApparent")},
        {("CSharpFormattingOptions", "IndentBlock"), new RoamingProfileStorage("TextEditor.CSharp.Specific.IndentBlock")},
        {("CSharpFormattingOptions", "IndentBraces"), new RoamingProfileStorage("TextEditor.CSharp.Specific.OpenCloseBracesIndent")},
        {("CSharpFormattingOptions", "IndentSwitchCaseSection"), new RoamingProfileStorage("TextEditor.CSharp.Specific.IndentSwitchCaseSection")},
        {("CSharpFormattingOptions", "IndentSwitchCaseSectionWhenBlock"), new RoamingProfileStorage("TextEditor.CSharp.Specific.IndentSwitchCaseSectionWhenBlock")},
        {("CSharpFormattingOptions", "IndentSwitchSection"), new RoamingProfileStorage("TextEditor.CSharp.Specific.IndentSwitchSection")},
        {("CSharpFormattingOptions", "LabelPositioning"), new RoamingProfileStorage("TextEditor.CSharp.Specific.LabelPositioning")},
        {("CSharpFormattingOptions", "NewLineForCatch"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLineForCatch")},
        {("CSharpFormattingOptions", "NewLineForClausesInQuery"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLineForClausesInQuery")},
        {("CSharpFormattingOptions", "NewLineForElse"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLineForElse")},
        {("CSharpFormattingOptions", "NewLineForFinally"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLineForFinally")},
        {("CSharpFormattingOptions", "NewLineForMembersInAnonymousTypes"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLineForMembersInAnonymousTypes")},
        {("CSharpFormattingOptions", "NewLineForMembersInObjectInit"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLineForMembersInObjectInit")},
        {("CSharpFormattingOptions", "NewLinesForBracesInAccessors"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLinesForBracesInAccessors")},
        {("CSharpFormattingOptions", "NewLinesForBracesInAnonymousMethods"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLinesForBracesInAnonymousMethods")},
        {("CSharpFormattingOptions", "NewLinesForBracesInAnonymousTypes"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLinesForBracesInAnonymousTypes")},
        {("CSharpFormattingOptions", "NewLinesForBracesInControlBlocks"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLinesForBracesInControlBlocks")},
        {("CSharpFormattingOptions", "NewLinesForBracesInLambdaExpressionBody"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLinesForBracesInLambdaExpressionBody")},
        {("CSharpFormattingOptions", "NewLinesForBracesInMethods"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLinesForBracesInMethods")},
        {("CSharpFormattingOptions", "NewLinesForBracesInObjectCollectionArrayInitializers"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLinesForBracesInObjectCollectionArrayInitializers")},
        {("CSharpFormattingOptions", "NewLinesForBracesInProperties"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLinesForBracesInProperties")},
        {("CSharpFormattingOptions", "NewLinesForBracesInTypes"), new RoamingProfileStorage("TextEditor.CSharp.Specific.NewLinesForBracesInTypes")},
        {("CSharpFormattingOptions", "SpaceAfterCast"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceAfterCast")},
        {("CSharpFormattingOptions", "SpaceAfterColonInBaseTypeDeclaration"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceAfterColonInBaseTypeDeclaration")},
        {("CSharpFormattingOptions", "SpaceAfterComma"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceAfterComma")},
        {("CSharpFormattingOptions", "SpaceAfterControlFlowStatementKeyword"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceAfterControlFlowStatementKeyword")},
        {("CSharpFormattingOptions", "SpaceAfterDot"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceAfterDot")},
        {("CSharpFormattingOptions", "SpaceAfterMethodCallName"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceAfterMethodCallName")},
        {("CSharpFormattingOptions", "SpaceAfterSemicolonsInForStatement"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceAfterSemicolonsInForStatement")},
        {("CSharpFormattingOptions", "SpaceBeforeColonInBaseTypeDeclaration"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceBeforeColonInBaseTypeDeclaration")},
        {("CSharpFormattingOptions", "SpaceBeforeComma"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceBeforeComma")},
        {("CSharpFormattingOptions", "SpaceBeforeDot"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceBeforeDot")},
        {("CSharpFormattingOptions", "SpaceBeforeOpenSquareBracket"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceBeforeOpenSquareBracket")},
        {("CSharpFormattingOptions", "SpaceBeforeSemicolonsInForStatement"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceBeforeSemicolonsInForStatement")},
        {("CSharpFormattingOptions", "SpaceBetweenEmptyMethodCallParentheses"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceBetweenEmptyMethodCallParentheses")},
        {("CSharpFormattingOptions", "SpaceBetweenEmptyMethodDeclarationParentheses"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceBetweenEmptyMethodDeclarationParentheses")},
        {("CSharpFormattingOptions", "SpaceBetweenEmptySquareBrackets"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceBetweenEmptySquareBrackets")},
        {("CSharpFormattingOptions", "SpacesIgnoreAroundVariableDeclaration"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpacesIgnoreAroundVariableDeclaration")},
        {("CSharpFormattingOptions", "SpaceWithinCastParentheses"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceWithinCastParentheses")},
        {("CSharpFormattingOptions", "SpaceWithinExpressionParentheses"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceWithinExpressionParentheses")},
        {("CSharpFormattingOptions", "SpaceWithinMethodCallParentheses"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceWithinMethodCallParentheses")},
        {("CSharpFormattingOptions", "SpaceWithinMethodDeclarationParenthesis"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceWithinMethodDeclarationParenthesis")},
        {("CSharpFormattingOptions", "SpaceWithinOtherParentheses"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceWithinOtherParentheses")},
        {("CSharpFormattingOptions", "SpaceWithinSquareBrackets"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpaceWithinSquareBrackets")},
        {("CSharpFormattingOptions", "SpacingAfterMethodDeclarationName"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpacingAfterMethodDeclarationName")},
        {("CSharpFormattingOptions", "SpacingAroundBinaryOperator"), new RoamingProfileStorage("TextEditor.CSharp.Specific.SpacingAroundBinaryOperator")},
        {("CSharpFormattingOptions", "WrappingKeepStatementsOnSingleLine"), new RoamingProfileStorage("TextEditor.CSharp.Specific.WrappingKeepStatementsOnSingleLine")},
        {("CSharpFormattingOptions", "WrappingPreserveSingleLine"), new RoamingProfileStorage("TextEditor.CSharp.Specific.WrappingPreserveSingleLine")},
        {("DateAndTime", "ProvideDateAndTimeCompletions"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ProvideDateAndTimeCompletions")},
        {("DiagnosticOptions", "LogTelemetryForBackgroundAnalyzerExecution"), new FeatureFlagStorage(@"Roslyn.LogTelemetryForBackgroundAnalyzerExecution")},
        {("DiagnosticOptions", "LspPullDiagnosticsFeatureFlag"), new FeatureFlagStorage(@"Lsp.PullDiagnostics")},
        {("DiagnosticTaggingOptions", "PullDiagnosticTagging"), new FeatureFlagStorage(@"Roslyn.PullDiagnosticTagging")},
        {("DocumentationCommentOptions", "AutoXmlDocCommentGeneration"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Automatic XML Doc Comment Generation", "TextEditor.VisualBasic.Specific.AutoComment")},
        {("EditorComponentOnOffOptions", "Adornment"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Components", "Adornment")},
        {("EditorComponentOnOffOptions", "CodeRefactorings"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Components", "Code Refactorings")},
        {("EditorComponentOnOffOptions", "Tagger"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Components", "Tagger")},
        {("ExtractMethodOptions", "AllowBestEffort"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Allow Best Effort")},
        {("ExtractMethodOptions", "DontPutOutOrRefOnStruct"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Don't Put Out Or Ref On Strcut")},
        {("FadingOptions", "FadeOutUnreachableCode"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.FadeOutUnreachableCode")},
        {("FadingOptions", "FadeOutUnusedImports"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.FadeOutUnusedImports")},
        {("FeatureManager/Storage", "CloudCacheFeatureFlag"), new FeatureFlagStorage(@"Roslyn.CloudCache3")},
        {("FeatureManager/Storage", "Database"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Database")},
        {("FeatureOnOffOptions", "AddImportsOnPaste"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.AddImportsOnPaste2")},
        {("FeatureOnOffOptions", "AlwaysUseDefaultSymbolServers"), new RoamingProfileStorage("TextEditor.AlwaysUseDefaultSymbolServers")},
        {("FeatureOnOffOptions", "AutoInsertBlockCommentStartString"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Auto Insert Block Comment Start String")},
        {("FeatureOnOffOptions", "AutomaticallyCompleteStatementOnSemicolon"), new RoamingProfileStorage("TextEditor.AutomaticallyCompleteStatementOnSemicolon")},
        {("FeatureOnOffOptions", "AutomaticallyFixStringContentsOnPaste"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.AutomaticallyFixStringContentsOnPaste")},
        {("FeatureOnOffOptions", "AutomaticInsertionOfAbstractOrInterfaceMembers"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.AutoRequiredMemberInsert")},
        {("FeatureOnOffOptions", "EndConstruct"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.AutoEndInsert")},
        {("FeatureOnOffOptions", "InheritanceMarginCombinedWithIndicatorMargin"), new RoamingProfileStorage("TextEditor.InheritanceMarginCombinedWithIndicatorMargin")},
        {("FeatureOnOffOptions", "InheritanceMarginIncludeGlobalImports"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InheritanceMarginIncludeGlobalImports")},
        {("FeatureOnOffOptions", "KeywordHighlighting"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Keyword Highlighting", "TextEditor.VisualBasic.Specific.EnableHighlightRelatedKeywords")},
        {("FeatureOnOffOptions", "LineSeparator"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Line Separator", "TextEditor.VisualBasic.Specific.DisplayLineSeparators")},
        {("FeatureOnOffOptions", "NavigateAsynchronously"), new RoamingProfileStorage("TextEditor.NavigateAsynchronously")},
        {("FeatureOnOffOptions", "NavigateToDecompiledSources"), new RoamingProfileStorage("TextEditor.NavigateToDecompiledSources")},
        {("FeatureOnOffOptions", "NavigateToSourceLinkAndEmbeddedSources"), new RoamingProfileStorage("TextEditor.NavigateToSourceLinkAndEmbeddedSources")},
        {("FeatureOnOffOptions", "OfferRemoveUnusedReferences"), new RoamingProfileStorage("TextEditor.OfferRemoveUnusedReferences")},
        {("FeatureOnOffOptions", "OfferRemoveUnusedReferencesFeatureFlag"), new FeatureFlagStorage(@"Roslyn.RemoveUnusedReferences")},
        {("FeatureOnOffOptions", "Outlining"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Outlining")},
        {("FeatureOnOffOptions", "PrettyListing"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PrettyListing")},
        {("FeatureOnOffOptions", "ReferenceHighlighting"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Reference Highlighting", "TextEditor.VisualBasic.Specific.EnableHighlightReferences")},
        {("FeatureOnOffOptions", "RenameTrackingPreview"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Rename Tracking Preview", "TextEditor.VisualBasic.Specific.RenameTrackingPreview")},
        {("FeatureOnOffOptions", "ShowInheritanceMargin"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowInheritanceMargin")},
        {("FeatureOnOffOptions", "SkipAnalyzersForImplicitlyTriggeredBuilds"), new RoamingProfileStorage("TextEditor.SkipAnalyzersForImplicitlyTriggeredBuilds")},
        {("FeatureOnOffOptions", "StringIdentation"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.StringIdentation")},
        {("FindUsagesOptions", "DefinitionGroupingPriority"), new LocalUserProfileStorage(@"Roslyn\Internal\FindUsages", "DefinitionGroupingPriority")},
        {("FormattingOptions", "AutoFormattingOnReturn"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Auto Formatting On Return")},
        {("FormattingOptions", "AutoFormattingOnSemicolon"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Auto Formatting On Semicolon")},
        {("FormattingOptions", "AutoFormattingOnTyping"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.Auto Formatting On Typing")},
        {("FormattingOptions", "FormatOnPaste"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.FormatOnPaste")},
        {("FormattingOptions", "IndentationSize"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Indent Size")},
        {("FormattingOptions", "SmartIndent"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Indent Style")},
        {("FormattingOptions", "TabSize"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Tab Size")},
        {("FormattingOptions", "UseTabs"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Insert Tabs")},
        {("GenerateConstructorFromMembersOptions", "AddNullChecks"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.GenerateConstructorFromMembersOptions.AddNullChecks")},
        {("GenerateEqualsAndGetHashCodeFromMembersOptions", "GenerateOperators"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.GenerateEqualsAndGetHashCodeFromMembersOptions.GenerateOperators")},
        {("GenerateEqualsAndGetHashCodeFromMembersOptions", "ImplementIEquatable"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.GenerateEqualsAndGetHashCodeFromMembersOptions.ImplementIEquatable")},
        {("GenerateOverridesOptions", "SelectAll"), new RoamingProfileStorage("TextEditor.Specific.GenerateOverridesOptions.SelectAll")},
        {("GenerationOptions", "PlaceSystemNamespaceFirst"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PlaceSystemNamespaceFirst")},
        {("GenerationOptions", "SeparateImportDirectiveGroups"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.SeparateImportDirectiveGroups")},
        {("ImplementTypeOptions", "InsertionBehavior"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.ImplementTypeOptions.InsertionBehavior")},
        {("ImplementTypeOptions", "PropertyGenerationBehavior"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.ImplementTypeOptions.PropertyGenerationBehavior")},
        {("InlineDiagnosticsOptions", "EnableInlineDiagnostics"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineDiagnostics")},
        {("InlineDiagnosticsOptions", "Location"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineDiagnostics.LocationOption")},
        {("InlineHintsOptions", "ColorHints"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ColorHints")},
        {("InlineHintsOptions", "DisplayAllHintsWhilePressingAltF1"), new RoamingProfileStorage("TextEditor.Specific.DisplayAllHintsWhilePressingAltF1")},
        {("InlineHintsOptions", "EnabledForParameters"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineParameterNameHints")},
        {("InlineHintsOptions", "EnabledForTypes"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineTypeHints")},
        {("InlineHintsOptions", "ForImplicitObjectCreation"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineTypeHints.ForImplicitObjectCreation")},
        {("InlineHintsOptions", "ForImplicitVariableTypes"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineTypeHints.ForImplicitVariableTypes")},
        {("InlineHintsOptions", "ForIndexerParameters"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineParameterNameHints.ForArrayIndexers")},
        {("InlineHintsOptions", "ForLambdaParameterTypes"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineTypeHints.ForLambdaParameterTypes")},
        {("InlineHintsOptions", "ForLiteralParameters"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineParameterNameHints.ForLiteralParameters")},
        {("InlineHintsOptions", "ForObjectCreationParameters"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineParameterNameHints.ForObjectCreationParameters")},
        {("InlineHintsOptions", "ForOtherParameters"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineParameterNameHints.ForOtherParameters")},
        {("InlineHintsOptions", "SuppressForParametersThatDifferOnlyBySuffix"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineParameterNameHints.SuppressForParametersThatDifferOnlyBySuffix")},
        {("InlineHintsOptions", "SuppressForParametersThatMatchArgumentName"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineParameterNameHints.SuppressForParametersThatMatchArgumentName")},
        {("InlineHintsOptions", "SuppressForParametersThatMatchMethodIntent"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.InlineParameterNameHints.SuppressForParametersThatMatchMethodIntent")},
        {("InlineRename", "CollapseRenameUI"), new RoamingProfileStorage("TextEditor.CollapseRenameUI")},
        {("InlineRename", "UseInlineAdornment"), new RoamingProfileStorage("TextEditor.RenameUseInlineAdornment")},
        {("InlineRenameSessionOptions", "PreviewChanges"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreviewRename")},
        {("InlineRenameSessionOptions", "RenameAsynchronously"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.RenameAsynchronously")},
        {("InlineRenameSessionOptions", "RenameFile"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.RenameFile")},
        {("InlineRenameSessionOptions", "RenameInComments"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.RenameInComments")},
        {("InlineRenameSessionOptions", "RenameInStrings"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.RenameInStrings")},
        {("InlineRenameSessionOptions", "RenameOverloads"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.RenameOverloads")},
        {("InternalDiagnosticsOptions", "CrashOnAnalyzerException"), new LocalUserProfileStorage(@"Roslyn\Internal\Diagnostics", "CrashOnAnalyzerException")},
        {("InternalDiagnosticsOptions", "EnableFileLoggingForDiagnostics"), new LocalUserProfileStorage(@"Roslyn\Internal\Diagnostics", "EnableFileLoggingForDiagnostics")},
        {("InternalDiagnosticsOptions", "NormalDiagnosticMode"), new LocalUserProfileStorage(@"Roslyn\Internal\Diagnostics", "NormalDiagnosticMode")},
        {("InternalFeatureOnOffOptions", "AutomaticLineEnder"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Automatic Line Ender")},
        {("InternalFeatureOnOffOptions", "BraceMatching"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Brace Matching")},
        {("InternalFeatureOnOffOptions", "Classification"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Classification")},
        {("InternalFeatureOnOffOptions", "EventHookup"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Event Hookup")},
        {("InternalFeatureOnOffOptions", "FormatOnSave"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "FormatOnSave")},
        {("InternalFeatureOnOffOptions", "FullSolutionAnalysisMemoryMonitor"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Full Solution Analysis Memory Monitor")},
        {("InternalFeatureOnOffOptions", "OOP64Bit"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "OOP64Bit")},
        {("InternalFeatureOnOffOptions", "OOPCoreClrFeatureFlag"), new FeatureFlagStorage(@"Roslyn.ServiceHubCore")},
        {("InternalFeatureOnOffOptions", "OOPServerGCFeatureFlag"), new FeatureFlagStorage(@"Roslyn.OOPServerGC")},
        {("InternalFeatureOnOffOptions", "RemoveRecommendationLimit"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "RemoveRecommendationLimit")},
        {("InternalFeatureOnOffOptions", "RenameTracking"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Rename Tracking")},
        {("InternalFeatureOnOffOptions", "SemanticColorizer"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Semantic Colorizer")},
        {("InternalFeatureOnOffOptions", "ShowDebugInfo"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "ShowDebugInfo")},
        {("InternalFeatureOnOffOptions", "SmartIndenter"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Smart Indenter")},
        {("InternalFeatureOnOffOptions", "Snippets"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Snippets2")},
        {("InternalFeatureOnOffOptions", "Squiggles"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Squiggles")},
        {("InternalFeatureOnOffOptions", "SyntacticColorizer"), new LocalUserProfileStorage(@"Roslyn\Internal\OnOff\Features", "Syntactic Colorizer")},
        {("InternalSolutionCrawlerOptions", "Solution Crawler"), new LocalUserProfileStorage(@"Roslyn\Internal\SolutionCrawler", "Solution Crawler")},
        {("JsonFeatureOptions", "ColorizeJsonPatterns"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ColorizeJsonPatterns")},
        {("JsonFeatureOptions", "DetectAndOfferEditorFeaturesForProbableJsonStrings"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.DetectAndOfferEditorFeaturesForProbableJsonStrings")},
        {("JsonFeatureOptions", "HighlightRelatedJsonComponentsUnderCursor"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.HighlightRelatedJsonComponentsUnderCursor")},
        {("JsonFeatureOptions", "ReportInvalidJsonPatterns"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ReportInvalidJsonPatterns")},
        {("KeybindingResetOptions", "EnabledFeatureFlag"), new FeatureFlagStorage(@"Roslyn.KeybindingResetEnabled")},
        {("KeybindingResetOptions", "NeedsReset"), new LocalUserProfileStorage(@"Roslyn\Internal\KeybindingsStatus", "NeedsReset")},
        {("KeybindingResetOptions", "NeverShowAgain"), new LocalUserProfileStorage(@"Roslyn\Internal\KeybindingsStatus", "NeverShowAgain")},
        {("KeybindingResetOptions", "ReSharperStatus"), new LocalUserProfileStorage(@"Roslyn\Internal\KeybindingsStatus", "ReSharperStatus")},
        {("LoggerOptions", "EtwLoggerKey"), new LocalUserProfileStorage(@"Roslyn\Internal\Performance\Logger", "EtwLogger")},
        {("LoggerOptions", "OutputWindowLoggerKey"), new LocalUserProfileStorage(@"Roslyn\Internal\Performance\Logger", "OutputWindowLogger")},
        {("LoggerOptions", "TraceLoggerKey"), new LocalUserProfileStorage(@"Roslyn\Internal\Performance\Logger", "TraceLogger")},
        {("LspOptions", "LspEditorFeatureFlag"), new FeatureFlagStorage(@"Roslyn.LSP.Editor")},
        {("LspOptions", "LspSemanticTokensFeatureFlag"), new FeatureFlagStorage(@"Roslyn.LSP.SemanticTokens")},
        {("LspOptions", "MaxCompletionListSize"), new LocalUserProfileStorage(@"Roslyn\Internal\Lsp", "MaxCompletionListSize")},
        {("NavigationBarOptions", "ShowNavigationBar"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Dropdown Bar")},
        {("QuickInfoOptions", "IncludeNavigationHintsInQuickInfo"), new RoamingProfileStorage("TextEditor.Specific.IncludeNavigationHintsInQuickInfo")},
        {("QuickInfoOptions", "ShowRemarksInQuickInfo"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ShowRemarks")},
        {("RegularExpressionsOptions", "ColorizeRegexPatterns"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ColorizeRegexPatterns")},
        {("RegularExpressionsOptions", "HighlightRelatedRegexComponentsUnderCursor"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.HighlightRelatedRegexComponentsUnderCursor")},
        {("RegularExpressionsOptions", "ProvideRegexCompletions"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ProvideRegexCompletions")},
        {("RegularExpressionsOptions", "ReportInvalidRegexPatterns"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ReportInvalidRegexPatterns")},
        {("ServiceFeatureOnOffOptions", "RemoveDocumentDiagnosticsOnDocumentClose"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.RemoveDocumentDiagnosticsOnDocumentClose")},
        {("SignatureHelpOptions", "ShowSignatureHelp"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Auto List Params")},
        {("SimplificationOptions", "AllowSimplificationToBaseType"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.AllowSimplificationToBaseType")},
        {("SimplificationOptions", "AllowSimplificationToGenericType"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.AllowSimplificationToGenericType")},
        {("SimplificationOptions", "NamingPreferences"), new CompositeStorage(new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.NamingPreferences5"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.NamingPreferences"))},
        {("SimplificationOptions", "PreferAliasToQualification"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferAliasToQualification")},
        {("SimplificationOptions", "PreferImplicitTypeInference"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferImplicitTypeInference")},
        {("SimplificationOptions", "PreferImplicitTypeInLocalDeclaration"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferImplicitTypeInLocalDeclaration")},
        {("SimplificationOptions", "PreferOmittingModuleNamesInQualification"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferOmittingModuleNamesInQualification")},
        {("SimplificationOptions", "QualifyEventAccess"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.QualifyEventAccess")},
        {("SimplificationOptions", "QualifyFieldAccess"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.QualifyFieldAccess")},
        {("SimplificationOptions", "QualifyMemberAccessWithThisOrMe"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.QualifyMemberAccessWithThisOrMe")},
        {("SimplificationOptions", "QualifyMethodAccess"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.QualifyMethodAccess")},
        {("SimplificationOptions", "QualifyPropertyAccess"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.QualifyPropertyAccess")},
        {("SolutionCrawlerOptionsStorage", "BackgroundAnalysisScopeOption"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.BackgroundAnalysisScopeOption")},
        {("SolutionCrawlerOptionsStorage", "CompilerDiagnosticsScopeOption"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.CompilerDiagnosticsScopeOption")},
        {("SplitCommentOptions", "Enabled"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.SplitComments")},
        {("SplitStringLiteralOptions", "Enabled"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.SplitStringLiterals")},
        {("StackTraceExplorerOptions", "OpenOnFocus"), new RoamingProfileStorage("StackTraceExplorer.Options.OpenOnFocus")},
        {("SuggestionsOptions", "Asynchronous"), new RoamingProfileStorage("TextEditor.Specific.Suggestions.Asynchronous4")},
        {("SuggestionsOptions", "AsynchronousQuickActionsDisableFeatureFlag"), new FeatureFlagStorage(@"Roslyn.AsynchronousQuickActionsDisable2")},
        {("SymbolSearchOptions", "Enabled"), new LocalUserProfileStorage(@"Roslyn\Features\SymbolSearch", "Enabled")},
        {("SymbolSearchOptions", "SuggestForTypesInNuGetPackages"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.SuggestForTypesInNuGetPackages")},
        {("SymbolSearchOptions", "SuggestForTypesInReferenceAssemblies"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.SuggestForTypesInReferenceAssemblies")},
        {("TaskListOptionsStorage", "ComputeTaskListItemsForClosedFiles"), new RoamingProfileStorage("TextEditor.Specific.ComputeTaskListItemsForClosedFiles")},
        {("TaskListOptionsStorage", "Descriptors"), new RoamingProfileStorage("Microsoft.VisualStudio.ErrorListPkg.Shims.TaskListOptions.CommentTokens")},
        {("UseConditionalExpressionOptions", "ConditionalExpressionWrappingLength"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.ConditionalExpressionWrappingLength")},
        {("ValidateFormatStringOption", "ReportInvalidPlaceholdersInStringDotFormatCalls"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.WarnOnInvalidStringDotFormatCalls")},
        {("VisualBasicCodeStyleOptions", "PreferIsNotExpression"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferIsNotExpression")},
        {("VisualBasicCodeStyleOptions", "PreferredModifierOrder"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferredModifierOrder")},
        {("VisualBasicCodeStyleOptions", "PreferSimplifiedObjectCreation"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.PreferSimplifiedObjectCreation")},
        {("VisualBasicCodeStyleOptions", "UnusedValueAssignment"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.UnusedValueAssignmentPreference")},
        {("VisualBasicCodeStyleOptions", "UnusedValueExpressionStatement"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.UnusedValueExpressionStatementPreference")},
        {("VisualStudioNavigationOptions", "NavigateToObjectBrowser"), new RoamingProfileStorage("TextEditor.%LANGUAGE%.Specific.NavigateToObjectBrowser")},
        {("VisualStudioWorkspaceStatusService", "PartialLoadModeFeatureFlag"), new FeatureFlagStorage(@"Roslyn.PartialLoadMode")},
        {("WorkspaceConfigurationOptions", "DisableBackgroundCompilation"), new FeatureFlagStorage(@"Roslyn.DisableBackgroundCompilation")},
        {("WorkspaceConfigurationOptions", "DisableCloneWhenProducingSkeletonReferences"), new FeatureFlagStorage(@"Roslyn.DisableCloneWhenProducingSkeletonReferences")},
        {("WorkspaceConfigurationOptions", "DisableReferenceManagerRecoverableMetadata"), new FeatureFlagStorage(@"Roslyn.DisableReferenceManagerRecoverableMetadata")},
        {("WorkspaceConfigurationOptions", "EnableDiagnosticsInSourceGeneratedFiles"), new RoamingProfileStorage("TextEditor.Roslyn.Specific.EnableDiagnosticsInSourceGeneratedFilesExperiment")},
        {("WorkspaceConfigurationOptions", "EnableDiagnosticsInSourceGeneratedFilesFeatureFlag"), new FeatureFlagStorage(@"Roslyn.EnableDiagnosticsInSourceGeneratedFiles")},
        {("WorkspaceConfigurationOptions", "EnableOpeningSourceGeneratedFilesInWorkspace"), new RoamingProfileStorage("TextEditor.Roslyn.Specific.EnableOpeningSourceGeneratedFilesInWorkspaceExperiment")},
        {("WorkspaceConfigurationOptions", "EnableOpeningSourceGeneratedFilesInWorkspaceFeatureFlag"), new FeatureFlagStorage(@"Roslyn.SourceGeneratorsEnableOpeningInWorkspace")},
        {("XamlOptions", "EnableLspIntelliSenseFeatureFlag"), new FeatureFlagStorage(@"Xaml.EnableLspIntelliSense")},
    };
}
