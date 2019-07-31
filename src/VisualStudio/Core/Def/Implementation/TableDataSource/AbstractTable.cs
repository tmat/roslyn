// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Shell.TableManager;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    /// <summary>
    /// Base implementation of new platform table. this knows how to create various ITableDataSource and connect
    /// them to ITableManagerProvider
    /// </summary>
    internal abstract class AbstractTable
    {
        private readonly TableWorkspaceProtocol _workspace;
        internal ITableManager TableManager { get; }

        protected AbstractTable(TableWorkspaceProtocol workspace, ITableManagerProvider provider, string tableIdentifier)
        {
            _workspace = workspace;
            TableManager = provider.GetTableManager(tableIdentifier);
        }

        protected abstract void AddTableSourceIfNecessary(bool hasAnyProjects);
        protected abstract void RemoveTableSourceIfNecessary(bool hasAnyProjects);

        protected abstract void ShutdownSource();

        protected void ConnectWorkspaceEvents()
        {
            _workspace.AddTableSource += AddTableSourceIfNecessary;
            _workspace.RemoveTableSource += hasAnyProjects =>

            {
                ShutdownSourceIfNecessary(hasAnyProjects);
                RemoveTableSourceIfNecessary(hasAnyProjects);
            };

            _workspace.Connect();
        }

        protected bool WorkspaceHasAnyProjects()
            => !_workspace.IsCurrentSolutionEmpty;

        private void ShutdownSourceIfNecessary(bool hasAnyProjects)
        {
            if (hasAnyProjects)
            {
                return;
            }

            ShutdownSource();
        }

        protected void AddInitialTableSource(ITableDataSource source)
        {
            if (WorkspaceHasAnyProjects())
            {
                AddTableSource(source);
            }
        }

        protected void AddTableSource(ITableDataSource source)
        {
            TableManager.AddSource(source, Columns);
        }

        internal abstract IReadOnlyCollection<string> Columns { get; }
    }
}
