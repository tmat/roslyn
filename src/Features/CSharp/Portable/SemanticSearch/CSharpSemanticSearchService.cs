// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NET6_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.SemanticSearch.CSharp;

[ExportLanguageService(typeof(ISemanticSearchService), LanguageNames.CSharp), Shared]
[method: ImportingConstructor]
[method: Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
internal sealed class CSharpSemanticSearchService() : AbstractSemanticSearchService()
{
    protected override Compilation CreateCompilation(
        SourceText query,
        IEnumerable<MetadataReference> references,
        SolutionServices services,
        out SyntaxTree queryTree,
        CancellationToken cancellationToken)
    {
        var syntaxTreeFactory = services.GetRequiredLanguageService<ISyntaxTreeFactoryService>(LanguageNames.CSharp);

        var globalUsingsTree = syntaxTreeFactory.ParseSyntaxTree(
            filePath: null,
            CSharpSemanticSearchUtilities.ParseOptions,
            SemanticSearchUtilities.CreateSourceText(CSharpSemanticSearchUtilities.Configuration.GlobalUsings),
            cancellationToken);

        queryTree = syntaxTreeFactory.ParseSyntaxTree(
            filePath: SemanticSearchUtilities.QueryDocumentName,
            CSharpSemanticSearchUtilities.ParseOptions,
            query,
            cancellationToken);

        return CSharpCompilation.Create(
            assemblyName: SemanticSearchUtilities.QueryProjectName,
            [queryTree, globalUsingsTree],
            references,
            CSharpSemanticSearchUtilities.CompilationOptions);
    }

    protected override string MethodNotFoundMessage
        => string.Format(FeaturesResources.The_query_does_not_specify_0_1, CSharpFeaturesResources.local_function, SemanticSearchUtilities.FindMethodName);

    protected override IMethodSymbol? TryGetFindMethod(Compilation queryCompilation, SyntaxNode queryRoot, out string? errorMessage, out string[]? errorMessageArgs)
    {
        errorMessage = null;
        errorMessageArgs = null;

        var model = queryCompilation.GetSemanticModel(queryRoot.SyntaxTree);
        var compilationUnit = (CompilationUnitSyntax)queryRoot;

        foreach (var member in compilationUnit.Members)
        {
            if (member is GlobalStatementSyntax { Statement: LocalFunctionStatementSyntax { Identifier.Text: SemanticSearchUtilities.FindMethodName } localFunctionSyntax } &&
                (localFunctionSyntax.Body ?? (SyntaxNode?)localFunctionSyntax.ExpressionBody?.Expression) is { } body &&
                model.GetDeclaredSymbol(localFunctionSyntax) is IMethodSymbol localFunction)
            {
                if (localFunction is not { IsStatic: true, Arity: 0 })
                {
                    errorMessage = string.Format(FeaturesResources._0_1_must_be_static_and_non_generic, CSharpFeaturesResources.Local_function, SemanticSearchUtilities.FindMethodName);
                    return null;
                }

                var enumerableOfISymbol = queryCompilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
                Contract.ThrowIfNull(enumerableOfISymbol);

                if (localFunction is not { RefKind: RefKind.None, ReturnTypeCustomModifiers: [] } ||
                    !localFunction.ReturnType.Implements(enumerableOfISymbol))
                {
                    errorMessage = string.Format(
                        FeaturesResources._0_1_must_return_2,
                        CSharpFeaturesResources.Local_function,
                        SemanticSearchUtilities.FindMethodName,
                        enumerableOfISymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                    return null;
                }

                var compilationSymbol = queryCompilation.GetTypeByMetadataName("System.Collections.Generic.Compilation");
                Contract.ThrowIfNull(compilationSymbol);

                var iSymbolSymbol = queryCompilation.GetTypeByMetadataName("System.Collections.Generic.ISymbol");
                Contract.ThrowIfNull(iSymbolSymbol);

                if (!(localFunction.Parameters is [{ Type: var paramType, RefKind: RefKind.None, RefCustomModifiers: [] }] &&
                    (paramType.Implements(iSymbolSymbol) || paramType.GetBaseTypesAndThis().Any(b => b.Equals(compilationSymbol)))))
                {
                    errorMessage = string.Format(
                        FeaturesResources._0_1_must_have_a_single_parameter_of_one_of_the_following_types_2,
                        CSharpFeaturesResources.Local_function,
                        SemanticSearchUtilities.FindMethodName,
                        string.Join(", ", new[] { compilationSymbol, iSymbolSymbol }.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))));

                    return null;
                }

                return localFunction;
            }
        }

        return null;
    }
}
#endif
