// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem;
using Microsoft.VisualStudio.LanguageServices.Implementation.Venus;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.VisualStudio.LanguageServices.Implementation
{
    internal static class HierarchyItemToProjectIdMap
    {
        /// <summary>
        /// Given an <see cref="IVsHierarchyItem"/> representing a project and an optional target framework moniker,
        /// returns the <see cref="ProjectId"/> of the equivalent Roslyn <see cref="Project"/>.
        /// </summary>
        /// <param name="hierarchyItem">An <see cref="IVsHierarchyItem"/> for the project root.</param>
        /// <param name="targetFrameworkMoniker">An optional string representing a TargetFrameworkMoniker.
        /// This is only useful in multi-targeting scenarios where there may be multiple Roslyn projects 
        /// (one per target framework) for a single project on disk.</param>
        /// <param name="projectId">The <see cref="ProjectId"/> of the found project, if any.</param>
        /// <returns>True if the desired project was found; false otherwise.</returns>
        public static bool TryGetProjectId(VisualStudioWorkspace workspace, IServiceProvider serviceProvider, IVsHierarchyItem hierarchyItem, string targetFrameworkMoniker, out ProjectId projectId)
        {
            // A project node is represented in two different hierarchies: the solution's IVsHierarchy (where it is a leaf node)
            // and the project's own IVsHierarchy (where it is the root node). The IVsHierarchyItem joins them together for the
            // purpose of creating the tree displayed in Solution Explorer. The project's hierarchy is what is passed from the
            // project system to the language service, so that's the one the one to query here. To do that we need to get
            // the "nested" hierarchy from the IVsHierarchyItem.
            var nestedHierarchy = hierarchyItem.HierarchyIdentity.NestedHierarchy;
            var nestedHierarchyId = hierarchyItem.HierarchyIdentity.NestedItemID;

            if (!nestedHierarchy.TryGetCanonicalName(nestedHierarchyId, out string nestedCanonicalName)
                || !nestedHierarchy.TryGetItemName(nestedHierarchyId, out string nestedName))
            {
                projectId = null;
                return false;
            }

            // First filter the projects by matching up properties on the input hierarchy against properties on each
            // project's hierarchy.
            var candidateProjects = workspace.CurrentSolution.Projects
                .Where(p =>
                {
                    // We're about to access various properties of the IVsHierarchy associated with the project.
                    // The properties supported and the interpretation of their values varies from one project system
                    // to another. This code is designed with C# and VB in mind, so we need to filter out everything
                    // else.
                    if (p.Language != LanguageNames.CSharp && p.Language != LanguageNames.VisualBasic)
                    {
                        return false;
                    }

                    if (!workspace.TryGetProjectGuid(p.Id, out var projectGuid))
                    {
                        return false;
                    }

                    // Here we try to match the hierarchy from Solution Explorer to a hierarchy from the Roslyn project.
                    // The canonical name of a hierarchy item must be unique _within_ an hierarchy, but since we're
                    // examining multiple hierarchies the canonical name could be the same. Indeed this happens when two
                    // project files are in the same folder--they both use the full path to the _folder_ as the canonical
                    // name. To distinguish them we also examine the "regular" name, which will necessarily be different
                    // if the two projects are in the same folder.
                    // Note that if a project has been loaded with Lightweight Solution Load it won't even have a
                    // hierarchy, so we need to check for null first.
                    var vsSolution = (IVsSolution)serviceProvider.GetService(typeof(IVsSolution));

                    if (ErrorHandler.Succeeded(vsSolution.GetProjectOfGuid(projectGuid, out var hierarchy))
                        && hierarchy.TryGetCanonicalName((uint)VSConstants.VSITEMID.Root, out string projectCanonicalName)
                        && hierarchy.TryGetItemName((uint)VSConstants.VSITEMID.Root, out string projectName)
                        && projectCanonicalName.Equals(nestedCanonicalName, StringComparison.OrdinalIgnoreCase)
                        && projectName.Equals(nestedName))
                    {
                        if (targetFrameworkMoniker == null)
                        {
                            return true;
                        }

                        return hierarchy.TryGetTargetFrameworkMoniker((uint)VSConstants.VSITEMID.Root, out string projectTargetFrameworkMoniker)
                            && projectTargetFrameworkMoniker.Equals(targetFrameworkMoniker);
                    }

                    return false;
                })
                .ToArray();

            // If we only have one candidate then no further checks are required.
            if (candidateProjects.Length == 1)
            {
                projectId = candidateProjects[0].Id;
                return true;
            }

            // If we have multiple candidates then we might be dealing with Web Application Projects. In this case
            // there will be one main project plus one project for each open aspx/cshtml/vbhtml file, all with
            // identical properties on their hierarchies. We can find the main project by taking the first project
            // without a ContainedDocument.
            foreach (var candidateProject in candidateProjects)
            {
                if (!candidateProject.DocumentIds.Any(id => ContainedDocument.TryGetContainedDocument(id) != null))
                {
                    projectId = candidateProject.Id;
                    return true;
                }
            }

            projectId = null;
            return false;
        }
    }
}
