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

public readonly unsafe ref struct LocalStoreTracker
{
    internal const int ProtocolVersion = 1;
    internal const int MaxCompressedIntegerSize = 4;
    internal const int MaxLongCompressedIntegerSize = 1 + sizeof(ulong);
    internal const int MaxLocalVariableCount = 0x10000;
    internal const int LiftedVariableBaseIndex = 0x10000;
    internal const int MaxCompressedIntegerValue = 0x1fffffff;

    private const int EncodedTypeSize = 16 + sizeof(uint); // mvid + token
    private const int MethodIdSize = MaxCompressedIntegerSize;
    private const int VariableIdSize = MaxCompressedIntegerSize;

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
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int}</code>
        /// </summary>
        MethodEntry,

        /// <summary>
        /// Logged when lambda or local function is entered.
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int} {lambda-id: compressed-int}</code>
        /// </summary>
        LambdaEntry,

        /// <summary>
        /// Logged when state machine method is entered.
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int} {state-machine-id: compressed-long}</code>
        /// </summary>
        StateMachineMethodEntry,

        /// <summary>
        /// Logged when state machine lambda or local function is entered.
        /// 
        /// Format: <code>{record-kind: compressed-int} {method-id: compressed-int} {lambda-id: compressed-int} {state-machine-id: compressed-long}</code>
        /// </summary>
        StateMachineLambdaEntry,

        /// <summary>
        /// Logged when a function return.
        /// Format: <code>{record-kind: compressed-int}</code>
        /// </summary>
        Return,

        /// <summary>
        /// An alias of a source local variable is stored to a target local variable.
        /// 
        /// Format: <code>{record-kind: compressed-int} {target-local-index: compressed-int} {source-local-index: compressed-int}</code>
        /// </summary>
        LocalAliasStoreToLocal,

        /// <summary>
        /// An alias of a source parameter is stored to a target local variable.
        /// 
        /// Format: <code>{record-kind: compressed-int} {target-local-index: compressed-int} {source-parameter-index: compressed-int}</code>
        /// </summary>
        ParameterAliasStoreToLocal,

        /// <summary>
        /// An alias of a source parameter is stored to a target parameter.
        /// 
        /// Format: <code>{record-kind: compressed-int} {target-parameter-index: compressed-int} {source-parameter-index: compressed-int}</code>
        /// </summary>
        ParameterAliasStoreToParameter,

        /// <summary>
        /// Value is stored to a parameter. The value is serialized based upon the static type of the parameter.
        /// 
        /// Format: <code>{record-kind: compressed-int} {parameter-index: compressed-int} {value: type-specific}</code>
        /// </summary>
        ParameterStore,

        /// <summary>
        /// Value is stored to a parameter whose type is unmanaged (blittable).
        /// 
        /// Format: <code>{record-kind: compressed-int} {parameter-index: compressed-int} {value: blob}</code>
        /// The size of the blob is determined from the type of the variable (<code>sizeof(T)</code>).
        /// </summary>
        ParameterUnmanagedStore,

        /// <summary>
        /// Value is stored to a local variable whose type is object or dynamic.
        /// The <see cref="TypeCode"/> of the runtime type of the value is added to <see cref="UntypedLocalStore_Base"/>.
        /// 
        /// Format: <code>{record-kind-base+type-code: compressed-int} {value: type-code-specific} {parameter-index: compressed-int}</code>
        /// </summary>
        UntypedParameterStore_Base,
        UntypedParameterStore_Max = UntypedParameterStore_Base + TypeCode.Count - 1,

        /// <summary>
        /// Value is stored to a local variable whose type is unmanaged (blittable).
        /// The value is serialized by copying the memory content to the stream as is.
        /// 
        /// Format: <code>{record-kind: compressed-int} {local-index: compressed-int} {value: blob}</code>
        /// The size of the blob is determined from the type of the variable (<code>sizeof(T)</code>).
        /// </summary>
        LocalUnmanagedStore,

        /// <summary>
        /// Value is stored to a local variable whose type is object or dynamic.
        /// The <see cref="TypeCode"/> of the runtime type of teh value is added to <see cref="UntypedLocalStore_Base"/>.
        /// 
        /// Format: <code>{record-kind-base+type-code: compressed-int} {value: type-code-specific} {local-index: compressed-int}</code>
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
        /// Format: <code>{record-kind+local-index: compressed-int} {value: type-specific}</code>
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
        /// 16 bytes
        /// </summary>
        Decimal,

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
        public byte* WriteRecordHeader(RecordKind kind, int payloadMaxSize)
        {
            var position = _position;
            if (position > _bufferEnd - payloadMaxSize - sizeof(byte))
            {
                position = Reset();
            }

            return WriteByte(position, (byte)kind);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeader(int kind, int payloadMaxSize)
        {
            var position = _position;
            if (position > _bufferEnd - payloadMaxSize - MaxCompressedIntegerSize)
            {
                position = Reset();
            }

            return WriteCompressedInteger(position, kind);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, bool value, int additionalSize)
            => WriteByte(WriteRecordHeader(kindBase + (int)TypeCode.Boolean, payloadMaxSize: sizeof(byte) + additionalSize), value ? (byte)1 : (byte)0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, sbyte value, int additionalSize)
            => WriteByte(WriteRecordHeader(kindBase + (int)TypeCode.SByte, payloadMaxSize: sizeof(byte) + additionalSize), unchecked((byte)value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, byte value, int additionalSize)
            => WriteByte(WriteRecordHeader(kindBase + (int)TypeCode.Byte, payloadMaxSize: sizeof(byte) + additionalSize), unchecked(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, int value, int additionalSize)
            => WriteUInt32(WriteRecordHeader(kindBase + (int)TypeCode.Int32, payloadMaxSize: sizeof(uint) + additionalSize), unchecked((uint)value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, uint value, int additionalSize)
            => WriteUInt32(WriteRecordHeader(kindBase + (int)TypeCode.UInt32, payloadMaxSize: sizeof(uint) + additionalSize), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, short value, int additionalSize)
            => WriteUInt16(WriteRecordHeader(kindBase + (int)TypeCode.Int16, payloadMaxSize: sizeof(ushort) + additionalSize), unchecked((ushort)value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, ushort value, int additionalSize)
            => WriteUInt16(WriteRecordHeader(kindBase + (int)TypeCode.UInt16, payloadMaxSize: sizeof(ushort) + additionalSize), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, char value, int additionalSize)
            => WriteUInt16(WriteRecordHeader(kindBase + (int)TypeCode.Char, payloadMaxSize: sizeof(ushort) + additionalSize), unchecked((ushort)value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, long value, int additionalSize)
            => WriteUInt64(WriteRecordHeader(kindBase + (int)TypeCode.Int64, payloadMaxSize: sizeof(ulong) + additionalSize), unchecked((ulong)value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, ulong value, int additionalSize)
            => WriteUInt64(WriteRecordHeader(kindBase + (int)TypeCode.UInt64, payloadMaxSize: sizeof(ulong) + additionalSize), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, float value, int additionalSize)
            => WriteFloat(WriteRecordHeader(kindBase + (int)TypeCode.Single, payloadMaxSize: sizeof(uint) + additionalSize), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, double value, int additionalSize)
            => WriteDouble(WriteRecordHeader(kindBase + (int)TypeCode.Double, payloadMaxSize: sizeof(ulong) + additionalSize), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, decimal value, int additionalSize)
            => WriteDecimal(WriteRecordHeader(kindBase + (int)TypeCode.Decimal, payloadMaxSize: sizeof(decimal) + additionalSize), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndValue(RecordKind kindBase, string value, int additionalSize)
        {
            var size = GetSerializedStringSize(value);
            return WriteString(WriteRecordHeader(kindBase + (int)TypeCode.String, payloadMaxSize: size + additionalSize), value, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndEnumValue(RecordKind kindBase, object value)
        {
            const int TypeAndVariableIdSize = EncodedTypeSize + VariableIdSize;

            var enumType = value.GetType();
            var underlyingType = enumType.GetEnumUnderlyingType();
            var enumBase = kindBase + (int)TypeCode.Enum_Base;

            return
                (underlyingType == typeof(int)) ? WriteType(WriteRecordHeaderAndValue(enumBase, (int)value, TypeAndVariableIdSize), enumType) :
                (underlyingType == typeof(byte)) ? WriteType(WriteRecordHeaderAndValue(enumBase, (byte)value, TypeAndVariableIdSize), enumType) :
                (underlyingType == typeof(short)) ? WriteType(WriteRecordHeaderAndValue(enumBase, (short)value, TypeAndVariableIdSize), enumType) :
                (underlyingType == typeof(long)) ? WriteType(WriteRecordHeaderAndValue(enumBase, (long)value, TypeAndVariableIdSize), enumType) :
                (underlyingType == typeof(sbyte)) ? WriteType(WriteRecordHeaderAndValue(enumBase, (sbyte)value, TypeAndVariableIdSize), enumType) :
                (underlyingType == typeof(ushort)) ? WriteType(WriteRecordHeaderAndValue(enumBase, (ushort)value, TypeAndVariableIdSize), enumType) :
                (underlyingType == typeof(uint)) ? WriteType(WriteRecordHeaderAndValue(enumBase, (uint)value, TypeAndVariableIdSize), enumType) :
                (underlyingType == typeof(ulong)) ? WriteType(WriteRecordHeaderAndValue(enumBase, (ulong)value, TypeAndVariableIdSize), enumType) :
                WriteRecordHeaderAndObjectValue(kindBase, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* WriteRecordHeaderAndObjectValue(RecordKind kindBase, object value)
        {
            var stringValue = (value is Exception exception ? exception.Message : value.ToString()) ?? "";

            var stringSize = GetSerializedStringSize(stringValue);
            return WriteType(
                WriteString(
                    WriteRecordHeader(kindBase + (int)TypeCode.Object, payloadMaxSize: stringSize + EncodedTypeSize + VariableIdSize),
                    stringValue,
                    stringSize),
                value.GetType());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void EndRecord(byte* position)
            => _position = position;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public byte* Reset()
        {
            var size = (int)(_position - _buffer);
            BufferFull(_threadId, _buffer, size, ProtocolVersion);
            new Span<byte>(_buffer, size).Clear();
            return _position = _buffer;
        }
    }

    /// <summary>
    /// Internal for testing.
    /// </summary>
    [ThreadStatic]
    internal static ThreadData s_threadData;

    private readonly ThreadData _data;

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

    private LocalStoreTracker(ThreadData data)
    {
        _data = data;
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
    private static byte* WriteByte(byte* position, byte value)
    {
        *position = value;
        return position + sizeof(byte);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteUInt16(byte* position, ushort value)
    {
        *(ushort*)position = value;
        return position + sizeof(ushort);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteUInt32(byte* position, uint value)
    {
        *(uint*)position = value;
        return position + sizeof(uint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteUInt64(byte* position, ulong value)
    {
        *(ulong*)position = value;
        return position + sizeof(ulong);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteFloat(byte* position, float value)
    {
        *(float*)position = value;
        return position + sizeof(float);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteDouble(byte* position, double value)
    {
        *(double*)position = value;
        return position + sizeof(double);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteDecimal(byte* position, decimal value)
    {
        *(decimal*)position = value;
        return position + sizeof(decimal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteAddress(byte* position, void* value)
    {
        *(void**)position = value;
        return position + sizeof(void*);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteMemory(byte* position, void* ptr, int size)
    {
        Buffer.MemoryCopy(ptr, position, size, size);
        return position + size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteCompressedInteger(byte* position, int value)
    {
        unchecked
        {
            if (value <= 0x7f)
                return WriteByte(position, (byte)value);

            if (value <= 0x3fff)
                return WriteUInt16(position, BinaryPrimitives.ReverseEndianness((ushort)(0x8000 | value)));

            return WriteUInt32(position, BinaryPrimitives.ReverseEndianness(0xc0000000 | (uint)value));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteCompressedLongInteger(byte* position, ulong value)
    {
        unchecked
        {
            // TODO: improve encoding
            if (value <= MaxCompressedIntegerValue)
                return WriteCompressedInteger(WriteByte(position, 0), (int)value);

            return WriteUInt64(WriteByte(position, 1), value);
        }
    }

    // <size in bytes><UTF16 string>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* WriteString(byte* position, string value, int size)
    {
        position = WriteCompressedInteger(position, size);
        fixed (char* strPtr = value)
        {
            Buffer.MemoryCopy(strPtr, position, size, size);
            return position + size;
        }
    }

    private static byte* WriteType(byte* position, Type type)
    {
        var mvid = type.Assembly.ManifestModule.ModuleVersionId;
        position = WriteMemory(position, &mvid, sizeof(Guid));
        int token;

        try
        {
            token = type.MetadataToken;
        }
        catch
        {
            token = 0;
        }

        return WriteUInt32(position, unchecked((uint)token));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LocalStoreTracker LogMethodEntry(int methodId)
    {
        var data = s_threadData ??= new ThreadData(BufferSize);

        data.EndRecord(
            WriteCompressedInteger(
                data.WriteRecordHeader(RecordKind.MethodEntry, payloadMaxSize: MaxCompressedIntegerSize),
                methodId));

        return new(data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LocalStoreTracker LogLambdaEntry(int methodId, int lambdaId)
    {
        var data = s_threadData ??= new ThreadData(BufferSize);

        data.EndRecord(
            WriteCompressedInteger(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.LambdaEntry, payloadMaxSize: 2 * MaxCompressedIntegerSize),
                    methodId),
                lambdaId));

        return new(data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LocalStoreTracker LogStateMachineMethodEntry(int methodId, ulong stateMachineInstanceId)
    {
        var data = s_threadData ??= new ThreadData(BufferSize);

        data.EndRecord(
            WriteCompressedLongInteger(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.StateMachineMethodEntry, payloadMaxSize: MaxCompressedIntegerSize + MaxLongCompressedIntegerSize),
                    methodId),
                stateMachineInstanceId));

        return new(data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LocalStoreTracker LogStateMachineLambdaEntry(int methodId, int lambdaId, ulong stateMachineInstanceId)
    {
        var data = s_threadData ??= new ThreadData(BufferSize);

        data.EndRecord(
            WriteCompressedLongInteger(
                WriteCompressedInteger(
                    WriteCompressedInteger(
                        data.WriteRecordHeader(RecordKind.StateMachineLambdaEntry, payloadMaxSize: 2 * MaxCompressedIntegerSize + MaxLongCompressedIntegerSize),
                        methodId),
                    lambdaId),
                stateMachineInstanceId));

        return new(data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogReturn()
    {
        var data = _data;
        data.EndRecord(data.WriteRecordHeader((int)RecordKind.Return, 0));
    }

    public void LogLocalStore(bool value, int localIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteByte(
                data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, sizeof(byte)),
                value ? (byte)1 : (byte)0));
    }

    public void LogLocalStore(byte value, int localIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteByte(
                data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, sizeof(byte)),
                value));
    }

    public void LogLocalStore(ushort value, int localIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteUInt16(
                data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, sizeof(ushort)),
                value));
    }

    public void LogLocalStore(uint value, int localIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteUInt32(
                data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, sizeof(uint)),
                value));
    }

    public void LogLocalStore(ulong value, int localIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteUInt64(
                data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, sizeof(ulong)),
                value));
    }

    public void LogLocalStore(float value, int localIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteFloat(
                data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, sizeof(float)),
                value));
    }

    public void LogLocalStore(double value, int localIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteDouble(
                data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, sizeof(double)),
                value));
    }

    public void LogLocalStore(decimal value, int localIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteDecimal(
                data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, sizeof(decimal)),
                value));
    }

    public void LogLocalStore(string value, int localIndex)
    {
        var size = GetSerializedStringSize(value);
        var data = _data;
        data.EndRecord(
            WriteString(
                data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, size),
                value,
                size));
    }

    public void LogLocalStore(void* value, int localIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteAddress(
                data.WriteRecordHeader((int)RecordKind.LocalStore_Base + localIndex, sizeof(void*)),
                value));
    }

    public void LogParameterStore(bool value, int parameterIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteByte(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterStore, MaxCompressedIntegerSize + sizeof(byte)),
                    parameterIndex),
                value ? (byte)1 : (byte)0));
    }

    public void LogParameterStore(byte value, int parameterIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteByte(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterStore, MaxCompressedIntegerSize + sizeof(byte)),
                    parameterIndex),
                value));
    }

    public void LogParameterStore(ushort value, int parameterIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteUInt16(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterStore, MaxCompressedIntegerSize + sizeof(ushort)),
                    parameterIndex),
                value));
    }

    public void LogParameterStore(uint value, int parameterIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteUInt32(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterStore, MaxCompressedIntegerSize + sizeof(uint)),
                    parameterIndex),
                value));
    }

    public void LogParameterStore(ulong value, int parameterIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteUInt64(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterStore, MaxCompressedIntegerSize + sizeof(ulong)),
                    parameterIndex),
                value));
    }

    public void LogParameterStore(float value, int parameterIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteFloat(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterStore, MaxCompressedIntegerSize + sizeof(float)),
                    parameterIndex),
                value));
    }

    public void LogParameterStore(double value, int parameterIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteDouble(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterStore, MaxCompressedIntegerSize + sizeof(double)),
                    parameterIndex),
                value));
    }

    public void LogParameterStore(decimal value, int parameterIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteDecimal(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterStore, MaxCompressedIntegerSize + sizeof(decimal)),
                    parameterIndex),
                value));
    }

    public void LogParameterStore(string value, int parameterIndex)
    {
        var size = GetSerializedStringSize(value);
        var data = _data;
        data.EndRecord(
            WriteString(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterStore, MaxCompressedIntegerSize + size),
                    parameterIndex),
                value,
                size));
    }

    public void LogParameterStore(void* value, int parameterIndex)
    {
        var data = _data;
        data.EndRecord(
            WriteAddress(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterStore, MaxCompressedIntegerSize + sizeof(void*)),
                    parameterIndex),
                value));
    }

    public void LogLocalStore(object value, int localIndex)
        => WriteUntypedStore(RecordKind.UntypedLocalStore_Base, value, localIndex);

    public void LogParameterStore(object value, int parameterIndex)
        => WriteUntypedStore(RecordKind.UntypedParameterStore_Base, value, parameterIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteUntypedStore(RecordKind kindBase, object value, int localOrParameterIndex)
    {
        // Note: Value of Nullable<T> is boxed as type T. We do not need to explicitly handle nullable types below.

        var data = _data;

        var position =
            (value is null) ? data.WriteRecordHeader(kindBase + (int)TypeCode.Null, payloadMaxSize: VariableIdSize) :
            (value.GetType() == typeof(int)) ? data.WriteRecordHeaderAndValue(kindBase, (int)value, VariableIdSize) :
            (value.GetType() == typeof(bool)) ? data.WriteRecordHeaderAndValue(kindBase, (bool)value, VariableIdSize) :
            (value.GetType() == typeof(string)) ? data.WriteRecordHeaderAndValue(kindBase, (string)value, VariableIdSize) :
            (value.GetType() == typeof(double)) ? data.WriteRecordHeaderAndValue(kindBase, (double)value, VariableIdSize) :
            (value.GetType() == typeof(float)) ? data.WriteRecordHeaderAndValue(kindBase, (float)value, VariableIdSize) :
            (value.GetType() == typeof(char)) ? data.WriteRecordHeaderAndValue(kindBase, (char)value, VariableIdSize) :
            (value.GetType() == typeof(byte)) ? data.WriteRecordHeaderAndValue(kindBase, (byte)value, VariableIdSize) :
            (value.GetType() == typeof(long)) ? data.WriteRecordHeaderAndValue(kindBase, (long)value, VariableIdSize) :
            (value.GetType() == typeof(decimal)) ? data.WriteRecordHeaderAndValue(kindBase, (decimal)value, VariableIdSize) :
            (value.GetType() == typeof(uint)) ? data.WriteRecordHeaderAndValue(kindBase, (uint)value, VariableIdSize) :
            (value.GetType() == typeof(short)) ? data.WriteRecordHeaderAndValue(kindBase, (short)value, VariableIdSize) :
            (value.GetType() == typeof(sbyte)) ? data.WriteRecordHeaderAndValue(kindBase, (sbyte)value, VariableIdSize) :
            (value.GetType() == typeof(ulong)) ? data.WriteRecordHeaderAndValue(kindBase, (ulong)value, VariableIdSize) :
            (value.GetType() == typeof(ushort)) ? data.WriteRecordHeaderAndValue(kindBase, (ushort)value, VariableIdSize) :
            value.GetType().IsEnum ? data.WriteRecordHeaderAndEnumValue(kindBase, value) : data.WriteRecordHeaderAndObjectValue(kindBase, value);

        data.EndRecord(WriteCompressedInteger(position, localOrParameterIndex));
    }

    private static int GetSerializedStringSize(string value)
        => Math.Min(value.Length, MaxSerializedStringLength) * sizeof(char);

    public void LogLocalStoreUnmanaged(void* address, int size, int localIndex)
    {
        var data = _data;

        data.EndRecord(
            WriteMemory(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.LocalUnmanagedStore, MaxCompressedIntegerSize + size),
                    localIndex),
                address,
                size));
    }

    public void LogParameterStoreUnmanaged(void* address, int size, int parameterIndex)
    {
        var data = _data;

        data.EndRecord(
            WriteMemory(
                WriteCompressedInteger(
                    data.WriteRecordHeader(RecordKind.ParameterUnmanagedStore, MaxCompressedIntegerSize + size),
                    parameterIndex),
                address,
                size));
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
        var data = _data;
        data.EndRecord(
            WriteCompressedInteger(
                WriteCompressedInteger(
                    data.WriteRecordHeader(kind, 2 * MaxCompressedIntegerSize),
                    targetIndex),
                sourceIndex));
    }
}
