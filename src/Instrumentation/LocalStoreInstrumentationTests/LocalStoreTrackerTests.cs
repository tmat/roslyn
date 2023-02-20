using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.Decompiler.CSharp.Syntax;
using Roslyn.Test.Utilities;
using Xunit;

using RecordKind = Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.RecordKind;
using TypeCode = Microsoft.CodeAnalysis.Runtime.LocalStoreTracker.TypeCode;

namespace Microsoft.CodeAnalysis.Runtime.UnitTests;

public unsafe class LocalStoreTrackerTests : IDisposable
{
    private static readonly Dictionary<Type, Func<nint, (object value, int size)>> s_deserializers = new();

    public void Dispose()
    {
        LocalStoreTracker.s_threadData = null;
    }

    public static BlobReader GetReader()
    {
        var bufferSize = 4 * 1024;
        var threadData = new TestThreadData()
        {
            // buffer should be big enough for the test scenario
            BufferFullImpl = (_, _, _, _) => Assert.False(true)
        };

        LocalStoreTracker.s_threadData = threadData;
        return new BlobReader(threadData.BufferPointer, bufferSize);
    }

    private static (object? value, int size) DeserializeUnmanagedType<T>(nint data) // not unmanaged to allow for Nullable<T>
    {
        var size = Unsafe.SizeOf<T>();
        var value = default(T);
        Unsafe.Copy(ref value, (void*)data);
        return (value, size);
    }

    private static Func<nint, (object value, int size)> GetDeserializer(Type type)
    {
        lock (s_deserializers)
        {
            if (s_deserializers.TryGetValue(type, out var deserializer))
            {
                return deserializer;
            }

            var method = typeof(LocalStoreTrackerTests).GetMethod(nameof(DeserializeUnmanagedType), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type);
            deserializer = method.CreateDelegate<Func<nint, (object value, int size)>>();
            s_deserializers.Add(type, deserializer);
            return deserializer;
        }
    }

    private static object ReadUnmanagedType(ref BlobReader reader, Type type)
    {
        var deserializer = GetDeserializer(type);
        var (value, size) = deserializer((nint)reader.CurrentPointer);
        reader.Offset += size;
        return value;
    }

    private static string Inspect(object? value)
        => value?.ToString() ?? "null";

    private string[] ReadRecords(BlobReader reader, Type[] localSignature, Type[] parameterSignature)
    {
        // metadata reader assumes little-endian:
        Debug.Assert(BitConverter.IsLittleEndian);

        reader.Reset();

        var list = new List<string>();
        var stack = new List<int>();
        var currentFrame = 0;

        while (true)
        {
            var recordKind = (RecordKind)reader.ReadCompressedInteger();
            var recordDisplay = recordKind switch
            {
                RecordKind.EndOfData => null,
                RecordKind.Return => ReadFrameReturn() + "Returned",
                RecordKind.MethodEntry => ReadFrameEntry() + "Entered",
                RecordKind.LambdaEntry => ReadMethodEntry(out var methodId) + ReadFrameEntry() + $"Entered lambda in method {methodId:X}",
                RecordKind.StateMachineMethodEntry => ReadFrameEntry() + $"Entered state machine #{ReadCompressedLongIntenger(ref reader)}",
                RecordKind.StateMachineLambdaEntry => ReadMethodEntry(out var methodId) + ReadFrameEntry() + $"Entered lambda state machine #{ReadCompressedLongIntenger(ref reader)} in method {methodId:X}",
                RecordKind.LocalAliasStoreToLocal => ReadAlias(target: 'L', source: 'L'),
                RecordKind.ParameterAliasStoreToLocal => ReadAlias(target: 'L', source: 'P'),
                RecordKind.ParameterAliasStoreToParameter => ReadAlias(target: 'P', source: 'P'),
                RecordKind.ParameterUnmanagedStore => ReadUnmanaged(isLocal: false),
                RecordKind.LocalUnmanagedStore => ReadUnmanaged(isLocal: true),
                >= RecordKind.UntypedLocalStore_Base and <= RecordKind.UntypedLocalStore_Max => ReadUntyped(isLocal: true),
                >= RecordKind.UntypedParameterStore_Base and <= RecordKind.UntypedParameterStore_Max => ReadUntyped(isLocal: false),
                RecordKind.ParameterStore => ReadTyped(isLocal: false),
                >= RecordKind.LocalStore_Base => ReadTyped(isLocal: true),
                _ => throw new InvalidOperationException($"Unexpected record kind: {recordKind}"),
            };

            if (recordDisplay == null)
            {
                break;
            }

            list.Add($"{currentFrame:X}: {recordDisplay}");

            string ReadMethodEntry(out int methodId)
            {
                methodId = reader.ReadCompressedInteger();
                return "";
            }

            string ReadFrameEntry()
            {
                currentFrame = reader.ReadCompressedInteger();
                stack.Add(currentFrame);
                return "";
            }

            string ReadFrameReturn()
            {
                currentFrame = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                return "";
            }

            string ReadAlias(char target, char source)
            {
                var targetLocalIndex = reader.ReadCompressedInteger();
                var sourceLocalIndex = reader.ReadCompressedInteger();
                return $"{target}{targetLocalIndex} -> {source}{sourceLocalIndex}";
            }

            string ReadUnmanaged(bool isLocal)
            {
                var index = reader.ReadCompressedInteger();
                var (type, display) = GetVariableTypeAndDisplay(index, isLocal);

                // TODO: we need instantiation token if the type is parameterized from surrounding generic context
                var value = ReadUnmanagedType(ref reader, type);

                return $"{display} = {Inspect(value)}";
            }

            string ReadUntyped(bool isLocal)
            {
                var typeCode = (TypeCode)(recordKind - (isLocal ? RecordKind.UntypedLocalStore_Base : RecordKind.UntypedParameterStore_Base));
                var value = ReadValue(ref reader, typeCode);
                var index = reader.ReadCompressedInteger();
                var (_, display) = GetVariableTypeAndDisplay(index, isLocal);

                return $"{display} = {Inspect(value.value)} ({Inspect(value.type)})";
            }

            string ReadTyped(bool isLocal)
            {
                var index = isLocal ? recordKind - RecordKind.LocalStore_Base : reader.ReadCompressedInteger();
                var (type, display) = GetVariableTypeAndDisplay(index, isLocal);
                var value = ReadValue(ref reader, type);

                return $"{display} = {Inspect(value)}";
            }

            (Type type, string display) GetVariableTypeAndDisplay(int index, bool isLocal)
            {
                if (index >= LocalStoreTracker.LiftedVariableBaseIndex)
                {
                    var fieldToken = (int)TableIndex.Field << 24 | (index - LocalStoreTracker.LiftedVariableBaseIndex);
                    var field = typeof(LocalStoreTrackerTests).Assembly.ManifestModule.ResolveField(fieldToken)!;

                    return (field.FieldType, $"{(isLocal ? "L" : "P")}'{field.Name}'");
                }

                return isLocal ? (localSignature[index], $"L{index}") : (parameterSignature[index], $"P{index}");
            }
        }

        return list.ToArray();
    }

    private static string UnmangleFieldName(string name)
        => (name[0] == '<') ? name[1..name.IndexOf('>')] : name;

    private static (object? value, Type? type) ReadValue(ref BlobReader reader, TypeCode typeCode)
        => typeCode switch
        {
            TypeCode.SByte => (reader.ReadSByte(), typeof(sbyte)),
            TypeCode.Byte => (reader.ReadByte(), typeof(byte)),
            TypeCode.Int16 => (reader.ReadInt16(), typeof(short)),
            TypeCode.UInt16 => (reader.ReadUInt16(), typeof(ushort)),
            TypeCode.Int32 => (reader.ReadInt32(), typeof(int)),
            TypeCode.UInt32 => (reader.ReadUInt32(), typeof(uint)),
            TypeCode.Int64 => (reader.ReadInt64(), typeof(long)),
            TypeCode.UInt64 => (reader.ReadUInt64(), typeof(ulong)),
            TypeCode.Boolean => (reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException() }, typeof(bool)),
            TypeCode.Single => (reader.ReadSingle(), typeof(float)),
            TypeCode.Double => (reader.ReadDouble(), typeof(double)),
            TypeCode.Decimal => (ReadDecimal(ref reader), typeof(decimal)),
            TypeCode.Char => (reader.ReadChar(), typeof(char)),
            TypeCode.String => (reader.ReadUTF16(reader.ReadCompressedInteger()), typeof(string)),
            TypeCode.Object => (reader.ReadUTF16(reader.ReadCompressedInteger()), ReadType(ref reader)),
            TypeCode.Null => (null, null),
            >= TypeCode.Enum_Base and <= TypeCode.Enum_Max => ReadEnumValue(ref reader, (TypeCode)(typeCode - TypeCode.Enum_Base)),
            _ => throw new InvalidDataException()
        };

    // ECMA representation is more compressed (saves 3B), but also more complex.
    private static decimal ReadDecimal(ref BlobReader reader)
    {
        var value = *(decimal*)reader.CurrentPointer;
        reader.Offset += sizeof(decimal);
        return value;
    }

    private static ulong ReadCompressedLongIntenger(ref BlobReader reader)
    {
        var discriminator = reader.ReadByte();
        return (discriminator == 0) ? (ulong)reader.ReadCompressedInteger() : reader.ReadUInt64();
    }

    private static (object? value, Type? type) ReadEnumValue(ref BlobReader reader, TypeCode underlyingTypeCode)
    {
        var (underlyingValue, _) = ReadValue(ref reader, underlyingTypeCode);
        var enumType = ReadType(ref reader);
        return (Enum.ToObject(enumType, underlyingValue!), enumType);
    }

    private static Type ReadType(ref BlobReader reader)
    {
        var mvid = reader.ReadGuid();
        var token = reader.ReadUInt32();

        var assembly = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.ManifestModule.ModuleVersionId == mvid).Single();
        return assembly.ManifestModule.ResolveType((int)token);
    }

    private static object? ReadValue(ref BlobReader reader, Type type)
        => Type.GetTypeCode(type) switch
        {
            System.TypeCode.SByte => ToEnum(reader.ReadSByte(), type),
            System.TypeCode.Byte => ToEnum(reader.ReadByte(), type),
            System.TypeCode.Int16 => ToEnum(reader.ReadInt16(), type),
            System.TypeCode.UInt16 => ToEnum(reader.ReadUInt16(), type),
            System.TypeCode.Int32 => ToEnum(reader.ReadInt32(), type),
            System.TypeCode.UInt32 => ToEnum(reader.ReadUInt32(), type),
            System.TypeCode.Int64 => ToEnum(reader.ReadInt64(), type),
            System.TypeCode.UInt64 => ToEnum(reader.ReadUInt64(), type),
            System.TypeCode.Boolean => reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException() },
            System.TypeCode.Single => reader.ReadSingle(),
            System.TypeCode.Double => reader.ReadDouble(),
            System.TypeCode.Decimal => ReadDecimal(ref reader),
            System.TypeCode.Char => reader.ReadChar(),
            System.TypeCode.String => reader.ReadUTF16(reader.ReadCompressedInteger()),
            _ when type.IsPointer => new nint((void*)(IntPtr.Size == 4 ? reader.ReadUInt32() : reader.ReadUInt64())),
            _ => throw new InvalidDataException()
        };

    private static object ToEnum(object value, Type type)
        => type.IsEnum ? Enum.ToObject(type, value) : value;

    private enum EByte : byte { A = 1 }
    private enum ESByte : sbyte { A = 1 }
    private enum EShort : short { A = 1 }
    private enum EUShort : ushort { A = 1 }
    private enum EInt : int { A = 1 }
    private enum EUInt : uint { A = 1 }
    private enum ELong : long { A = 1 }
    private enum EULong : ulong { A = 1 }

    public static IEnumerable<object?[]> GetObjects()
    {
        yield return new object?[] { sbyte.MaxValue };
        yield return new object?[] { byte.MaxValue };
        yield return new object?[] { short.MaxValue };
        yield return new object?[] { ushort.MaxValue };
        yield return new object?[] { int.MaxValue };
        yield return new object?[] { uint.MaxValue };
        yield return new object?[] { long.MaxValue };
        yield return new object?[] { ulong.MaxValue };
        yield return new object?[] { true };
        yield return new object?[] { double.NaN };
        yield return new object?[] { decimal.MaxValue };
        yield return new object?[] { float.NegativeInfinity };
        yield return new object?[] { 'x' };
        yield return new object?[] { "str" };
        yield return new object?[] { (nuint)123 };
        yield return new object?[] { new object() };
        yield return new object?[] { new Version(1, 2, 3, 4) };
        yield return new object?[] { GetExceptionWithStackTrace(new ArgumentOutOfRangeException("*param*", "*message*")) };
        yield return new object?[] { null };
        yield return new object?[] { (int?)123 };
    }

    private static Exception GetExceptionWithStackTrace(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private class Closure<T>
    {
        public Closure(T t)
        {
            a__14 = t;
        }

        // primitive:
        public bool a__1 = true;
        public sbyte a__2 = sbyte.MaxValue;
        public byte a__3 = byte.MaxValue;
        public char a__4 = 'x';
        public short a__5 = short.MaxValue;
        public ushort a__6 = ushort.MaxValue;
        public uint a__7 = uint.MaxValue;
        public long a__8 = long.MaxValue;
        public ulong a__9 = ulong.MaxValue;
        public string a__10 = "str";
        public EByte a__11 = EByte.A;

        // unmanaged:
        public nint a__12 = 0x12345678;
        public nint? a__13 = 0xABCDEF;
        public T a__14;

        // untyped:
        public object a__15 = new Tuple<string, int>("a", 1);
        public object a__16 = EULong.A;
    }

    private static byte ToLoggerType(sbyte value) => unchecked((byte)value);
    private static byte ToLoggerType(byte value) => value;
    private static ushort ToLoggerType(short value) => unchecked((ushort)value);
    private static ushort ToLoggerType(ushort value) => value;
    private static uint ToLoggerType(int value) => unchecked((uint)value);
    private static uint ToLoggerType(uint value) => value;
    private static ulong ToLoggerType(long value) => unchecked((ulong)value);
    private static ulong ToLoggerType(ulong value) => value;
    private static byte ToLoggerType(ESByte value) => (byte)value;
    private static byte ToLoggerType(EByte value) => (byte)value;
    private static ushort ToLoggerType(EShort value) => (ushort)value;
    private static ushort ToLoggerType(EUShort value) => (ushort)value;
    private static uint ToLoggerType(EInt value) => (uint)value;
    private static uint ToLoggerType(EUInt value) => (uint)value;
    private static ulong ToLoggerType(ELong value) => (ulong)value;
    private static ulong ToLoggerType(EULong value) => (ulong)value;
    private static void* ToLoggerType(void* value) => value;
    private static void* ToLoggerType(nint value) => (void*)value;
    private static void* ToLoggerType(nuint value) => (void*)value;
    private static bool ToLoggerType(bool value) => value;
    private static char ToLoggerType(char value) => value;
    private static float ToLoggerType(float value) => value;
    private static double ToLoggerType(double value) => value;
    private static decimal ToLoggerType(decimal value) => value;
    private static string ToLoggerType(string value) => value;
    private static object ToLoggerType(object value) => value;

    [Fact]
    public void Invariants()
    {
        const int MaxCompressedIntegerValue = 0x1fffffff;
        const int MaxFieldRowId = 0xffffff;

        // the value used by the tracker matches the value used by the compiler:
        // TODO: Assert.Equal(LocalStoreTracker.LiftedVariableBaseIndex, Cci.MetadataWriter.LiftedVariableBaseIndex);

        // lifted local index shall not overlap with an index of non-lifted variable:
        Assert.True(LocalStoreTracker.LiftedVariableBaseIndex >= LocalStoreTracker.MaxLocalVariableCount);

        // RecordKind fits into compressed integer:
        Assert.True((int)RecordKind.LocalStore_Base + LocalStoreTracker.LiftedVariableBaseIndex + MaxFieldRowId <= MaxCompressedIntegerValue);
    }

    [Theory]
    [CombinatorialData]
    public void LogLocalStore(bool isLocal)
    {
        var reader = GetReader();

        var methodId = 0xABCDEF;
        var tracker = LocalStoreTracker.LogMethodEntry(methodId);

        var v0 = true;
        var v1 = isLocal ? sbyte.MinValue : sbyte.MaxValue;
        var v2 = isLocal ? byte.MaxValue : byte.MinValue;
        var v3 = 'x';
        var v4 = isLocal ? short.MaxValue : short.MinValue;
        var v5 = isLocal ? ushort.MaxValue : ushort.MinValue;
        var v6 = isLocal ? int.MaxValue : int.MinValue;
        var v7 = isLocal ? uint.MaxValue : uint.MinValue;
        var v8 = isLocal ? long.MaxValue : long.MinValue;
        var v9 = isLocal ? ulong.MaxValue : ulong.MinValue;
        var v10 = "paired surrogates:\uD800\uDC00 low:\uD800 high:\uDC00 null:\0";
        var v11 = &methodId;

        if (isLocal)
        {
            tracker.LogLocalStore(ToLoggerType(v0), 0);
            tracker.LogLocalStore(ToLoggerType(v1), 1);
            tracker.LogLocalStore(ToLoggerType(v2), 2);
            tracker.LogLocalStore(ToLoggerType(v3), 3);
            tracker.LogLocalStore(ToLoggerType(v4), 4);
            tracker.LogLocalStore(ToLoggerType(v5), 5);
            tracker.LogLocalStore(ToLoggerType(v6), 6);
            tracker.LogLocalStore(ToLoggerType(v7), 7);
            tracker.LogLocalStore(ToLoggerType(v8), 8);
            tracker.LogLocalStore(ToLoggerType(v9), 9);
            tracker.LogLocalStore(ToLoggerType(v10), 10);
            tracker.LogLocalStore(ToLoggerType(v11), 11);
        }
        else
        {
            tracker.LogParameterStore(ToLoggerType(v0), 0);
            tracker.LogParameterStore(ToLoggerType(v1), 1);
            tracker.LogParameterStore(ToLoggerType(v2), 2);
            tracker.LogParameterStore(ToLoggerType(v3), 3);
            tracker.LogParameterStore(ToLoggerType(v4), 4);
            tracker.LogParameterStore(ToLoggerType(v5), 5);
            tracker.LogParameterStore(ToLoggerType(v6), 6);
            tracker.LogParameterStore(ToLoggerType(v7), 7);
            tracker.LogParameterStore(ToLoggerType(v8), 8);
            tracker.LogParameterStore(ToLoggerType(v9), 9);
            tracker.LogParameterStore(ToLoggerType(v10), 10);
            tracker.LogParameterStore(ToLoggerType(v11), 11);
        }

        var types = new[]
        {
            v0.GetType(),
            v1.GetType(),
            v2.GetType(),
            v3.GetType(),
            v4.GetType(),
            v5.GetType(),
            v6.GetType(),
            v7.GetType(),
            v8.GetType(),
            v9.GetType(),
            v10.GetType(),
            typeof(int*),
        };

        var lp = isLocal ? 'L' : 'P';

        AssertEx.Equal(new[]
        {
            $"ABCDEF: Entered",
            $"ABCDEF: {lp}0 = {v0}",
            $"ABCDEF: {lp}1 = {v1}",
            $"ABCDEF: {lp}2 = {v2}",
            $"ABCDEF: {lp}3 = {v3}",
            $"ABCDEF: {lp}4 = {v4}",
            $"ABCDEF: {lp}5 = {v5}",
            $"ABCDEF: {lp}6 = {v6}",
            $"ABCDEF: {lp}7 = {v7}",
            $"ABCDEF: {lp}8 = {v8}",
            $"ABCDEF: {lp}9 = {v9}",
            $"ABCDEF: {lp}10 = {v10}",
            $"ABCDEF: {lp}11 = {(nint)v11}",
        }, ReadRecords(reader, localSignature: types, parameterSignature: types));
    }

    [Theory]
    [CombinatorialData]
    public void LogLocalStore_Floats(bool isLocal)
    {
        var reader = GetReader();

        var methodId = 0xABCDEF;
        var tracker = LocalStoreTracker.LogMethodEntry(methodId);

        var v1 = isLocal ? float.MaxValue : float.MinValue;
        var v2 = isLocal ? double.MaxValue : double.MinValue;
        var v3 = isLocal ? decimal.MaxValue : decimal.MinValue;

        if (isLocal)
        {
            tracker.LogLocalStore(ToLoggerType(v1), 0);
            tracker.LogLocalStore(ToLoggerType(v2), 1);
            tracker.LogLocalStore(ToLoggerType(v3), 2);
        }
        else
        {
            tracker.LogParameterStore(ToLoggerType(v1), 0);
            tracker.LogParameterStore(ToLoggerType(v2), 1);
            tracker.LogParameterStore(ToLoggerType(v3), 2);
        }

        var types = new[]
        {
            v1.GetType(),
            v2.GetType(),
            v3.GetType(),
        };

        var lp = isLocal ? 'L' : 'P';

        AssertEx.Equal(new[]
        {
            $"ABCDEF: Entered",
            $"ABCDEF: {lp}0 = {v1}",
            $"ABCDEF: {lp}1 = {v2}",
            $"ABCDEF: {lp}2 = {v3}",
        }, ReadRecords(reader, localSignature: types, parameterSignature: types));
    }

    [Theory]
    [CombinatorialData]
    public void LogLocalStore_Enums(bool isLocal)
    {
        var reader = GetReader();

        var methodId = 1;
        var tracker = LocalStoreTracker.LogMethodEntry(methodId);

        if (isLocal)
        {
            tracker.LogLocalStore(ToLoggerType(EByte.A), 0);
            tracker.LogLocalStore(ToLoggerType(ESByte.A), 1);
            tracker.LogLocalStore(ToLoggerType(EShort.A), 2);
            tracker.LogLocalStore(ToLoggerType(EUShort.A), 3);
            tracker.LogLocalStore(ToLoggerType(EInt.A), 4);
            tracker.LogLocalStore(ToLoggerType(EUInt.A), 5);
            tracker.LogLocalStore(ToLoggerType(ELong.A), 6);
            tracker.LogLocalStore(ToLoggerType(EULong.A), 7);
        }
        else
        {
            tracker.LogParameterStore(ToLoggerType(EByte.A), 0);
            tracker.LogParameterStore(ToLoggerType(ESByte.A), 1);
            tracker.LogParameterStore(ToLoggerType(EShort.A), 2);
            tracker.LogParameterStore(ToLoggerType(EUShort.A), 3);
            tracker.LogParameterStore(ToLoggerType(EInt.A), 4);
            tracker.LogParameterStore(ToLoggerType(EUInt.A), 5);
            tracker.LogParameterStore(ToLoggerType(ELong.A), 6);
            tracker.LogParameterStore(ToLoggerType(EULong.A), 7);
        }

        var types = new[]
        {
            typeof(EByte),
            typeof(ESByte),
            typeof(EShort),
            typeof(EUShort),
            typeof(EInt),
            typeof(EUInt),
            typeof(ELong),
            typeof(EULong),
        };

        var lp = isLocal ? 'L' : 'P';

        AssertEx.Equal(new[]
        {
            "1: Entered",
            $"1: {lp}0 = A",
            $"1: {lp}1 = A",
            $"1: {lp}2 = A",
            $"1: {lp}3 = A",
            $"1: {lp}4 = A",
            $"1: {lp}5 = A",
            $"1: {lp}6 = A",
            $"1: {lp}7 = A",
        }, ReadRecords(reader, localSignature: types, parameterSignature: types));
    }

    [Theory]
    [CombinatorialData]
    public void LogLocalStoreUnmanaged(bool isLocal)
    {
        var reader = GetReader();

        var methodId = 1;
        var tracker = LocalStoreTracker.LogMethodEntry(methodId);

        var v0 = Half.Pi;
        var v1 = (int?)123;
        var v2 = (int?)null;
        var v3 = (DateTime.Now, (int?)1, new Int128(1, 2));

        var s0 = sizeof(Half);
        var s1 = sizeof(int?);
        var s2 = sizeof(int?);
        var s3 = sizeof((DateTime, int?, Int128));

        if (isLocal)
        {
            tracker.LogLocalStoreUnmanaged(&v0, s0, 0);
            tracker.LogLocalStoreUnmanaged(&v1, s1, 1);
            tracker.LogLocalStoreUnmanaged(&v2, s2, 2);
            tracker.LogLocalStoreUnmanaged(&v3, s3, 3);
        }
        else
        {
            tracker.LogParameterStoreUnmanaged(&v0, s0, 0);
            tracker.LogParameterStoreUnmanaged(&v1, s1, 1);
            tracker.LogParameterStoreUnmanaged(&v2, s2, 2);
            tracker.LogParameterStoreUnmanaged(&v3, s3, 3);
        }

        var types = new[] { typeof(Half), typeof(int?), typeof(int?), typeof((DateTime, int?, Int128)) };
        var lp = isLocal ? 'L' : 'P';

        AssertEx.Equal(new[]
        {
            "1: Entered",
            $"1: {lp}0 = 3.14",
            $"1: {lp}1 = 123",
            $"1: {lp}2 = null",
            $"1: {lp}3 = {v3}"
        }, ReadRecords(reader, localSignature: types, parameterSignature: types));
    }

    [Theory]
    [MemberData(nameof(GetObjects))]
    public void LogLocalStoreUntyped(object value)
    {
        value = decimal.MaxValue;

        var reader = GetReader();

        var methodId = 1;
        var tracker = LocalStoreTracker.LogMethodEntry(methodId);

        tracker.LogLocalStore(value, 0);

        var valueStr = value is Exception e ? e.Message : Inspect(value);
        var types = new[] { typeof(object) };

        AssertEx.Equal(new[]
        {
            $"1: Entered",
            $"1: L0 = {valueStr} ({Inspect(value?.GetType())})",
        }, ReadRecords(reader, localSignature: types, parameterSignature: types));
    }

    [Theory]
    [MemberData(nameof(GetObjects))]
    public void LogParameterStoreUntyped(object value)
    {
        var reader = GetReader();

        var methodId = 1;
        var tracker = LocalStoreTracker.LogMethodEntry(methodId);

        tracker.LogParameterStore(value, 0);

        var valueStr = value is Exception e ? e.Message : Inspect(value);
        var types = new[] { typeof(object) };

        AssertEx.Equal(new[]
        {
            $"1: Entered",
            $"1: P0 = {valueStr} ({Inspect(value?.GetType())})",
        }, ReadRecords(reader, localSignature: types, parameterSignature: types));
    }

    [Theory]
    [CombinatorialData]
    public void LogStoreUntypedEnums([CombinatorialValues(EByte.A, ESByte.A, EShort.A, EUShort.A, EInt.A, EUInt.A, ELong.A, EULong.A)] object value, bool isLocal)
    {
        var reader = GetReader();

        var methodId = 1;
        var tracker = LocalStoreTracker.LogMethodEntry(methodId);

        if (isLocal)
        {
            tracker.LogLocalStore(value, 0);
        }
        else
        {
            tracker.LogParameterStore(value, 0);
        }

        var lp = isLocal ? "L" : "P";
        var types = new[] { typeof(object) };

        AssertEx.Equal(new[]
        {
            $"1: Entered",
            $"1: {lp}0 = A ({value.GetType()})",
        }, ReadRecords(reader, localSignature: types, parameterSignature: types));
    }

    [Fact]
    public void LogAlias()
    {
        var reader = GetReader();

        var methodId = 1;
        var tracker = LocalStoreTracker.LogMethodEntry(methodId);

        tracker.LogLocalStoreLocalAlias(0, 1);
        tracker.LogLocalStoreParameterAlias(2, 3);
        tracker.LogParameterStoreParameterAlias(5, 6);

        AssertEx.Equal(new[]
        {
            "1: Entered",
            "1: L1 -> L0",
            "1: L3 -> P2",
            "1: P6 -> P5"
        }, ReadRecords(reader, localSignature: Array.Empty<Type>(), parameterSignature: Array.Empty<Type>()));
    }

    [Theory]
    [CombinatorialData]
    public void LogLocalStore_Lifted(bool isLocal)
    {
        var reader = GetReader();

        var methodId = 1;
        var lambdaId = 2;
        var tracker = LocalStoreTracker.LogLambdaEntry(methodId, lambdaId);

        var closure = new Closure<DateTime>(DateTime.Now);

        int GetLiftedIndex(int ordinal)
        {
            var token = closure.GetType().GetField($"a__{ordinal}")!.MetadataToken;
            return (token & 0x00FFFFFF) + LocalStoreTracker.LiftedVariableBaseIndex;
        }

        static int SizeOf<T>(ref T _)
            => Unsafe.SizeOf<T>();

        if (isLocal)
        {
            // primitive:
            tracker.LogLocalStore(ToLoggerType(closure.a__1), GetLiftedIndex(1));
            tracker.LogLocalStore(ToLoggerType(closure.a__2), GetLiftedIndex(2));
            tracker.LogLocalStore(ToLoggerType(closure.a__3), GetLiftedIndex(3));
            tracker.LogLocalStore(ToLoggerType(closure.a__4), GetLiftedIndex(4));
            tracker.LogLocalStore(ToLoggerType(closure.a__5), GetLiftedIndex(5));
            tracker.LogLocalStore(ToLoggerType(closure.a__6), GetLiftedIndex(6));
            tracker.LogLocalStore(ToLoggerType(closure.a__7), GetLiftedIndex(7));
            tracker.LogLocalStore(ToLoggerType(closure.a__8), GetLiftedIndex(8));
            tracker.LogLocalStore(ToLoggerType(closure.a__9), GetLiftedIndex(9));
            tracker.LogLocalStore(ToLoggerType(closure.a__10), GetLiftedIndex(10));
            tracker.LogLocalStore(ToLoggerType(closure.a__11), GetLiftedIndex(11));

            // unmanaged:
            tracker.LogLocalStoreUnmanaged(Unsafe.AsPointer(ref closure.a__12), SizeOf(ref closure.a__12), GetLiftedIndex(12));
            tracker.LogLocalStoreUnmanaged(Unsafe.AsPointer(ref closure.a__13), SizeOf(ref closure.a__13), GetLiftedIndex(13));
            // TODO: generic tracker.LogLocalStoreUnmanaged(Unsafe.AsPointer(ref closure.a__14), SizeOf(ref closure.a__14), GetLiftedIndex(14));

            // untyped:
            tracker.LogLocalStore(ToLoggerType(closure.a__15), GetLiftedIndex(15));
            tracker.LogLocalStore(ToLoggerType(closure.a__16), GetLiftedIndex(16));
        }
        else
        {
            // primitive:
            tracker.LogParameterStore(ToLoggerType(closure.a__1), GetLiftedIndex(1));
            tracker.LogParameterStore(ToLoggerType(closure.a__2), GetLiftedIndex(2));
            tracker.LogParameterStore(ToLoggerType(closure.a__3), GetLiftedIndex(3));
            tracker.LogParameterStore(ToLoggerType(closure.a__4), GetLiftedIndex(4));
            tracker.LogParameterStore(ToLoggerType(closure.a__5), GetLiftedIndex(5));
            tracker.LogParameterStore(ToLoggerType(closure.a__6), GetLiftedIndex(6));
            tracker.LogParameterStore(ToLoggerType(closure.a__7), GetLiftedIndex(7));
            tracker.LogParameterStore(ToLoggerType(closure.a__8), GetLiftedIndex(8));
            tracker.LogParameterStore(ToLoggerType(closure.a__9), GetLiftedIndex(9));
            tracker.LogParameterStore(ToLoggerType(closure.a__10), GetLiftedIndex(10));
            tracker.LogParameterStore(ToLoggerType(closure.a__11), GetLiftedIndex(11));

            // unmanaged:
            tracker.LogParameterStoreUnmanaged(Unsafe.AsPointer(ref closure.a__12), SizeOf(ref closure.a__12), GetLiftedIndex(12));
            tracker.LogParameterStoreUnmanaged(Unsafe.AsPointer(ref closure.a__13), SizeOf(ref closure.a__13), GetLiftedIndex(13));
            // TODO: generic tracker.LogParameterStoreUnmanaged(Unsafe.AsPointer(ref closure.a__14), SizeOf(ref closure.a__14), GetLiftedIndex(14));

            // untyped:
            tracker.LogParameterStore(ToLoggerType(closure.a__15), GetLiftedIndex(15));
            tracker.LogParameterStore(ToLoggerType(closure.a__16), GetLiftedIndex(16));
        }

        var lp = isLocal ? "L" : "P";

        AssertEx.Equal(new[]
        {
            $"2: Entered lambda in method 1",
            $"2: {lp}'a__1' = {closure.a__1}",
            $"2: {lp}'a__2' = {closure.a__2}",
            $"2: {lp}'a__3' = {closure.a__3}",
            $"2: {lp}'a__4' = {closure.a__4}",
            $"2: {lp}'a__5' = {closure.a__5}",
            $"2: {lp}'a__6' = {closure.a__6}",
            $"2: {lp}'a__7' = {closure.a__7}",
            $"2: {lp}'a__8' = {closure.a__8}",
            $"2: {lp}'a__9' = {closure.a__9}",
            $"2: {lp}'a__10' = {closure.a__10}",
            $"2: {lp}'a__11' = {closure.a__11}",
            $"2: {lp}'a__12' = {closure.a__12}",
            $"2: {lp}'a__13' = {closure.a__13}",
            $"2: {lp}'a__15' = {closure.a__15} (System.Tuple`2[T1,T2])", // TODO: generic inst
            $"2: {lp}'a__16' = {closure.a__16} ({closure.a__16.GetType()})",
        }, ReadRecords(reader, localSignature: Array.Empty<Type>(), parameterSignature: Array.Empty<Type>()));
    }
}
