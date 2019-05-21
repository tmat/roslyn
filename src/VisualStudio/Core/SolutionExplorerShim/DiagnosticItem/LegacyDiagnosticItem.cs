// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Internal.VisualStudio.PlatformUI;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.SolutionExplorer
{
    internal sealed partial class LegacyDiagnosticItem : BaseDiagnosticItem
    {
        private readonly AnalyzerItem _analyzerItem;
        private readonly IContextMenuController _contextMenuController;

        public LegacyDiagnosticItem(AnalyzerItem analyzerItem, DiagnosticDescriptor descriptor, ReportDiagnostic effectiveSeverity, IContextMenuController contextMenuController, CultureInfo culture)
            : base(descriptor, effectiveSeverity, culture)
        {
            _analyzerItem = analyzerItem;

            _contextMenuController = contextMenuController;
        }

        protected override Workspace Workspace
        {
            get { return _analyzerItem.AnalyzersFolder.Workspace; }
        }

        public override ProjectId ProjectId
        {
            get { return _analyzerItem.AnalyzersFolder.ProjectId; }
        }

        protected override AnalyzerReference AnalyzerReference
        {
            get { return _analyzerItem.AnalyzerReference; }
        }

        public override IContextMenuController ContextMenuController
        {
            get
            {
                return _contextMenuController;
            }
        }
    }
}
