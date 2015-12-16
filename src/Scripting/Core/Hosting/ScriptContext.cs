// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Scripting.Hosting
{
    public sealed class ScriptContext
    {
        private readonly Script _script;

        public ScriptContext(Script script)
        {
            _script = script;
        }

        public struct ReferencedLibrary
        {
            private readonly IAssemblySymbol _assembly;
            public ImmutableArray<string> Aliases { get; private set; }

            internal ReferencedLibrary(IAssemblySymbol assembly, ImmutableArray<string> aliases)
            {
                _assembly = assembly;
                Aliases = aliases;
            }

            public AssemblyIdentity Identity => _assembly.Identity;

            public override string ToString()
            {
                string aliasesStr = Aliases.IsEmpty ? "" : $" (aliased as {Aliases.Join(", ")})";
                return $"{Identity.Name}, Version={Identity.Version}{aliasesStr}";
            }
        }

        public IEnumerable<ReferencedLibrary> GetReferences()
        {
            foreach (var entry in _script.GetCompilation().GetBoundReferenceManager().GetReferencedAssemblyAliases())
            {
                yield return new ReferencedLibrary(entry.Item1, entry.Item2);
            }
        }
    }
}
