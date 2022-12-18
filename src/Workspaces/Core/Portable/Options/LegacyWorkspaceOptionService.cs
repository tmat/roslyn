// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using Microsoft.CodeAnalysis.Host.Mef;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Options;

[ExportWorkspaceService(typeof(ILegacyWorkspaceOptionService)), Shared]
internal sealed class LegacyWorkspaceOptionService : ILegacyWorkspaceOptionService
{
    public IGlobalOptionService GlobalOptions { get; }

    // access is interlocked
    private ImmutableArray<Workspace> _registeredWorkspaces;

    /// <summary>
    /// Stores options that are not defined by Roslyn and do not implement <see cref="IOption2"/>.
    /// </summary>
    private ImmutableDictionary<OptionKey, object?> _currentExternallyDefinedOptionValues;

    [ImportingConstructor]
    [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
    public LegacyWorkspaceOptionService(IGlobalOptionService globalOptionService)
    {
        GlobalOptions = globalOptionService;
        _registeredWorkspaces = ImmutableArray<Workspace>.Empty;
        _currentExternallyDefinedOptionValues = ImmutableDictionary.Create<OptionKey, object?>();
    }

    public object? GetOption(OptionKey key)
    {
        if (key.Option is IOption2 internallyDefinedOption)
        {
            return GlobalOptions.GetOption<object?>(new OptionKey2(internallyDefinedOption, key.Language));
        }

        if (_currentExternallyDefinedOptionValues.TryGetValue(key, out var value))
        {
            return value;
        }

        return key.Option.DefaultValue;
    }

    /// <summary>
    /// Sets values of options that may be stored in <see cref="Solution.Options"/> (public options).
    /// Clears <see cref="SolutionOptionSet"/> of registered workspaces so that next time
    /// <see cref="Solution.Options"/> are queried for the options new values are fetched from 
    /// <see cref="GlobalOptionService"/>.
    /// </summary>
    public void SetOptions(
        ImmutableArray<KeyValuePair<OptionKey2, object?>> internallyDefinedOptions,
        ImmutableArray<KeyValuePair<OptionKey, object?>> externallyDefinedOptions)
    {
        var anyOptionChanged = GlobalOptions.SetGlobalOptions(internallyDefinedOptions);

        foreach (var (optionKey, value) in externallyDefinedOptions)
        {
            var existingValue = GetOption(optionKey);
            if (Equals(value, existingValue))
            {
                continue;
            }

            anyOptionChanged = true;
            ImmutableInterlocked.GetOrAdd(ref _currentExternallyDefinedOptionValues, optionKey, value);
        }

        if (anyOptionChanged)
        {
            UpdateRegisteredWorkspaces();
        }
    }

    private void UpdateRegisteredWorkspaces()
    {
        // Ensure that the Workspace's CurrentSolution snapshot is updated with new options for all registered workspaces
        // prior to raising option changed event handlers.
        foreach (var workspace in _registeredWorkspaces)
        {
            workspace.UpdateCurrentSolutionOnOptionsChanged();
        }
    }

    public void RegisterWorkspace(Workspace workspace)
        => ImmutableInterlocked.Update(ref _registeredWorkspaces, (workspaces, workspace) => workspaces.Add(workspace), workspace);

    public void UnregisterWorkspace(Workspace workspace)
        => ImmutableInterlocked.Update(ref _registeredWorkspaces, (workspaces, workspace) => workspaces.Remove(workspace), workspace);

}
