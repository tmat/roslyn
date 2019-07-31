// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.SolutionCrawler;
using Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    internal abstract class TableWorkspaceProtocol
    {
        [Export(typeof(Primary))]
        internal sealed class Primary : TableWorkspaceProtocol
        {
            [ImportingConstructor]
            public Primary(VisualStudioWorkspace workspace)
                : base(workspace)
            {
            }
        }

        [Export(typeof(MiscellaneousFiles))]
        internal sealed class MiscellaneousFiles : TableWorkspaceProtocol
        {
            [ImportingConstructor]
            public MiscellaneousFiles(MiscellaneousFilesWorkspace workspace)
                : base(workspace)
            {
            }
        }

        private readonly Workspace _workspace;

        public event Action<bool> AddTableSource;
        public event Action<bool> RemoveTableSource;
        public event Action<bool> SolutionCrawlerProgressChanged;

        public TableWorkspaceProtocol(Workspace workspace)
        {
            Contract.ThrowIfNull(workspace);
            _workspace = workspace;
        }

        public void Connect()
        {
            _workspace.WorkspaceChanged += OnWorkspaceChanged;
        }

        private void OnWorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            switch (e.Kind)
            {
                case WorkspaceChangeKind.SolutionAdded:
                case WorkspaceChangeKind.ProjectAdded:
                    AddTableSource(e.NewSolution.ProjectIds.Count == 0);
                    break;

                case WorkspaceChangeKind.SolutionRemoved:
                case WorkspaceChangeKind.ProjectRemoved:
                    RemoveTableSource(e.NewSolution.ProjectIds.Count == 0);
                    break;

                case WorkspaceChangeKind.SolutionChanged:
                case WorkspaceChangeKind.SolutionCleared:
                case WorkspaceChangeKind.SolutionReloaded:
                case WorkspaceChangeKind.ProjectChanged:
                case WorkspaceChangeKind.ProjectReloaded:
                case WorkspaceChangeKind.DocumentAdded:
                case WorkspaceChangeKind.DocumentRemoved:
                case WorkspaceChangeKind.DocumentReloaded:
                case WorkspaceChangeKind.DocumentChanged:
                case WorkspaceChangeKind.AdditionalDocumentAdded:
                case WorkspaceChangeKind.AdditionalDocumentRemoved:
                case WorkspaceChangeKind.AdditionalDocumentReloaded:
                case WorkspaceChangeKind.AdditionalDocumentChanged:
                case WorkspaceChangeKind.AnalyzerConfigDocumentAdded:
                case WorkspaceChangeKind.AnalyzerConfigDocumentRemoved:
                case WorkspaceChangeKind.AnalyzerConfigDocumentChanged:
                case WorkspaceChangeKind.AnalyzerConfigDocumentReloaded:
                    break;
                default:
                    Contract.Fail("Can't reach here");
                    return;
            }
        }

        internal bool IsCurrentSolutionEmpty => _workspace.CurrentSolution.ProjectIds.Count == 0;

        public void ConnectToSolutionCrawlerService()
        {
            var handler = SolutionCrawlerProgressChanged;

            Contract.ThrowIfNull(handler);

            var crawlerService = _workspace.Services.GetService<ISolutionCrawlerService>();
            if (crawlerService == null)
            {
                // can happen depends on host such as testing host.
                return;
            }

            var reporter = crawlerService.GetProgressReporter(_workspace);
            reporter.ProgressChanged += OnSolutionCrawlerProgressChanged;

            // set initial value
            handler(reporter.InProgress);
        }

        private void OnSolutionCrawlerProgressChanged(object sender, ProgressData progressData)
        {
            switch (progressData.Status)
            {
                case ProgressStatus.Started:
                    SolutionCrawlerProgressChanged?.Invoke(true);
                    break;
                case ProgressStatus.Stoped:
                    SolutionCrawlerProgressChanged?.Invoke(false);
                    break;
            }
        }
    }
}
