// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Contracts.EditAndContinue;

namespace Microsoft.CodeAnalysis.EditAndContinue
{
    internal readonly struct SolutionUpdate
    {
        public readonly ModuleUpdates ModuleUpdates;
        public readonly ImmutableArray<(Guid ModuleId, ImmutableArray<(ManagedModuleMethodId Method, NonRemappableRegion Region)>)> NonRemappableRegions;
        public readonly ImmutableArray<ProjectBaseline> ProjectBaselines;
        public readonly ImmutableArray<ProjectDiagnostics> Diagnostics;
        public readonly ImmutableArray<(DocumentId DocumentId, ImmutableArray<RudeEditDiagnostic> Diagnostics)> DocumentsWithRudeEdits;
        public readonly Diagnostic? SyntaxError;

        public SolutionUpdate(
            ModuleUpdates moduleUpdates,
            ImmutableArray<(Guid ModuleId, ImmutableArray<(ManagedModuleMethodId Method, NonRemappableRegion Region)>)> nonRemappableRegions,
            ImmutableArray<ProjectBaseline> projectBaselines,
            ImmutableArray<ProjectDiagnostics> diagnostics,
            ImmutableArray<(DocumentId DocumentId, ImmutableArray<RudeEditDiagnostic> Diagnostics)> documentsWithRudeEdits,
            Diagnostic? syntaxError)
        {
            ModuleUpdates = moduleUpdates;
            NonRemappableRegions = nonRemappableRegions;
            ProjectBaselines = projectBaselines;
            Diagnostics = diagnostics;
            DocumentsWithRudeEdits = documentsWithRudeEdits;
            SyntaxError = syntaxError;
        }

        public static SolutionUpdate Blocked(
            ImmutableArray<ProjectDiagnostics> diagnostics,
            ImmutableArray<(DocumentId, ImmutableArray<RudeEditDiagnostic>)> documentsWithRudeEdits,
            Diagnostic? syntaxError,
            bool hasEmitErrors)
            => new(
                new(syntaxError != null || hasEmitErrors ? ModuleUpdateStatus.Blocked : ModuleUpdateStatus.RestartRequired, ImmutableArray<ManagedHotReloadUpdate>.Empty),
                ImmutableArray<(Guid, ImmutableArray<(ManagedModuleMethodId, NonRemappableRegion)>)>.Empty,
                ImmutableArray<ProjectBaseline>.Empty,
                diagnostics,
                documentsWithRudeEdits,
                syntaxError);

        internal void Log(TraceLog log, UpdateId updateId)
        {
            log.Write("Solution update {0}.{1} status: {2}", updateId.SessionId.Ordinal, updateId.Ordinal, ModuleUpdates.Status);

            foreach (var moduleUpdate in ModuleUpdates.Updates)
            {
                log.Write("Module update {0}: capabilities=[{1}], types=[{2}], methods=[{3}]",
                    moduleUpdate.ModuleName,
                    moduleUpdate.RequiredCapabilities,
                    moduleUpdate.UpdatedTypes,
                    moduleUpdate.UpdatedMethods);
            }

            foreach (var projectDiagnostics in Diagnostics)
            {
                for (var i = 0; i < projectDiagnostics.Diagnostics.Length; i++)
                {
                    log.Write("Project {0} update diagnostic #{1}: {2}",
                        projectDiagnostics.ProjectId,
                        i,
                        projectDiagnostics.Diagnostics[i]);
                }
            }

            foreach (var documentWithRudeEdit in DocumentsWithRudeEdits)
            {
                for (var i = 0; i < documentWithRudeEdit.Diagnostics.Length; i++)
                {
                    log.Write("Updated document {0} with rude edit #{1}: {2}",
                        documentWithRudeEdit.DocumentId,
                        i,
                        documentWithRudeEdit.Diagnostics[i].Kind);
                }
            }
        }
    }
}
