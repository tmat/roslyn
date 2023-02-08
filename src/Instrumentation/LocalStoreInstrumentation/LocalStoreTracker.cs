// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// The following code is not meant to be debuggable.
//#line hidden

// #pragma warning disable

// Emit no annotations
#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers.Binary;

namespace Microsoft.CodeAnalysis.Runtime;

public unsafe readonly struct LocalStoreTracker
{
    internal const int ProtocolVersion = 1;
    internal const int MaxCompressedIntegerSize = 4;
    internal const int MaxLocalVariableCount = 0x10000;
    internal const int LiftedVariableBaseIndex = 0x10000;

    /// <summary>
    /// Kind of the record in the data stream.
    /// 
    /// Local/parameter index refers to a an index of a local variable or a parameter within the local/parameter signature of the method identified by <c>method-id</c>,
    /// unless the variable is lifted. If the variable is lifted the index - <see cref="LiftedVariableBaseIndex"/> is the row id of the FieldDef that stores the variable.
    /// </summary>
    internal enum RecordKind
    {
        /// <summary>
        /// Indicates end of data blob in the buffer.
        /// </summary>
        EndOfData = 0,

        /// <summary>
        /// Logged when method is entered.
        /// If the method is MoveNext of a state machine <see cref="MethodEntry"/> is only logged when the state machine starts,
        /// not each time it resumes.
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int}</code>
        /// </summary>
        MethodEntry,

        /// <summary>
        /// Logged when a lambda or a local function is entered.
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int} {lambda-id: compressed-int}</code>
        /// </summary>
        LambdaEntry,

        /// <summary>
        /// An alias of a source local variable is stored to a target local variable.
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int} {target-local-index: compressed-int} {source-local-index: compressed-int}</code>
        /// </summary>
        LocalAliasStoreToLocal,

        /// <summary>
        /// An alias of a source parameter is stored to a target local variable.
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int} {target-local-index: compressed-int} {source-parameter-index: compressed-int}</code>
        /// </summary>
        ParameterAliasStoreToLocal,

        /// <summary>
        /// An alias of a source parameter is stored to a target parameter.
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int} {target-parameter-index: compressed-int} {source-parameter-index: compressed-int}</code>
        /// </summary>
        ParameterAliasStoreToParameter,

        /// <summary>
        /// Value is stored to a parameter. The value is serialized based upon the static type of the parameter.
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int} {parameter-index: compressed-int} {value: type-specific}</code>
        /// </summary>
        ParameterStore,

        /// <summary>
        /// Value is stored to a parameter whose type is unmanaged (blittable).
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int} {parameter-index: compressed-int} {value: blob}</code>
        /// The size of the blob is determined from the type of the variable (<code>sizeof(T)</code>).
        /// </summary>
        ParameterUnmanagedStore,

        /// <summary>
        /// Value is stored to a local variable whose type is object or dynamic.
        /// The <see cref="TypeCode"/> of the runtime type of the value is added to <see cref="UntypedLocalStore_Base"/>.
        /// 
        /// Format: <code>{record-kind-base+type-code: compressed-int} {value: type-code-specific} {method-id: compressed-int} {parameter-index: compressed-int}</code>
        /// </summary>
        UntypedParameterStore_Base,
        UntypedParameterStore_Max = UntypedParameterStore_Base + TypeCode.Count - 1,

        /// <summary>
        /// Value is stored to a local variable whose type is unmanaged (blittable).
        /// The value is serialized by copying the memory content to the stream as is.
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int} {local-index: compressed-int} {value: blob}</code>
        /// The size of the blob is determined from the type of the variable (<code>sizeof(T)</code>).
        /// </summary>
        LocalUnmanagedStore,

        /// <summary>
        /// Value is stored to a local variable whose type is object or dynamic.
        /// The <see cref="TypeCode"/> of the runtime type of teh value is added to <see cref="UntypedLocalStore_Base"/>.
        /// 
        /// Format: <code>{record-kind-base+type-code: compressed-int} {value: type-code-specific} {method-id: compressed-int} {local-index: compressed-int}</code>
        /// </summary>
        UntypedLocalStore_Base,
        UntypedLocalStore_Max = UntypedLocalStore_Base + TypeCode.Count - 1,

        /// <summary>
        /// Value is stored to a local variable. The value is serialized based upon the static type of the local variable.
        /// The local index is added to the record kind.
        /// 
        /// If the local is lifted the index is <see cref="LiftedVariableBaseIndex"/> + RowId [1..0xffffff]
        /// of the FieldDef that stores their value (state machine or closure field).
        /// 
        /// Format: <code>{record-kind+local-index: compressed-int} {method-id: compressed-int} {value: type-specific}</code>
        /// </summary>
        LocalStore_Base
    }

    internal enum TypeCode
    {
        /// <summary>
        /// 1 byte
        /// </summary>
        SByte = 0,

        /// <summary>
        /// 1 byte
        /// </summary>
        Byte,

        /// <summary>
        /// 2 bytes
        /// </summary>
        Int16,

        /// <summary>
        /// 2 bytes
        /// </summary>
        UInt16,

        /// <summary>
        /// 4 bytes
        /// </summary>
        Int32,

        /// <summary>
        /// 4 bytes
        /// </summary>
        UInt32,

        /// <summary>
        /// 8 bytes
        /// </summary>
        Int64,

        /// <summary>
        /// 8 bytes
        /// </summary>
        UInt64,

        /// <summary>
        /// The value is encoded as
        /// <code>{underlying-value} {mvid: guid} {type-token: uint}</code>
        /// </summary>
        Enum_Base,
        Enum_Max = Enum_Base + (UInt64 - SByte + 1),

        /// <summary>
        /// The value is encoded as a single byte 1 (true) or 0 (false).
        /// </summary>
        Boolean,

        /// <summary>
        /// 4 bytes
        /// </summary>
        Single,

        /// <summary>
        /// 8 bytes
        /// </summary>
        Double,

        /// <summary>
        /// 2 bytes
        /// </summary>
        Char,

        /// <summary>
        /// The value is encoded as
        /// <code>{size-of-string-value-in-bytes: compressed int} {string-value: UTF16}</code>
        /// </summary>
        String,

        /// <summary>
        /// The value is encoded as
        /// <code>{size-of-string-value-in-bytes: compressed int} {string-value: UTF16} {mvid: guid} {type-token: uint}</code>
        /// </summary>
        Object,

        /// <summary>
        /// No value.
        /// </summary>
        Null,

        Count
    }

    internal class ThreadData
    {
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

        public ThreadData(int bufferSize)
        {
            _buffer = (byte*)NativeMemory.Alloc((nuint)bufferSize);
            GC.AddMemoryPressure(bufferSize);

            _position = _buffer;

            // reserve one byte at the end of the buffer to indicate end of data
            _bufferEnd = _buffer + bufferSize - 1;

            _threadId = Environment.CurrentManagedThreadId;

            var span = new Span<byte>(_buffer, bufferSize);
            span.Clear();
            BufferCreated(_threadId, _buffer, bufferSize, ProtocolVersion);
        }

        // for testing:
        internal byte* BufferPointer => _buffer;

        // for testing:
        protected virtual void BufferFull(int managedThreadId, byte* buffer, int dataSize, int protocolVersion)
            => LocalStoreTracker.BufferFull(managedThreadId, buffer, dataSize, protocolVersion);

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
            var size = (int)(_position - _buffer);
            BufferFull(_threadId, _buffer, size, ProtocolVersion);
            new Span<byte>(_buffer, size).Clear();
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
        public void WriteDouble(double value)
            => *(double*)Advance(sizeof(double)) = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloat(float value)
            => *(float*)Advance(sizeof(float)) = value;

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
                    WriteUInt16(BinaryPrimitives.ReverseEndianness((ushort)(0x8000 | value)));
                }
                else // if (value <= MaxCompressedIntegerValue)
                {
                    WriteUInt32(BinaryPrimitives.ReverseEndianness(0xc0000000 | (uint)value));
                }
            }
        }

        // <size in bytes><UTF16 string>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteString(string value, int size)
        {
            WriteCompressedInteger(size);

            fixed (char* strPtr = value)
            {
                Buffer.MemoryCopy(strPtr, Advance(size), size, size);
            }
        }

        public void WriteType(Type type)
        {
            var mvid = type.Assembly.ManifestModule.ModuleVersionId;
            WriteMemory(&mvid, sizeof(Guid));
            int token;

            try
            {
                token = type.MetadataToken;
            }
            catch
            {
                token = 0;
            }

            WriteUInt32(unchecked((uint)token));
        }
    }

    /// <summary>
    /// Internal for testing.
    /// </summary>
    [ThreadStatic]
    internal static ThreadData s_threadData;

    private readonly ThreadData _data;
    private readonly int _methodId;

    /// <summary>
    /// Maximum serialized string length.
    /// Can be set when the program is suspended at <see cref="BufferCreated(int, byte*, int, int)"/> or <see cref="BufferFull(int, byte*, int, int)"/>.
    /// Affects the next serialized string.
    /// </summary>
    public static int MaxSerializedStringLength = 64;

    /// <summary>
    /// The size of a per-thread data buffer.
    /// The value can be changed at any time and will affect buffers allocated afterwards.
    /// </summary>
    public static int BufferSize = 4 * 1024;

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
    public static void BufferCreated(int managedThreadId, byte* buffer, int bufferSize, int protocolVersion)
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
    public static void BufferFull(int managedThreadId, byte* buffer, int dataSize, int protocolVersion)
    {
        // Exfiltrate buffer data by placing a tracepoint here
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LocalStoreTracker LogMethodEntry(int methodId)
    {
        var data = s_threadData ??= new ThreadData(BufferSize);
        data.WriteRecordHeader(RecordKind.MethodEntry, payloadMaxSize: MaxCompressedIntegerSize);
        data.WriteCompressedInteger(methodId);
        return new(data, methodId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LocalStoreTracker LogLambdaEntry(int methodId, int lambdaId)
    {
        var data = s_threadData ??= new ThreadData(BufferSize);
        data.WriteRecordHeader(RecordKind.LambdaEntry, payloadMaxSize: 2 * MaxCompressedIntegerSize);
        data.WriteCompressedInteger(methodId);
        data.WriteCompressedInteger(lambdaId);
        return new(data, lambdaId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteLocalStoreRecordHeaderAndId(int localIndex, int valueSize)
    {
        _data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, MaxCompressedIntegerSize + valueSize);
        WriteMethodId();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteVariableStoreRecordHeaderAndId(RecordKind kind, int localOrParameterIndex, int valueSize)
    {
        _data.WriteRecordHeader((int)kind, MethodAndVariableIdSize + valueSize);
        WriteMethodAndVariableId(localOrParameterIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteParameterStoreRecordHeaderAndId(int parameterIndex, int valueSize)
        => WriteVariableStoreRecordHeaderAndId(RecordKind.ParameterStore, parameterIndex, valueSize);

    public void LogLocalStore(bool value, int localIndex)
    {
        WriteLocalStoreRecordHeaderAndId(localIndex, sizeof(byte));
        _data.WriteByte(value ? (byte)1 : (byte)0);
    }

    public void LogLocalStore(byte value, int localIndex)
    {
        WriteLocalStoreRecordHeaderAndId(localIndex, sizeof(byte));
        _data.WriteByte(value);
    }

    public void LogLocalStore(ushort value, int localIndex)
    {
        WriteLocalStoreRecordHeaderAndId(localIndex, sizeof(ushort));
        _data.WriteUInt16(value);
    }

    public void LogLocalStore(uint value, int localIndex)
    {
        WriteLocalStoreRecordHeaderAndId(localIndex, sizeof(uint));
        _data.WriteUInt32(value);
    }

    public void LogLocalStore(ulong value, int localIndex)
    {
        WriteLocalStoreRecordHeaderAndId(localIndex, sizeof(ulong));
        _data.WriteUInt64(value);
    }

    public void LogLocalStore(string value, int localIndex)
    {
        var size = GetSerializedStringSize(value);
        WriteLocalStoreRecordHeaderAndId(localIndex, size);
        _data.WriteString(value, size);
    }

    public void LogLocalStore(void* value, int localIndex)
    {
        WriteLocalStoreRecordHeaderAndId(localIndex, sizeof(void*));
        _data.WriteAddress(value);
    }

    public void LogParameterStore(bool value, int parameterIndex)
    {
        WriteParameterStoreRecordHeaderAndId(parameterIndex, sizeof(byte));
        _data.WriteByte(value ? (byte)1 : (byte)0);
    }

    public void LogParameterStore(byte value, int parameterIndex)
    {
        WriteParameterStoreRecordHeaderAndId(parameterIndex, sizeof(byte));
        _data.WriteByte(value);
    }

    public void LogParameterStore(ushort value, int parameterIndex)
    {
        WriteParameterStoreRecordHeaderAndId(parameterIndex, sizeof(ushort));
        _data.WriteUInt16(value);
    }

    public void LogParameterStore(uint value, int parameterIndex)
    {
        WriteParameterStoreRecordHeaderAndId(parameterIndex, sizeof(uint));
        _data.WriteUInt32(value);
    }

    public void LogParameterStore(ulong value, int parameterIndex)
    {
        WriteParameterStoreRecordHeaderAndId(parameterIndex, sizeof(ulong));
        _data.WriteUInt64(value);
    }

    public void LogParameterStore(string value, int parameterIndex)
    {
        var size = GetSerializedStringSize(value);
        WriteParameterStoreRecordHeaderAndId(parameterIndex, size);
        _data.WriteString(value, size);
    }

    public void LogParameterStore(void* value, int parameterIndex)
    {
        WriteParameterStoreRecordHeaderAndId(parameterIndex, sizeof(void*));
        _data.WriteAddress(value);
    }

    public void LogLocalStore(object value, int localIndex)
        => WriteUntypedStore(RecordKind.UntypedLocalStore_Base, value, localIndex);

    public void LogParameterStore(object value, int parameterIndex)
        => WriteUntypedStore(RecordKind.UntypedParameterStore_Base, value, parameterIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteUntypedStore(RecordKind kindBase, object value, int localOrParameterIndex)
    {
        // Note: Value of Nullable<T> is boxed as type T. We do not need to explicitly handle nullable types below.

        if (value is null)
            _data.WriteRecordHeader(kindBase + (int)TypeCode.Null, payloadMaxSize: 0 + MethodAndVariableIdSize);
        else if (value.GetType() == typeof(int))
            WriteRecordHeaderAndValue(kindBase, (int)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(bool))
            WriteRecordHeaderAndValue(kindBase, (bool)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(string))
            WriteRecordHeaderAndValue(kindBase, (string)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(double))
            WriteRecordHeaderAndValue(kindBase, (double)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(float))
            WriteRecordHeaderAndValue(kindBase, (float)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(char))
            WriteRecordHeaderAndValue(kindBase, (char)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(byte))
            WriteRecordHeaderAndValue(kindBase, (byte)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(long))
            WriteRecordHeaderAndValue(kindBase, (long)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(uint))
            WriteRecordHeaderAndValue(kindBase, (uint)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(short))
            WriteRecordHeaderAndValue(kindBase, (short)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(sbyte))
            WriteRecordHeaderAndValue(kindBase, (sbyte)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(ulong))
            WriteRecordHeaderAndValue(kindBase, (ulong)value, MethodAndVariableIdSize);
        else if (value.GetType() == typeof(ushort))
            WriteRecordHeaderAndValue(kindBase, (ushort)value, MethodAndVariableIdSize);
        else if (value.GetType().IsEnum)
        {
            const int MethodAndVariableIdAndTypeSize = MethodAndVariableIdSize + EncodedTypeSize;

            var enumType = value.GetType();
            var underlyingType = enumType.GetEnumUnderlyingType();
            var enumBase = kindBase + (int)TypeCode.Enum_Base;

            if (underlyingType == typeof(int))
            {
                WriteRecordHeaderAndValue(enumBase, (int)value, MethodAndVariableIdAndTypeSize);
                _data.WriteType(enumType);
            }
            else if (underlyingType == typeof(sbyte))
            {
                WriteRecordHeaderAndValue(enumBase, (sbyte)value, MethodAndVariableIdAndTypeSize);
                _data.WriteType(enumType);
            }
            else if (underlyingType == typeof(byte))
            {
                WriteRecordHeaderAndValue(enumBase, (byte)value, MethodAndVariableIdAndTypeSize);
                _data.WriteType(enumType);
            }
            else if (underlyingType == typeof(short))
            {
                WriteRecordHeaderAndValue(enumBase, (short)value, MethodAndVariableIdAndTypeSize);
                _data.WriteType(enumType);
            }
            else if (underlyingType == typeof(ushort))
            {
                WriteRecordHeaderAndValue(enumBase, (ushort)value, MethodAndVariableIdAndTypeSize);
                _data.WriteType(enumType);
            }
            else if (underlyingType == typeof(uint))
            {
                WriteRecordHeaderAndValue(enumBase, (uint)value, MethodAndVariableIdAndTypeSize);
                _data.WriteType(enumType);
            }
            else if (underlyingType == typeof(long))
            {
                WriteRecordHeaderAndValue(enumBase, (long)value, MethodAndVariableIdAndTypeSize);
                _data.WriteType(enumType);
            }
            else if (underlyingType == typeof(ulong))
            {
                WriteRecordHeaderAndValue(enumBase, (ulong)value, MethodAndVariableIdAndTypeSize);
                _data.WriteType(enumType);
            }
            else
            {
                WriteRecordHeaderAndObjectValue(kindBase, value, MethodAndVariableIdSize);
            }
        }
        else
        {
            WriteRecordHeaderAndObjectValue(kindBase, value, MethodAndVariableIdSize);
        }

        WriteMethodAndVariableId(localOrParameterIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteMethodId()
    {
        _data.WriteCompressedInteger(_methodId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteMethodAndVariableId(int localOrParameterIndex)
    {
        WriteMethodId();
        _data.WriteCompressedInteger(localOrParameterIndex);
    }

    private const int EncodedTypeSize = 16 + sizeof(uint); // mvid + token
    private const int MethodIdSize = MaxCompressedIntegerSize;
    private const int MethodAndVariableIdSize = MethodIdSize + MaxCompressedIntegerSize;

    private static int GetSerializedStringSize(string value)
        => Math.Min(value.Length, MaxSerializedStringLength) * sizeof(char);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, bool value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.Boolean, payloadMaxSize: sizeof(byte) + additionalSize);
        _data.WriteByte(value ? (byte)1 : (byte)0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, sbyte value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.SByte, payloadMaxSize: sizeof(byte) + additionalSize);
        _data.WriteByte(unchecked((byte)value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, byte value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.Byte, payloadMaxSize: sizeof(byte) + additionalSize);
        _data.WriteByte(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, int value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.Int32, payloadMaxSize: sizeof(uint) + additionalSize);
        _data.WriteUInt32(unchecked((uint)value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, uint value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.UInt32, payloadMaxSize: sizeof(uint) + additionalSize);
        _data.WriteUInt32(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, short value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.Int16, payloadMaxSize: sizeof(ushort) + additionalSize);
        _data.WriteUInt16(unchecked((ushort)value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, ushort value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.UInt16, payloadMaxSize: sizeof(ushort) + additionalSize);
        _data.WriteUInt16(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, char value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.Char, payloadMaxSize: sizeof(ushort) + additionalSize);
        _data.WriteUInt16(unchecked((ushort)value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, long value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.Int64, payloadMaxSize: sizeof(ulong) + additionalSize);
        _data.WriteUInt64(unchecked((ulong)value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, ulong value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.UInt64, payloadMaxSize: sizeof(ulong) + additionalSize);
        _data.WriteUInt64(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, float value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.Single, payloadMaxSize: sizeof(uint) + additionalSize);
        _data.WriteFloat(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, double value, int additionalSize)
    {
        _data.WriteRecordHeader(kindBase + (int)TypeCode.Double, payloadMaxSize: sizeof(ulong) + additionalSize);
        _data.WriteDouble(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndValue(RecordKind kindBase, string value, int additionalSize)
    {
        var size = GetSerializedStringSize(value);
        _data.WriteRecordHeader(kindBase + (int)TypeCode.String, payloadMaxSize: size + additionalSize);
        _data.WriteString(value, size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteRecordHeaderAndObjectValue(RecordKind kindBase, object value, int additionalSize)
    {
        var stringValue = (value is Exception exception ? exception.Message : value.ToString()) ?? "";

        var stringSize = GetSerializedStringSize(stringValue);
        _data.WriteRecordHeader(kindBase + (int)TypeCode.Object, payloadMaxSize: stringSize + EncodedTypeSize + additionalSize);
        _data.WriteString(stringValue, stringSize);
        _data.WriteType(value.GetType());
    }

    public void LogLocalStoreUnmanaged<T>(ref T local, int localIndex)
    {
        var size = Unsafe.SizeOf<T>();
        WriteVariableStoreRecordHeaderAndId(RecordKind.LocalUnmanagedStore, localIndex, size);
        _data.WriteMemory(Unsafe.AsPointer(ref local), size);
    }

    public void LogParameterStoreUnmanaged<T>(ref T local, int parameterIndex)
    {
        var size = Unsafe.SizeOf<T>();
        WriteVariableStoreRecordHeaderAndId(RecordKind.ParameterUnmanagedStore, parameterIndex, size);
        _data.WriteMemory(Unsafe.AsPointer(ref local), size);
    }

    /// <summary>
    /// targetLocal = ref sourceLocal;
    /// </summary>
    public void LogLocalStoreLocalAlias(int sourceLocalIndex, int targetLocalIndex)
        => LogAlias(RecordKind.LocalAliasStoreToLocal, sourceLocalIndex, targetLocalIndex);

    /// <summary>
    /// targetLocal = ref sourceParameter;
    /// </summary>
    public void LogLocalStoreParameterAlias(int sourceParameterIndex, int targetLocalIndex)
        => LogAlias(RecordKind.ParameterAliasStoreToLocal, sourceParameterIndex, targetLocalIndex);

    /// <summary>
    /// targetParameter = ref sourceParameter;
    /// </summary>
    public void LogParameterStoreParameterAlias(int sourceParameterIndex, int targetParameterIndex)
        => LogAlias(RecordKind.ParameterAliasStoreToParameter, sourceParameterIndex, targetParameterIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogAlias(RecordKind kind, int sourceIndex, int targetIndex)
    {
        WriteVariableStoreRecordHeaderAndId(kind, targetIndex, MaxCompressedIntegerSize);
        _data.WriteCompressedInteger(sourceIndex);
    }
}
