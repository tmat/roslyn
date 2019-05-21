// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Composition;
using System.Threading;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis
{
    [Export(typeof(PrimaryWorkspace)), Shared]
    internal sealed class PrimaryWorkspace
    {
        private volatile Workspace _primaryWorkspace;

        /// <summary>
        /// The primary workspace, usually set by the host environment.
        /// Only one workspace can be the primary.
        /// </summary>
        public Workspace Workspace
        {
            get => _primaryWorkspace;
            set
            {
                Contract.ThrowIfTrue(Interlocked.CompareExchange(ref _primaryWorkspace, value, null) != null);
            }
        }
    }
}
