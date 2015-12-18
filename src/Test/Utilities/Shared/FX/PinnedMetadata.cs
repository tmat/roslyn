// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using Roslyn.Utilities;

namespace Roslyn.Test.Utilities
{
    internal sealed class PinnedMetadata : IDisposable
    {
        private GCHandle _bytes; // non-readonly as Free() mutates to prevent double-free.
        private Lazy<MetadataReader> _lazyReader;
        public readonly IntPtr Pointer;
        public readonly int Size;

        public unsafe PinnedMetadata(ImmutableArray<byte> metadata)
        {
            _bytes = GCHandle.Alloc(metadata.DangerousGetUnderlyingArray(), GCHandleType.Pinned);
            Pointer = _bytes.AddrOfPinnedObject();
            Size = metadata.Length;
            _lazyReader = new Lazy<MetadataReader>(() => new MetadataReader((byte*)Pointer, Size, MetadataReaderOptions.None, null));
        }

        public MetadataReader Reader => _lazyReader.Value;

        public void Dispose()
        {
            _bytes.Free();
        }
    }
}
