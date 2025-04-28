// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Emit;

public partial class CompilationEmitTests
{
    [Fact]
    public void MetadataTokenRequests_ArgumentValidation()
    {
        var compilation = CreateCompilation("class C;");
        var peStream = new MemoryStream();

        Assert.Throws<ArgumentNullException>(() => compilation.Emit(
            peStream,
            metadataTokenRequests: [null]));
    }

    private void Validate(string source, Func<Compilation, ISymbol> getSymbol)
    {
        var compilation = CreateCompilation(source);
        var peStream = new MemoryStream();

        var sourceSymbol = getSymbol(compilation);
        var result = compilation.Emit(peStream, metadataTokenRequests: [sourceSymbol]);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Verify();

        peStream.Position = 0;
        var reference = AssemblyMetadata.CreateFromStream(peStream).GetReference();

        var peCompilation = CreateCompilation("", [reference], options: TestOptions.DebugDll.WithMetadataImportOptions(MetadataImportOptions.All));

        var peSymbol = getSymbol(peCompilation);
        var expected = result.RequestedMetadataTokens.Single();
        var actual = sourceSymbol.MetadataToken;
        AssertEx.AreEqual(expected, actual, message: $"{sourceSymbol}: expected = 0x{expected:X8}, actual = 0x{actual:X8}");
    }

    [Theory]
    [InlineData("namespace N;", "N")]
    [InlineData("class C;", "C")]
    [InlineData("class C<T>;", "C")]
    [InlineData("class C;", "C..ctor")]
    [InlineData("class C { static C() {} }", "C..cctor")]
    [InlineData("class C { int F; }", "C.F")]
    [InlineData("class C { event System.Action E; }", "C.E")]
    [InlineData("class C { int P { get; set; } }", "C.P")]
    [InlineData("class C { int P { get; set; } }", "C.get_P")]
    [InlineData("class C { int P { get; set; } }", "C.set_P")]
    [InlineData("class C { Action E { add {} remove {} } }", "C.E")]
    [InlineData("class C { Action E { add {} remove {} } }", "C.add_E")]
    [InlineData("class C { Action E { add {} remove {} } }", "C.remove_E")]
    public void ValidateMetadataTokenRequest(string source, string qualifiedName)
        => Validate(source, c => c.GetMember(qualifiedName));

    [Fact]
    public void ValidateMetadataTokenRequest_Parameter()
        => Validate("class C { void M(int p) {} }", c => c.GetMember<IMethodSymbol>("C.M").Parameters.Single());

    [Fact]
    public void ValidateMetadataTokenRequest_TypeParameter_Method()
        => Validate("class C { void M<T>() {} }", c => c.GetMember<IMethodSymbol>("C.M").TypeParameters.Single());

    [Fact]
    public void ValidateMetadataTokenRequest_TypeParameter_Type()
        => Validate("class C<T>;", c => c.GetMember<INamedTypeSymbol>("C").TypeParameters.Single());
}
