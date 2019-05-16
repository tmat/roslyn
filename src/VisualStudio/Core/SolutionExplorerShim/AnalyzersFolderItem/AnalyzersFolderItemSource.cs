// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.SolutionExplorer
{
    internal class AnalyzersFolderItemSource : IAttachedCollectionSource
    {
        private readonly IVsHierarchyItem _projectHierarchyItem;
        private readonly ObservableCollection<AnalyzersFolderItem> _folderItems;
        private readonly IAnalyzersCommandHandler _commandHandler;
        private readonly IVsHierarchy _projectHierarchy;

        public AnalyzersFolderItemSource(IVsHierarchy projectHierarchy, IVsHierarchyItem projectHierarchyItem, IAnalyzersCommandHandler commandHandler)
        {
            _projectHierarchy = projectHierarchy;
            _projectHierarchyItem = projectHierarchyItem;
            _commandHandler = commandHandler;

            _folderItems = new ObservableCollection<AnalyzersFolderItem>();

            Update();
        }

        public bool HasItems => true;
        public IEnumerable Items => _folderItems;
        public object SourceItem => _projectHierarchyItem;

        internal void Update()
        {
            // Don't create the item a 2nd time.
            if (_folderItems.Any())
            {
                return;
            }

            _folderItems.Add(
                new AnalyzersFolderItem(
                    _projectHierarchy,
                    _projectHierarchyItem,
                    _commandHandler.AnalyzerFolderContextMenuController));
        }
    }
}
