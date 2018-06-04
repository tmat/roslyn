// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.EditAndContinue
{
    /// <summary>
    /// Represents a debugging session.
    /// </summary>
    internal sealed class DebuggingSession
    {
        public readonly Solution InitialSolution;

        internal readonly CancellationTokenSource Cancellation;

        /// <summary>
        /// Maintains a map of projects to the MVIDs of the corresponding compiled PE files.
        /// The map is populated on demand.
        /// The value is <c>default(Guid)</c> when the project hasn't been compiled at the time the debugging started.
        /// </summary>
        private readonly Dictionary<ProjectId, AsyncLazy<Guid>> _projectModuleIds;

        /// <summary>
        /// A set of projects initialized for Edit and Continue.
        /// Once a document that belongs to a project participating in EnC is changed the project initialization is triggered.
        /// </summary>
        private readonly HashSet<ProjectId> _projectsInitializedForEditAndContinue;

        /// <summary>
        /// Triggered whenever a change is detected in a document of a project participating in EnC.
        /// </summary>
        public event Action<ProjectId> InitializeProjectForEditAndContinue;

        internal DebuggingSession(Solution initialSolution)
        {
            Debug.Assert(initialSolution != null);
            InitialSolution = initialSolution;
            Cancellation = new CancellationTokenSource();
            _projectModuleIds = new Dictionary<ProjectId, AsyncLazy<Guid>>();
        }

        internal void OnDocumentChanged(Document document)
        {
            var initializer = InitializeProjectForEditAndContinue;
            if (initializer == null)
            {
                return;
            }

            var projectId = document.Project.Id;
            bool initializationRequired = false;
            
            lock (_projectsInitializedForEditAndContinue)
            {
                initializationRequired = _projectsInitializedForEditAndContinue.Add(projectId);
            }

            if (initializationRequired)
            {
                initializer(projectId);
            }
        }

        public Task<Guid> GetProjectModuleIdAsync(ProjectId projectId, CancellationToken cancellationToken)
        {
            AsyncLazy<Guid> lazyValue;

            lock (_projectModuleIds)
            {
                if (!_projectModuleIds.TryGetValue(projectId, out lazyValue))
                {
                    lazyValue = new AsyncLazy<Guid>(ct => ReadProjectModuleIdAsync(projectId, ct), cacheResult: true);
                }
            }

            return lazyValue.GetValueAsync(cancellationToken);
        }

        private Task<Guid> ReadProjectModuleIdAsync(ProjectId projectId, CancellationToken cancellationToken)
        {
            var outputFilePath = InitialSolution.GetProjectState(projectId).OutputFilePath;
            Debug.Assert(PathUtilities.IsAbsolute(outputFilePath));

            return Task.Factory.SafeStartNew(() => ReadMvid(outputFilePath), cancellationToken, TaskScheduler.Default);
        }

        private static Guid ReadMvid(string path)
        {
            if (!File.Exists(path))
            {
                return default;
            }

            try
            {
                using (var reader = new PEReader(FileUtilities.OpenRead(path)))
                {
                    var metadataReader = reader.GetMetadataReader();
                    return metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid);
                }
            }
            catch
            {
                // TODO: report error to the user when the MVID can't be read from an existing file.
                // The file might be corrupt or locked.
                return default;
            }
        }
    }
}
