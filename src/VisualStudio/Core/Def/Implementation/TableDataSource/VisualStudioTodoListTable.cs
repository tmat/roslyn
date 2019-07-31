// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Host;
using Microsoft.VisualStudio.Shell.TableManager;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.TableDataSource
{
    [ExportEventListener(WellKnownEventListeners.TodoListProvider, WorkspaceKind.Host), Shared]
    internal sealed class VisualStudioTodoListTableWorkspaceEventListener : IEventListener<ITodoListProvider>
    {
        internal const string IdentifierString = nameof(VisualStudioTodoListTable);

        private readonly ITableManagerProvider _tableManagerProvider;

        [ImportingConstructor]
        public VisualStudioTodoListTableWorkspaceEventListener(ITableManagerProvider tableManagerProvider)
        {
            _tableManagerProvider = tableManagerProvider;
        }

        public void StartListening(TableWorkspaceProtocol workspace, ITodoListProvider service)
        {
            new VisualStudioTodoListTable(workspace, service, _tableManagerProvider);
        }

        internal sealed class VisualStudioTodoListTable : VisualStudioBaseTodoListTable
        {
            // internal for testing
            internal VisualStudioTodoListTable(TableWorkspaceProtocol workspace, ITodoListProvider todoListProvider, ITableManagerProvider provider) :
                base(workspace, todoListProvider, IdentifierString, provider)
            {
                ConnectWorkspaceEvents();
            }
        }
    }
}
