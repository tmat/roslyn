// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Microsoft.DiaSymReader;
using Roslyn.Test.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.ExpressionEvaluator
{
    internal sealed class RuntimeInstance : IDisposable
    {
        internal RuntimeInstance(ImmutableArray<ModuleInstance> modules)
        {
            this.Modules = modules;
        }

        internal readonly ImmutableArray<ModuleInstance> Modules;

        void IDisposable.Dispose()
        {
            foreach (var module in this.Modules)
            {
                module.Dispose();
            }
        }
    }

    internal sealed class ModuleInstance : IDisposable
    {
        internal readonly MetadataReference MetadataReference;
        internal readonly ModuleMetadata ModuleMetadata;
        internal readonly Guid ModuleVersionId;
        internal readonly object SymReader;
        private readonly bool _includeLocalSignatures;
        private bool _disposed;

        internal ModuleInstance(
            MetadataReference metadataReference,
            ModuleMetadata moduleMetadata,
            Guid moduleVersionId,
            object symReader,
            bool includeLocalSignatures)
        {
            this.MetadataReference = metadataReference;
            this.ModuleMetadata = moduleMetadata;
            this.SymReader = symReader; // should be non-null if and only if there are symbols
            _includeLocalSignatures = includeLocalSignatures;
            ModuleVersionId = moduleVersionId;
        }

        internal MetadataBlock MetadataBlock
        {
            get
            {
                IntPtr pointer;
                int size;
                ModuleMetadata.Module.GetMetadataMemoryBlock(out pointer, out size);
                return new MetadataBlock(ModuleVersionId, Guid.Empty, pointer, size);
            }
        }

        internal MetadataReader MetadataReader => ModuleMetadata.MetadataReader;

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Free();
        }

        private void Free()
        {
            if (!_disposed)
            {
                ((ISymUnmanagedDispose)SymReader)?.Destroy();
                _disposed = true;
            }
        }

        ~ModuleInstance()
        {
            Free();
        }

        unsafe internal int GetLocalSignatureToken(MethodDefinitionHandle methodHandle)
        {
            if (!_includeLocalSignatures)
            {
                return 0;
            }

            var peReader = ModuleMetadata.Module.PEReaderOpt;
            Debug.Assert(peReader != null);

            var body = peReader.GetMethodBody(ModuleMetadata.MetadataReader.GetMethodDefinition(methodHandle).RelativeVirtualAddress);
            return MetadataTokens.GetToken(body.LocalSignature);
        }
    }
}
