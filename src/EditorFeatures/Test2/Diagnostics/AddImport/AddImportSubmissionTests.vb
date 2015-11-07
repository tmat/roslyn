' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports Microsoft.CodeAnalysis.CodeFixes
Imports Microsoft.CodeAnalysis.CSharp.CodeFixes.AddImport
Imports Microsoft.CodeAnalysis.Diagnostics
Imports Microsoft.CodeAnalysis.VisualBasic.CodeFixes.AddImport

Namespace Microsoft.CodeAnalysis.Editor.UnitTests.Diagnostics.AddImport

    Public Class AddImportSubmissionTests
        Inherits AbstractCrossLanguageUserDiagnosticTest

        Friend Overrides Function CreateDiagnosticProviderAndFixer(workspace As Workspace, language As String) As Tuple(Of DiagnosticAnalyzer, CodeFixProvider)
            Dim fixer As CodeFixProvider
            If language = LanguageNames.CSharp Then
                fixer = New CSharpAddImportCodeFixProvider()
            Else
                fixer = New VisualBasicAddImportCodeFixProvider()
            End If

            Return Tuple.Create(Of DiagnosticAnalyzer, CodeFixProvider)(Nothing, fixer)
        End Function

        <WorkItem(540515)>
        <WpfFact, Trait(Traits.Feature, Traits.Features.CodeActionsAddImport)>
        Public Sub Field_AcrossSubmission()
            Dim input =
<Workspace>
    <Submission Language="C#" CommonReferences="true">
        object {|Definition:$$foo|};
    </Submission>
    <Submission Language="C#" CommonReferences="true">
        [|foo|]
    </Submission>
</Workspace>

            Dim expected = "
using NS2;

public class Class1
{
    public void Foo()
    {
        var x = new Class2();
    }
}".Trim()

            Test(input, expected)
        End Sub
    End Class
End Namespace
