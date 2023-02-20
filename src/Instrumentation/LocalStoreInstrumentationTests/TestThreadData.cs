// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.CodeAnalysis.Runtime.UnitTests;

internal class TestThreadData : LocalStoreTracker.ThreadData
{
    public TestThreadData(
        int bufferSize = 4 * 1024,
        Action<int, nint, int, int>? bufferFullImpl = null)
        : base(bufferSize)
    {
        BufferFullImpl = bufferFullImpl;
    }

    public Action<int, nint, int, int>? BufferFullImpl;

    protected override unsafe void BufferFull(int managedThreadId, byte* buffer, int dataSize, int protocolVersion)
        => BufferFullImpl?.Invoke(managedThreadId, (nint)buffer, dataSize, protocolVersion);
}
