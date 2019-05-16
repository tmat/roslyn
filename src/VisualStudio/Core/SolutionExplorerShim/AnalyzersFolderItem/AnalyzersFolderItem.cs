// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem;
using Microsoft.VisualStudio.LanguageServices.SolutionExplorer;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VSLangProj140;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.SolutionExplorer
{
    internal partial class AnalyzersFolderItem : BaseItem
    {
        private readonly IVsHierarchy _projectHierarchy;
        private readonly IContextMenuController _contextMenuController;

        public IVsHierarchyItem ParentItem { get; }

        public AnalyzersFolderItem(
            IVsHierarchy projectHierarchy,
            IVsHierarchyItem parentItem,
            IContextMenuController contextMenuController)
            : base(SolutionExplorerShim.Analyzers)
        {
            _projectHierarchy = projectHierarchy;
            ParentItem = parentItem;
            _contextMenuController = contextMenuController;
        }

        public override ImageMoniker IconMoniker
            => KnownMonikers.CodeInformation;

        public override ImageMoniker ExpandedIconMoniker
            => KnownMonikers.CodeInformation;

        public override object GetBrowseObject()
            => new BrowseObject(this);

        public override IContextMenuController ContextMenuController
            => _contextMenuController;

        /// <summary>
        /// Get the DTE object for the Project.
        /// </summary>
        private VSProject3 GetVSProject()
            => _projectHierarchy.TryGetProject(out var project) ? project.Object as VSProject3 : null;

        /// <summary>
        /// Add an analyzer with the given path to this folder.
        /// </summary>
        public void AddAnalyzer(string path)
        {
            var vsproject = GetVSProject();
            if (vsproject == null)
            {
                return;
            }

            vsproject.AnalyzerReferences.Add(path);
        }

        /// <summary>
        /// Remove an analyzer with the given path from this folder.
        /// </summary>
        public void RemoveAnalyzer(string path)
        {
            var vsproject = GetVSProject();
            if (vsproject == null)
            {
                return;
            }

            vsproject.AnalyzerReferences.Remove(path);
        }
    }
}
