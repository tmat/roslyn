// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.SolutionExplorer
{
    using Workspace = Microsoft.CodeAnalysis.Workspace;

    [Export(typeof(IAttachedCollectionSourceProvider))]
    [Name("AnalyzersFolderProvider")]
    [Order(Before = HierarchyItemsProviderNames.Contains)]
    internal class AnalyzersFolderItemProvider : AttachedCollectionSourceProvider<IVsHierarchyItem>
    {
        private readonly IAnalyzersCommandHandler _commandHandler;
        private readonly IServiceProvider _serviceProvider;

        [ImportingConstructor]
        public AnalyzersFolderItemProvider(
            [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider,
            [Import(typeof(AnalyzersCommandHandler))] IAnalyzersCommandHandler commandHandler)
        {
            _commandHandler = commandHandler;
            _serviceProvider = serviceProvider;
        }

        protected override IAttachedCollectionSource CreateCollectionSource(IVsHierarchyItem item, string relationshipName)
        {
            if (item != null &&
                item.HierarchyIdentity != null &&
                item.HierarchyIdentity.NestedHierarchy != null &&
                relationshipName == KnownRelationships.Contains)
            {
                var hierarchy = item.HierarchyIdentity.NestedHierarchy;
                var itemId = item.HierarchyIdentity.NestedItemID;

                var projectTreeCapabilities = GetProjectTreeCapabilities(hierarchy, itemId);
                if (projectTreeCapabilities.Any(c => c.Equals("References")))
                {
                    return CreateCollectionSourceCore(item.Parent, item);
                }
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private IAttachedCollectionSource CreateCollectionSourceCore(IVsHierarchyItem parentItem, IVsHierarchyItem item)
        {
            var componentModel = (IComponentModel)_serviceProvider.GetService(typeof(SComponentModel));
            var provider = componentModel.DefaultExportProvider.GetExportedValueOrDefault<ISolutionExplorerWorkspaceProvider>();
            var workspace = provider?.GetWorkspace() as VisualStudioWorkspace;
            if (workspace == null)
            {
                return null;
            }

            if (HierarchyItemToProjectIdMap.TryGetProjectId(workspace, _serviceProvider, parentItem, targetFrameworkMoniker: null, projectId: out var projectId))
            {
                var projectHierarchy = workspace.GetHierarchy(projectId);
                if (projectHierarchy != null)
                {
                    return new AnalyzersFolderItemSource(projectHierarchy, item, _commandHandler);
                }
            }

            return null;
        }

        private static ImmutableArray<string> GetProjectTreeCapabilities(IVsHierarchy hierarchy, uint itemId)
        {
            if (hierarchy.GetProperty(itemId, (int)__VSHPROPID7.VSHPROPID_ProjectTreeCapabilities, out var capabilitiesObj) == VSConstants.S_OK)
            {
                var capabilitiesString = (string)capabilitiesObj;
                return ImmutableArray.Create(capabilitiesString.Split(' '));
            }
            else
            {
                return ImmutableArray<string>.Empty;
            }
        }
    }
}
