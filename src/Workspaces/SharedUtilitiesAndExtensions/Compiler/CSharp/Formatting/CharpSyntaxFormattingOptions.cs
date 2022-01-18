// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace Microsoft.CodeAnalysis.CSharp.Formatting
{
    [Flags]
    internal enum SpacingFlags
    {
        SpacesIgnoreAroundVariableDeclaration = 1,
        SpacingAfterMethodDeclarationName = 1 << 1,
        SpaceBetweenEmptyMethodDeclarationParentheses = 1 << 2,
        SpaceWithinMethodDeclarationParenthesis = 1 << 3,
        SpaceAfterMethodCallName = 1 << 4,
        SpaceBetweenEmptyMethodCallParentheses = 1 << 5,
        SpaceWithinMethodCallParentheses = 1 << 6,
        SpaceAfterControlFlowStatementKeyword = 1 << 7,
        SpaceWithinExpressionParentheses = 1 << 8,
        SpaceWithinCastParentheses = 1 << 9,
        SpaceBeforeSemicolonsInForStatement = 1 << 10,
        SpaceAfterSemicolonsInForStatement = 1 << 11,
        SpaceWithinOtherParentheses = 1 << 12,
        SpaceAfterCast = 1 << 13,
        SpaceBeforeOpenSquareBracket = 1 << 14,
        SpaceBetweenEmptySquareBrackets = 1 << 15,
        SpaceWithinSquareBrackets = 1 << 16,
        SpaceAfterColonInBaseTypeDeclaration = 1 << 17,
        SpaceBeforeColonInBaseTypeDeclaration = 1 << 18,
        SpaceAfterComma = 1 << 19,
        SpaceBeforeComma = 1 << 20,
        SpaceAfterDot = 1 << 21,
        SpaceBeforeDot = 1 << 22,
    }

    [Flags]
    internal enum NewLineFlags
    {
        NewLineForMembersInObjectInit = 1,
        NewLineForMembersInAnonymousTypes = 1 << 1,
        NewLineForElse = 1 << 2,
        NewLineForCatch = 1 << 3,
        NewLineForFinally = 1 << 4,
        NewLinesForBracesInTypes = 1 << 5,
        NewLinesForBracesInAnonymousTypes = 1 << 6,
        NewLinesForBracesInObjectCollectionArrayInitializers = 1 << 7,
        NewLinesForBracesInProperties = 1 << 8,
        NewLinesForBracesInMethods = 1 << 9,
        NewLinesForBracesInAccessors = 1 << 10,
        NewLinesForBracesInAnonymousMethods = 1 << 11,
        NewLinesForBracesInLambdaExpressionBody = 1 << 12,
        NewLinesForBracesInControlBlocks = 1 << 13,
    }

    internal sealed class CSharpSyntaxFormattingOptions : SyntaxFormattingOptions
    {
        public readonly bool IndentBraces;

        public readonly SpacingFlags Spacing;
        public readonly BinaryOperatorSpacingOptions SpacingAroundBinaryOperator;

        public readonly NewLineFlags NewLines;
        public readonly bool WrappingKeepStatementsOnSingleLine;
        public readonly LabelPositionOptions LabelPositioning;
        public readonly bool IndentBlock;
        public readonly bool IndentSwitchCaseSection;
        public readonly bool IndentSwitchCaseSectionWhenBlock;
        public readonly bool IndentSwitchSection;
        public readonly bool NewLineForClausesInQuery;
        public readonly bool WrappingPreserveSingleLine;

        public CSharpSyntaxFormattingOptions(
            bool useTabs,
            int tabSize,
            int indentationSize,
            string newLine,
            bool separateImportDirectiveGroups,
            bool indentBraces,
            SpacingFlags spacing,
            BinaryOperatorSpacingOptions spacingAroundBinaryOperator,
            NewLineFlags newLines,
            bool wrappingKeepStatementsOnSingleLine,
            LabelPositionOptions labelPositioning,
            bool indentBlock,
            bool indentSwitchCaseSection,
            bool indentSwitchCaseSectionWhenBlock,
            bool indentSwitchSection,
            bool newLineForClausesInQuery,
            bool wrappingPreserveSingleLine)
            : base(useTabs,
                  tabSize,
                  indentationSize,
                  newLine,
                  separateImportDirectiveGroups)
        {
            IndentBraces = indentBraces;
            Spacing = spacing;
            SpacingAroundBinaryOperator = spacingAroundBinaryOperator;
            NewLines = newLines;
            WrappingKeepStatementsOnSingleLine = wrappingKeepStatementsOnSingleLine;
            LabelPositioning = labelPositioning;
            IndentBlock = indentBlock;
            IndentSwitchCaseSection = indentSwitchCaseSection;
            IndentSwitchCaseSectionWhenBlock = indentSwitchCaseSectionWhenBlock;
            IndentSwitchSection = indentSwitchSection;
            NewLineForClausesInQuery = newLineForClausesInQuery;
            WrappingPreserveSingleLine = wrappingPreserveSingleLine;
        }

        public static readonly CSharpSyntaxFormattingOptions Default = new(
            useTabs: FormattingOptions2.UseTabs.DefaultValue,
            tabSize: FormattingOptions2.TabSize.DefaultValue,
            indentationSize: FormattingOptions2.IndentationSize.DefaultValue,
            newLine: FormattingOptions2.NewLine.DefaultValue,
            separateImportDirectiveGroups: GenerationOptions.SeparateImportDirectiveGroups.DefaultValue,
            indentBraces: CSharpFormattingOptions2.IndentBraces.DefaultValue,
            spacing:
                (CSharpFormattingOptions2.SpacingAfterMethodDeclarationName.DefaultValue ? SpacingFlags.SpacesIgnoreAroundVariableDeclaration : 0) |
                (CSharpFormattingOptions2.SpaceBetweenEmptyMethodDeclarationParentheses.DefaultValue ? SpacingFlags.SpaceBetweenEmptyMethodDeclarationParentheses : 0) |
                (CSharpFormattingOptions2.SpaceWithinMethodDeclarationParenthesis.DefaultValue ? SpacingFlags.SpaceWithinMethodDeclarationParenthesis : 0) |
                (CSharpFormattingOptions2.SpaceAfterMethodCallName.DefaultValue ? SpacingFlags.SpaceAfterMethodCallName : 0) |
                (CSharpFormattingOptions2.SpaceBetweenEmptyMethodCallParentheses.DefaultValue ? SpacingFlags.SpaceBetweenEmptyMethodCallParentheses : 0) |
                (CSharpFormattingOptions2.SpaceWithinMethodCallParentheses.DefaultValue ? SpacingFlags.SpaceWithinMethodCallParentheses : 0) |
                (CSharpFormattingOptions2.SpaceAfterControlFlowStatementKeyword.DefaultValue ? SpacingFlags.SpaceAfterControlFlowStatementKeyword : 0) |
                (CSharpFormattingOptions2.SpaceWithinExpressionParentheses.DefaultValue ? SpacingFlags.SpaceWithinExpressionParentheses : 0) |
                (CSharpFormattingOptions2.SpaceWithinCastParentheses.DefaultValue ? SpacingFlags.SpaceWithinCastParentheses : 0) |
                (CSharpFormattingOptions2.SpaceBeforeSemicolonsInForStatement.DefaultValue ? SpacingFlags.SpaceBeforeSemicolonsInForStatement : 0) |
                (CSharpFormattingOptions2.SpaceAfterSemicolonsInForStatement.DefaultValue ? SpacingFlags.SpaceAfterSemicolonsInForStatement : 0) |
                (CSharpFormattingOptions2.SpaceWithinOtherParentheses.DefaultValue ? SpacingFlags.SpaceWithinOtherParentheses : 0) |
                (CSharpFormattingOptions2.SpaceAfterCast.DefaultValue ? SpacingFlags.SpaceAfterCast : 0) |
                (CSharpFormattingOptions2.SpaceBeforeOpenSquareBracket.DefaultValue ? SpacingFlags.SpaceBeforeOpenSquareBracket : 0) |
                (CSharpFormattingOptions2.SpaceBetweenEmptySquareBrackets.DefaultValue ? SpacingFlags.SpaceBetweenEmptySquareBrackets : 0) |
                (CSharpFormattingOptions2.SpaceWithinSquareBrackets.DefaultValue ? SpacingFlags.SpaceWithinSquareBrackets : 0) |
                (CSharpFormattingOptions2.SpaceAfterColonInBaseTypeDeclaration.DefaultValue ? SpacingFlags.SpaceAfterColonInBaseTypeDeclaration : 0) |
                (CSharpFormattingOptions2.SpaceBeforeColonInBaseTypeDeclaration.DefaultValue ? SpacingFlags.SpaceBeforeColonInBaseTypeDeclaration : 0) |
                (CSharpFormattingOptions2.SpaceAfterComma.DefaultValue ? SpacingFlags.SpaceAfterComma : 0) |
                (CSharpFormattingOptions2.SpaceBeforeComma.DefaultValue ? SpacingFlags.SpaceBeforeComma : 0) |
                (CSharpFormattingOptions2.SpaceAfterDot.DefaultValue ? SpacingFlags.SpaceAfterDot : 0) |
                (CSharpFormattingOptions2.SpaceBeforeDot.DefaultValue ? SpacingFlags.SpaceBeforeDot : 0),
            spacingAroundBinaryOperator: CSharpFormattingOptions2.SpacingAroundBinaryOperator.DefaultValue,
            newLines: 0,
            //newLineForMembersInObjectInit: CSharpFormattingOptions2.NewLineForMembersInObjectInit.DefaultValue,
            //newLineForMembersInAnonymousTypes: CSharpFormattingOptions2.NewLineForMembersInAnonymousTypes.DefaultValue,
            //newLineForElse: CSharpFormattingOptions2.NewLineForElse.DefaultValue,
            //newLineForCatch: CSharpFormattingOptions2.NewLineForCatch.DefaultValue,
            //newLineForFinally: CSharpFormattingOptions2.NewLineForFinally.DefaultValue,
            //newLinesForBracesInTypes: CSharpFormattingOptions2.NewLinesForBracesInTypes.DefaultValue,
            //newLinesForBracesInAnonymousTypes: CSharpFormattingOptions2.NewLinesForBracesInAnonymousTypes.DefaultValue,
            //newLinesForBracesInObjectCollectionArrayInitializers: CSharpFormattingOptions2.NewLinesForBracesInObjectCollectionArrayInitializers.DefaultValue,
            //newLinesForBracesInProperties: CSharpFormattingOptions2.NewLinesForBracesInProperties.DefaultValue,
            //newLinesForBracesInMethods: CSharpFormattingOptions2.NewLinesForBracesInMethods.DefaultValue,
            //newLinesForBracesInAccessors: CSharpFormattingOptions2.NewLinesForBracesInAccessors.DefaultValue,
            //newLinesForBracesInAnonymousMethods: CSharpFormattingOptions2.NewLinesForBracesInAnonymousMethods.DefaultValue,
            //newLinesForBracesInLambdaExpressionBody: CSharpFormattingOptions2.NewLinesForBracesInLambdaExpressionBody.DefaultValue,
            //newLinesForBracesInControlBlocks: CSharpFormattingOptions2.NewLinesForBracesInControlBlocks.DefaultValue,
            wrappingKeepStatementsOnSingleLine: CSharpFormattingOptions2.WrappingKeepStatementsOnSingleLine.DefaultValue,
            labelPositioning: CSharpFormattingOptions2.LabelPositioning.DefaultValue,
            indentBlock: CSharpFormattingOptions2.IndentBlock.DefaultValue,
            indentSwitchCaseSection: CSharpFormattingOptions2.IndentSwitchCaseSection.DefaultValue,
            indentSwitchCaseSectionWhenBlock: CSharpFormattingOptions2.IndentSwitchCaseSectionWhenBlock.DefaultValue,
            indentSwitchSection: CSharpFormattingOptions2.IndentSwitchSection.DefaultValue,
            newLineForClausesInQuery: CSharpFormattingOptions2.NewLineForClausesInQuery.DefaultValue,
            wrappingPreserveSingleLine: CSharpFormattingOptions2.WrappingPreserveSingleLine.DefaultValue);

        public static CSharpSyntaxFormattingOptions Create(AnalyzerConfigOptions options)
            => new(
                useTabs: options.GetOption(FormattingOptions2.UseTabs),
                tabSize: options.GetOption(FormattingOptions2.TabSize),
                indentationSize: options.GetOption(FormattingOptions2.IndentationSize),
                newLine: options.GetOption(FormattingOptions2.NewLine),
                separateImportDirectiveGroups: options.GetOption(GenerationOptions.SeparateImportDirectiveGroups),
                indentBraces: options.GetOption(CSharpFormattingOptions2.IndentBraces),
                spacing: 0,
                //spacesIgnoreAroundVariableDeclaration: options.GetOption(CSharpFormattingOptions2.SpacesIgnoreAroundVariableDeclaration),
                //spacingAfterMethodDeclarationName: options.GetOption(CSharpFormattingOptions2.SpacingAfterMethodDeclarationName),
                //spaceBetweenEmptyMethodDeclarationParentheses: options.GetOption(CSharpFormattingOptions2.SpaceBetweenEmptyMethodDeclarationParentheses),
                //spaceWithinMethodDeclarationParenthesis: options.GetOption(CSharpFormattingOptions2.SpaceWithinMethodDeclarationParenthesis),
                //spaceAfterMethodCallName: options.GetOption(CSharpFormattingOptions2.SpaceAfterMethodCallName),
                //spaceBetweenEmptyMethodCallParentheses: options.GetOption(CSharpFormattingOptions2.SpaceBetweenEmptyMethodCallParentheses),
                //spaceWithinMethodCallParentheses: options.GetOption(CSharpFormattingOptions2.SpaceWithinMethodCallParentheses),
                //spaceAfterControlFlowStatementKeyword: options.GetOption(CSharpFormattingOptions2.SpaceAfterControlFlowStatementKeyword),
                //spaceWithinExpressionParentheses: options.GetOption(CSharpFormattingOptions2.SpaceWithinExpressionParentheses),
                //spaceWithinCastParentheses: options.GetOption(CSharpFormattingOptions2.SpaceWithinCastParentheses),
                //spaceBeforeSemicolonsInForStatement: options.GetOption(CSharpFormattingOptions2.SpaceBeforeSemicolonsInForStatement),
                //spaceAfterSemicolonsInForStatement: options.GetOption(CSharpFormattingOptions2.SpaceAfterSemicolonsInForStatement),
                //spaceWithinOtherParentheses: options.GetOption(CSharpFormattingOptions2.SpaceWithinOtherParentheses),
                //spaceAfterCast: options.GetOption(CSharpFormattingOptions2.SpaceAfterCast),
                //spaceBeforeOpenSquareBracket: options.GetOption(CSharpFormattingOptions2.SpaceBeforeOpenSquareBracket),
                //spaceBetweenEmptySquareBrackets: options.GetOption(CSharpFormattingOptions2.SpaceBetweenEmptySquareBrackets),
                //spaceWithinSquareBrackets: options.GetOption(CSharpFormattingOptions2.SpaceWithinSquareBrackets),
                //spaceAfterColonInBaseTypeDeclaration: options.GetOption(CSharpFormattingOptions2.SpaceAfterColonInBaseTypeDeclaration),
                //spaceBeforeColonInBaseTypeDeclaration: options.GetOption(CSharpFormattingOptions2.SpaceBeforeColonInBaseTypeDeclaration),
                //spaceAfterComma: options.GetOption(CSharpFormattingOptions2.SpaceAfterComma),
                //spaceBeforeComma: options.GetOption(CSharpFormattingOptions2.SpaceBeforeComma),
                //spaceAfterDot: options.GetOption(CSharpFormattingOptions2.SpaceAfterDot),
                //spaceBeforeDot: options.GetOption(CSharpFormattingOptions2.SpaceBeforeDot),
                spacingAroundBinaryOperator: options.GetOption(CSharpFormattingOptions2.SpacingAroundBinaryOperator),
                //newLineForMembersInObjectInit: options.GetOption(CSharpFormattingOptions2.NewLineForMembersInObjectInit),
                //newLineForMembersInAnonymousTypes: options.GetOption(CSharpFormattingOptions2.NewLineForMembersInAnonymousTypes),
                //newLineForElse: options.GetOption(CSharpFormattingOptions2.NewLineForElse),
                //newLineForCatch: options.GetOption(CSharpFormattingOptions2.NewLineForCatch),
                //newLineForFinally: options.GetOption(CSharpFormattingOptions2.NewLineForFinally),
                //newLinesForBracesInTypes: options.GetOption(CSharpFormattingOptions2.NewLinesForBracesInTypes),
                //newLinesForBracesInAnonymousTypes: options.GetOption(CSharpFormattingOptions2.NewLinesForBracesInAnonymousTypes),
                //newLinesForBracesInObjectCollectionArrayInitializers: options.GetOption(CSharpFormattingOptions2.NewLinesForBracesInObjectCollectionArrayInitializers),
                //newLinesForBracesInProperties: options.GetOption(CSharpFormattingOptions2.NewLinesForBracesInProperties),
                //newLinesForBracesInMethods: options.GetOption(CSharpFormattingOptions2.NewLinesForBracesInMethods),
                //newLinesForBracesInAccessors: options.GetOption(CSharpFormattingOptions2.NewLinesForBracesInAccessors),
                //newLinesForBracesInAnonymousMethods: options.GetOption(CSharpFormattingOptions2.NewLinesForBracesInAnonymousMethods),
                //newLinesForBracesInLambdaExpressionBody: options.GetOption(CSharpFormattingOptions2.NewLinesForBracesInLambdaExpressionBody),
                //newLinesForBracesInControlBlocks: options.GetOption(CSharpFormattingOptions2.NewLinesForBracesInControlBlocks),
                newLines: 0,
                wrappingKeepStatementsOnSingleLine: options.GetOption(CSharpFormattingOptions2.WrappingKeepStatementsOnSingleLine),
                labelPositioning: options.GetOption(CSharpFormattingOptions2.LabelPositioning),
                indentBlock: options.GetOption(CSharpFormattingOptions2.IndentBlock),
                indentSwitchCaseSection: options.GetOption(CSharpFormattingOptions2.IndentSwitchCaseSection),
                indentSwitchCaseSectionWhenBlock: options.GetOption(CSharpFormattingOptions2.IndentSwitchCaseSectionWhenBlock),
                indentSwitchSection: options.GetOption(CSharpFormattingOptions2.IndentSwitchSection),
                newLineForClausesInQuery: options.GetOption(CSharpFormattingOptions2.NewLineForClausesInQuery),
                wrappingPreserveSingleLine: options.GetOption(CSharpFormattingOptions2.WrappingPreserveSingleLine));
    }
}
