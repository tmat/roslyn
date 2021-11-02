' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports Microsoft.CodeAnalysis.Formatting
Imports Microsoft.CodeAnalysis.CodeStyle
Imports Microsoft.CodeAnalysis.Diagnostics
Imports Microsoft.CodeAnalysis.Editing

Namespace Microsoft.CodeAnalysis.VisualBasic.Formatting
    Friend NotInheritable Class VisualBasicFormatterOptions
        Inherits FormatterOptions

        Public Sub New(
            AllowDisjointSpanMerging As Boolean,
            AutoFormattingOnReturn As Boolean,
            AutoFormattingOnTyping As Boolean,
            AutoFormattingOnCloseBrace As Boolean,
            IndentStyle As FormattingOptions.IndentStyle,
            NewLine As String,
            IndentationSize As Integer,
            TabSize As Integer,
            UseTabs As Boolean,
            SeparateImportDirectiveGroups As Boolean,
            OperatorPlacementWhenWrapping As OperatorPlacementWhenWrappingPreference,
            PreferredWrappingColumn As Integer)
            MyBase.New(
                AllowDisjointSpanMerging,
                AutoFormattingOnReturn,
                AutoFormattingOnTyping,
                AutoFormattingOnCloseBrace,
                IndentStyle,
                NewLine,
                IndentationSize,
                TabSize,
                UseTabs,
                SeparateImportDirectiveGroups,
                OperatorPlacementWhenWrapping,
                PreferredWrappingColumn)
        End Sub

        Public Overloads Shared Function From(options As AnalyzerConfigOptions) As VisualBasicFormatterOptions
#If CODE_STYLE Then
            ' Unused in code-style layer
            Dim allowDisjointSpanMerging = False
            Dim autoFormattingOnReturn = False
            Dim autoFormattingOnTyping = False
            Dim autoFormattingOnCloseBrace = False
            Dim indentStyle = FormattingOptions.IndentStyle.None
#Else
            Dim allowDisjointSpanMerging = options.GetOption(FormattingBehaviorOptions.AllowDisjointSpanMerging)
            Dim autoFormattingOnReturn = options.GetOption(FormattingBehaviorOptions.AutoFormattingOnReturn)
            Dim autoFormattingOnTyping = options.GetOption(FormattingBehaviorOptions.AutoFormattingOnTyping)
            Dim autoFormattingOnCloseBrace = options.GetOption(FormattingBehaviorOptions.AutoFormattingOnCloseBrace)
            Dim indentStyle = options.GetOption(FormattingBehaviorOptions.SmartIndent)
#End If
            Return New VisualBasicFormatterOptions(
                AllowDisjointSpanMerging:=allowDisjointSpanMerging,
                AutoFormattingOnReturn:=autoFormattingOnReturn,
                AutoFormattingOnTyping:=autoFormattingOnTyping,
                AutoFormattingOnCloseBrace:=autoFormattingOnCloseBrace,
                IndentStyle:=indentStyle,
                NewLine:=options.GetOption(FormattingOptions2.NewLine),
                IndentationSize:=options.GetOption(FormattingOptions2.IndentationSize),
                TabSize:=options.GetOption(FormattingOptions2.TabSize),
                UseTabs:=options.GetOption(FormattingOptions2.UseTabs),
                SeparateImportDirectiveGroups:=options.GetOption(GenerationOptions.SeparateImportDirectiveGroups),
                OperatorPlacementWhenWrapping:=options.GetOption(CodeStyleOptions2.OperatorPlacementWhenWrapping),
                PreferredWrappingColumn:=options.GetOption(FormattingOptions2.PreferredWrappingColumn))
        End Function
    End Class
End Namespace
