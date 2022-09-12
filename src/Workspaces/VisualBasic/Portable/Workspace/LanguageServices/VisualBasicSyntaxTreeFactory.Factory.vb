' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Composition
Imports System.IO
Imports System.Text
Imports System.Threading
Imports Microsoft.CodeAnalysis.Host
Imports Microsoft.CodeAnalysis.Host.Mef
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic
    Partial Friend Class VisualBasicSyntaxTreeFactory
        <ExportLanguageServiceFactory(GetType(ISyntaxTreeFactory), LanguageNames.VisualBasic), [Shared]>
        Partial Friend Class Factory
            Implements ILanguageServiceFactory

            <ImportingConstructor>
            <Obsolete(MefConstruction.ImportingConstructorMessage, True)>
            Public Sub New()
            End Sub

            Public Function CreateLanguageService(provider As HostLanguageServices) As ILanguageService Implements ILanguageServiceFactory.CreateLanguageService
                Return New VisualBasicSyntaxTreeFactory(provider.LanguageServices.SolutionServices)
            End Function
        End Class
    End Class
End Namespace
