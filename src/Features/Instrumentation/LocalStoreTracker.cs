// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// The following code is not meant to be debuggable.
#line hidden

// Emit no annotations
#nullable disable

using System;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.CodeAnalysis.Runtime;

public unsafe readonly struct LocalStoreTracker
{
    private const int ProtocolVersion = 1;
    private const int MaxCompressedIntegerSize = 4;
    private const int MaxStringLength = 16;

    private enum RecordKind : byte
    {
        /// <summary>
        /// Indicates end of data blob in the buffer.
        /// </summary>
        EndOfData = 0,

        MethodEntry,
        MethodEntryWithAddresses,

        LocalAddressStore,
        LocalUnmanagedStore,
        ParameterAddressStore,
        ParameterUnmanagedStore,

        UntypedLocalStore_Base,
        UntypedParameterStore_Base = UntypedLocalStore_Base + TypeCode.Count,

        TypedParameterStore = UntypedParameterStore_Base + TypeCode.Count,
        TypedLocalStore_Base, // + local index
    }

    private enum TypeCode : byte
    {
        Null = 0,
        Boolean = 1,
        Char = 2,
        SByte = 3,
        Byte = 4,
        Int16 = 5,
        UInt16 = 6,
        Int32 = 7,
        UInt32 = 8,
        Int64 = 9,
        UInt64 = 10,
        Single = 11,
        Double = 12,
        String = 13,
        Object = 14,
        Count
    }

    private sealed class ThreadData
    {
        // TODO: values 
        private const int BufferSize = 100;

        /// <summary>
        /// Managed id of the thread this buffer belongs to.
        /// </summary>
        private readonly int _threadId;

        /// <summary>
        /// A circular buffer allocated using native memory allocator and zero-initialized.
        /// After the data is exfiltrated it is also zeroed out.
        /// 
        /// This allows the consumer to determine where the data end at any point in time (provided that the thread writing the data is suspended),
        /// not just when the buffer is full. If the thread is suspended at an arbitrary point the last record in the buffer might be partially written
        /// and thus the consumer must ignore it.
        /// 
        /// The buffer always starts with a compressed integer indicating <see cref="RecordKind"/> of the first record in the buffer.
        /// A record never wraps around the buffer end. If the entire record that is about to be written doesn't fit the buffer is cleared and the record
        /// is written at the buffer start.
        /// </summary>
        private readonly byte* _buffer;

        /// <summary>
        /// Points at the byte immediately following the last allocated buffer byte.
        /// </summary>
        private readonly byte* _bufferEnd;

        /// <summary>
        /// Current position where data are being written.
        /// </summary>
        private byte* _position;

        public ThreadData()
        {
            _buffer = (byte*)NativeMemory.Alloc(BufferSize);
            GC.AddMemoryPressure(BufferSize);

            _position = _buffer;

            // reserve one byte at the end of the buffer to indicate end of data
            _bufferEnd = _buffer + BufferSize - 1;

            _threadId = Thread.CurrentThread.ManagedThreadId;

            var span = new Span<byte>(_buffer, BufferSize);
            span.Clear();
            BufferCreated(_threadId, span, ProtocolVersion);
        }

        ~ThreadData()
        {
            NativeMemory.Free(_buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteRecordHeader(RecordKind kind, int payloadMaxSize)
        {
            var position = _position;
            if (position > _bufferEnd - payloadMaxSize - sizeof(byte))
            {
                position = Reset();
            }

            *position = (byte)kind;
            _position = position + sizeof(byte);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteRecordHeader(int kind, int payloadMaxSize)
        {
            if (_position > _bufferEnd - payloadMaxSize - MaxCompressedIntegerSize)
            {
                Reset();
            }

            WriteCompressedInteger(kind);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public byte* Reset()
        {
            var span = new Span<byte>(_buffer, (int)(long)(_position - _buffer));
            BufferFull(_threadId, span, ProtocolVersion);
            span.Clear();
            return _position = _buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* Advance(int size)
        {
            var position = _position;
            _position = position + size;
            return position;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
            => *Advance(sizeof(byte)) = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt16(ushort value)
            => *(ushort*)Advance(sizeof(ushort)) = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32(uint value)
            => *(uint*)Advance(sizeof(uint)) = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt64(ulong value)
            => *(ulong*)Advance(sizeof(ulong)) = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteAddress(void* value)
            => *(void**)Advance(sizeof(void*)) = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteMemory(void* ptr, int size)
            => Buffer.MemoryCopy(ptr, Advance(size), size, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteCompressedInteger(int value)
        {
            unchecked
            {
                if (value <= 0x7f)
                {
                    WriteByte((byte)value);
                }
                else if (value <= 0x3fff)
                {
                    WriteUInt16((ushort)(0x8000 | value));
                }
                else // if (value <= MaxCompressedIntegerValue)
                {
                    WriteUInt32(0xc0000000 | (uint)value);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteCompressedSignedInteger(int value)
        {
            unchecked
            {
                const int b6 = (1 << 6) - 1;
                const int b13 = (1 << 13) - 1;
                const int b28 = (1 << 28) - 1;

                // 0xffffffff for negative value
                // 0x00000000 for non-negative
                int signMask = value >> 31;

                if ((value & ~b6) == (signMask & ~b6))
                {
                    int n = ((value & b6) << 1) | (signMask & 1);
                    WriteByte((byte)n);
                }
                else if ((value & ~b13) == (signMask & ~b13))
                {
                    int n = ((value & b13) << 1) | (signMask & 1);
                    WriteUInt16((ushort)(0x8000 | n));
                }
                else // if ((value & ~b28) == (signMask & ~b28))
                {
                    int n = ((value & b28) << 1) | (signMask & 1);
                    WriteUInt32(0xc0000000 | (uint)n);
                }
            }
        }
    }

    [ThreadStatic]
    private static ThreadData s_threadData;

    private readonly ThreadData _data;
    private readonly int _methodId;

    private LocalStoreTracker(ThreadData data, int methodId)
    {
        _data = data;
        _methodId = methodId;
    }

    /// <summary>
    /// Notifies data consumer of a new thread data buffer.
    /// The consumer places a tracepoint into this method and read its parameters.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void BufferCreated(int managedThreadId, Span<byte> span, int protocolVersion)
    {
    }

    /// <summary>
    /// Notifies data consumer of a full buffer.
    /// The consumer places a tracepoint into this method and read its parameters.
    /// </summary>
    /// <remarks>
    /// The last record might be partial and may be continued at the beginning of the buffer. (!)
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void BufferFull(int managedThreadId, Span<byte> span, int protocolVersion)
    {
        // Exfiltrate buffer data by placing a tracepoint here
    }

    // method with no local or parameter address taken
    public static LocalStoreTracker LogMethodEntry(int methodId)
    {
        var data = s_threadData ??= new ThreadData();
        data.WriteRecordHeader(RecordKind.MethodEntry, payloadMaxSize: MaxCompressedIntegerSize);
        data.WriteCompressedInteger(methodId);
        return new(data, methodId);
    }

    // followed by local and parameter addresses
    public static LocalStoreTracker LogMethodEntry(int methodId, int addressCount)
    {
        var data = s_threadData ??= new ThreadData();

        data.WriteRecordHeader(RecordKind.MethodEntryWithAddresses, payloadMaxSize: MaxCompressedIntegerSize + addressCount * (MaxCompressedIntegerSize + sizeof(void*)));
        data.WriteCompressedInteger(methodId);

        return new(data, methodId);
    }

    public void LogLocalLoadAddress<T>(ref T local, ushort localIndex)
    {
        _data.WriteCompressedSignedInteger(localIndex);
        _data.WriteAddress(Unsafe.AsPointer(ref local));
    }

    public void LogParameterLoadAddress<T>(ref T parameter, ushort parameterIndex)
    {
        _data.WriteCompressedSignedInteger(-parameterIndex - 1);
        _data.WriteAddress(Unsafe.AsPointer(ref parameter));
    }

    public void LogLocalStore(bool value, ushort localIndex)
    {
        _data.WriteRecordHeader((int)RecordKind.TypedLocalStore_Base + localIndex, payloadMaxSize: sizeof(byte) + MaxCompressedIntegerSize);
        _data.WriteByte(value ? (byte)1 : (byte)0);
        _data.WriteCompressedInteger(_methodId);
    }

    public void LogLocalStore(byte value, ushort localIndex)
    {
        _data.WriteRecordHeader((int)RecordKind.TypedLocalStore_Base + localIndex, payloadMaxSize: sizeof(byte) + MaxCompressedIntegerSize);
        _data.WriteByte(value);
        _data.WriteCompressedInteger(_methodId);
    }

    public void LogLocalStore(ushort value, ushort localIndex)
    {
        _data.WriteRecordHeader((int)RecordKind.TypedLocalStore_Base + localIndex, payloadMaxSize: sizeof(ushort) + MaxCompressedIntegerSize);
        _data.WriteUInt16(value);
        _data.WriteCompressedInteger(_methodId);
    }

    public void LogLocalStore(uint value, ushort localIndex)
    {
        _data.WriteRecordHeader((int)RecordKind.TypedLocalStore_Base + localIndex, payloadMaxSize: sizeof(uint) + MaxCompressedIntegerSize);
        _data.WriteUInt32(value);
        _data.WriteCompressedInteger(_methodId);
    }

    public void LogLocalStore(ulong value, ushort localIndex)
    {
        _data.WriteRecordHeader((int)RecordKind.TypedLocalStore_Base + localIndex, payloadMaxSize: sizeof(ulong) + MaxCompressedIntegerSize);
        _data.WriteUInt64(value);
        _data.WriteCompressedInteger(_methodId);
    }

    public void LogLocalStore(string value, ushort localIndex)
    {
        WriteStringValueStore((int)RecordKind.TypedLocalStore_Base + localIndex, value);
        _data.WriteCompressedInteger(_methodId);
    }

    public void LogParameterStore(bool value, ushort parameterIndex)
    {
        _data.WriteRecordHeader(RecordKind.TypedParameterStore, payloadMaxSize: sizeof(byte) + 2 * MaxCompressedIntegerSize);
        _data.WriteByte(value ? (byte)1 : (byte)0);
        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(parameterIndex);
    }

    public void LogParameterStore(byte value, ushort parameterIndex)
    {
        _data.WriteRecordHeader(RecordKind.TypedParameterStore, payloadMaxSize: sizeof(byte) + 2 * MaxCompressedIntegerSize);
        _data.WriteByte(value);
        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(parameterIndex);
    }

    public void LogParameterStore(ushort value, ushort parameterIndex)
    {
        _data.WriteRecordHeader(RecordKind.TypedParameterStore, payloadMaxSize: sizeof(ushort) + 2 * MaxCompressedIntegerSize);
        _data.WriteUInt32(value);
        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(parameterIndex);
    }

    public void LogParameterStore(uint value, ushort parameterIndex)
    {
        _data.WriteRecordHeader(RecordKind.TypedParameterStore, payloadMaxSize: sizeof(uint) + 2 * MaxCompressedIntegerSize);
        _data.WriteUInt32(value);
        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(parameterIndex);
    }

    public void LogParameterStore(ulong value, ushort parameterIndex)
    {
        _data.WriteRecordHeader(RecordKind.TypedParameterStore, payloadMaxSize: sizeof(ulong) + 2 * MaxCompressedIntegerSize);
        _data.WriteUInt64(value);
        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(parameterIndex);
    }

    public void LogParameterStore(string value, ushort parameterIndex)
    {
        WriteStringValueStore((int)RecordKind.TypedParameterStore, (string)value);
        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(parameterIndex);
    }

    public void LogLocalStore(object value, ushort localIndex)
        => WriteUntypedStore(RecordKind.UntypedLocalStore_Base, value, localIndex);

    public void LogParameterStore(object value, ushort parameterIndex)
        => WriteUntypedStore(RecordKind.UntypedParameterStore_Base, value, parameterIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteUntypedStore(RecordKind kindBase, object value, ushort localOrParameterIndex)
    {
        const int IdSize = 2 * MaxCompressedIntegerSize;

        if (value is null)
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.Null), payloadMaxSize: 0 + IdSize);
        }
        else if (value.GetType() == typeof(bool))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.Boolean), payloadMaxSize: sizeof(byte) + IdSize);
            _data.WriteByte((bool)value ? (byte)1 : (byte)0);
        }
        else if (value.GetType() == typeof(byte))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.Byte), payloadMaxSize: sizeof(byte) + IdSize);
            _data.WriteByte((byte)value);
        }
        else if (value.GetType() == typeof(sbyte))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.SByte), payloadMaxSize: sizeof(byte) + IdSize);
            _data.WriteByte(unchecked((byte)(sbyte)value));
        }
        else if (value.GetType() == typeof(ushort))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.UInt16), payloadMaxSize: sizeof(ushort) + IdSize);
            _data.WriteUInt16((ushort)value);
        }
        else if (value.GetType() == typeof(short))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.Int16), payloadMaxSize: sizeof(ushort) + IdSize);
            _data.WriteUInt16(unchecked((ushort)(short)value));
        }
        else if (value.GetType() == typeof(char))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.Char), payloadMaxSize: sizeof(ushort) + IdSize);
            _data.WriteUInt16(unchecked((ushort)(char)value));
        }
        else if (value.GetType() == typeof(int))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.Int32), payloadMaxSize: sizeof(uint) + IdSize);
            _data.WriteUInt32(unchecked((uint)(int)value));
        }
        else if (value.GetType() == typeof(uint))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.UInt32), payloadMaxSize: sizeof(uint) + IdSize);
            _data.WriteUInt32((uint)value);
        }
        else if (value.GetType() == typeof(long))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.Int64), payloadMaxSize: sizeof(ulong) + IdSize);
            _data.WriteUInt64(unchecked((ulong)(long)value));
        }
        else if (value.GetType() == typeof(ulong))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.UInt64), payloadMaxSize: sizeof(ulong) + IdSize);
            _data.WriteUInt64((ulong)value);
        }
        else if (value.GetType() == typeof(float))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.Single), payloadMaxSize: sizeof(uint) + IdSize);
            _data.WriteUInt32(unchecked((uint)(float)value));
        }
        else if (value.GetType() == typeof(double))
        {
            _data.WriteRecordHeader((RecordKind)(kindBase + (int)TypeCode.Double), payloadMaxSize: sizeof(ulong) + IdSize);
            _data.WriteUInt64(unchecked((ulong)(double)value));
        }
        else if (value.GetType() == typeof(string))
        {
            WriteStringValueStore((int)kindBase + (int)TypeCode.String, (string)value);
        }
        else
        {
            // TODO: check for debugger display?
            // TODO: Nullable<T>

            WriteStringValueStore((int)kindBase + (int)TypeCode.Object, value.ToString() ?? "");
        }

        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(localOrParameterIndex);
    }

    // <size in characters><UTF16 string>
    private void WriteStringValueStore(int kind, string value)
    {
        // TODO: use UTF8 unpaired surrogate preserving encoding

        var length = Math.Min(value.Length, MaxStringLength);
        var size = length * sizeof(char);

        _data.WriteRecordHeader(kind, payloadMaxSize: MaxCompressedIntegerSize + size + 2 * MaxCompressedIntegerSize);
        _data.WriteCompressedInteger(length);

        fixed (char* strPtr = value)
        {
            Buffer.MemoryCopy(strPtr, _data.Advance(size), size, size);
        }
    }

    public void LogLocalStoreUnmanaged<T>(ref T local, ushort targetLocalIndex) where T : unmanaged
    {
        int size = sizeof(T);
        _data.WriteRecordHeader(RecordKind.LocalUnmanagedStore, payloadMaxSize: size + 2 * MaxCompressedIntegerSize);
        _data.WriteMemory(Unsafe.AsPointer(ref local), size);
        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(targetLocalIndex);
    }

    public void LogParameterStoreUnmanaged<T>(ref T local, ushort targetParameterIndex) where T : unmanaged
    {
        int size = sizeof(T);
        _data.WriteRecordHeader(RecordKind.ParameterUnmanagedStore, payloadMaxSize: size + 2 * MaxCompressedIntegerSize);
        _data.WriteMemory(Unsafe.AsPointer(ref local), size);
        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(targetParameterIndex);
    }

    public void LogLocalStoreAddress<T>(ref T local, ushort targetLocalIndex)
    {
        _data.WriteRecordHeader(RecordKind.LocalAddressStore, payloadMaxSize: sizeof(void*) + 2 * MaxCompressedIntegerSize);
        _data.WriteAddress(Unsafe.AsPointer(ref local));
        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(targetLocalIndex);
    }

    public void LogParameterStoreAddress<T>(ref T param, ushort targetParameterIndex)
    {
        _data.WriteRecordHeader(RecordKind.ParameterAddressStore, payloadMaxSize: sizeof(void*) + 2 * MaxCompressedIntegerSize);
        _data.WriteAddress(Unsafe.AsPointer(ref param));
        _data.WriteCompressedInteger(_methodId);
        _data.WriteCompressedInteger(targetParameterIndex);
    }
}
