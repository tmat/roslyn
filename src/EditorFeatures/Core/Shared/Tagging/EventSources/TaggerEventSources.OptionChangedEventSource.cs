// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.Editor.Shared.Tagging;

internal partial class TaggerEventSources
{
    private sealed class GlobalOptionChangedEventSource(IGlobalOptionService globalOptions, Func<IOption2, bool> predicate) : AbstractTaggerEventSource
    {
        private readonly Func<IOption2, bool> _predicate = predicate;

        public override void Connect()
        {
            globalOptions.AddOptionChangedHandler(WeakEventHandler<OptionChangedEventArgs>.Create(this, OnGlobalOptionChanged));
        }

        public override void Disconnect()
        {
            globalOptions.RemoveOptionChangedHandler(WeakEventHandler<OptionChangedEventArgs>.Create(this, OnGlobalOptionChanged));
        }

        private static void OnGlobalOptionChanged(GlobalOptionChangedEventSource self, OptionChangedEventArgs e)
        {
            if (e.HasOption(self._predicate))
            {
                self.RaiseChanged();
            }
        }
    }
}
