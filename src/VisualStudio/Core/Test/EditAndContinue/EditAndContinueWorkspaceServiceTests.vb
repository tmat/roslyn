' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Diagnostics
Imports Microsoft.CodeAnalysis.EditAndContinue
Imports Microsoft.CodeAnalysis.Test.Utilities

Namespace Microsoft.VisualStudio.LanguageServices.UnitTests.EditAndContinue
    <[UseExportProvider]>
    Public Class EditAndContinueWorkspaceServiceTests
        <Fact>
        Public Sub ReadOnlyDocumentTest()
            Dim diagnosticService As IDiagnosticAnalyzerService = New EditAndContinueTestHelper.TestDiagnosticAnalyzerService()
            Dim encService As IEditAndContinueService = New EditAndContinueService(diagnosticService, New TestActiveStatementProvider())
            Dim workspace = EditAndContinueTestHelper.CreateTestWorkspace()
            Dim currentSolution = workspace.CurrentSolution
            Dim project = currentSolution.Projects(0)

            Dim sessionReason As SessionReadOnlyReason
            Dim projectReason As ProjectReadOnlyReason
            Dim isReadOnly As Boolean

            ' not yet start
            isReadOnly = encService.IsProjectReadOnly(project.Id, sessionReason, projectReason)

            Assert.Equal(ProjectReadOnlyReason.None, projectReason)
            Assert.Equal(SessionReadOnlyReason.None, sessionReason)
            Assert.Equal(False, isReadOnly)

            ' run mode 
            encService.StartDebuggingSession(workspace.CurrentSolution)
            isReadOnly = encService.IsProjectReadOnly(project.Id, sessionReason, projectReason)

            Assert.Equal(ProjectReadOnlyReason.None, projectReason)
            Assert.Equal(SessionReadOnlyReason.Running, sessionReason)
            Assert.Equal(True, isReadOnly)

            encService.StartEditSession(currentSolution, stoppedAtException:=False)
            isReadOnly = encService.IsProjectReadOnly(project.Id, sessionReason, projectReason)
            Assert.Equal(ProjectReadOnlyReason.None, projectReason)
            Assert.Equal(SessionReadOnlyReason.None, sessionReason)
            Assert.Equal(False, isReadOnly)

            ' end edit session
            encService.EndEditSession(ImmutableDictionary(Of ActiveMethodId, ImmutableArray(Of NonRemappableRegion)).Empty)
            isReadOnly = encService.IsProjectReadOnly(project.Id, sessionReason, projectReason)
            Assert.Equal(ProjectReadOnlyReason.None, projectReason)
            Assert.Equal(SessionReadOnlyReason.Running, sessionReason)
            Assert.Equal(True, isReadOnly)

            ' break mode and stop at exception
            encService.StartEditSession(currentSolution, stoppedAtException:=True)
            isReadOnly = encService.IsProjectReadOnly(project.Id, sessionReason, projectReason)
            Assert.Equal(ProjectReadOnlyReason.None, projectReason)
            Assert.Equal(SessionReadOnlyReason.StoppedAtException, sessionReason)
            Assert.Equal(True, isReadOnly)
        End Sub
    End Class

End Namespace
