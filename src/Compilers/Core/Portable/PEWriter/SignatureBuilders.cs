// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Cci;

namespace System.Reflection.Metadata.Ecma335
{
    internal interface ISignatureBuilder
    {
        BlobBuilder Builder { get; }
    }

    internal struct SignatureBuilder : ISignatureBuilder
    {
        public BlobBuilder Builder { get; }

        public SignatureBuilder(BlobBuilder builder)
        {
            Builder = builder;
        }

        public CustomModifiersBuilder<SignatureTypeBuilder<SignatureBuilder>> FieldSignature()
        {
            Builder.WriteByte((byte)SignatureKind.Field);
            return new CustomModifiersBuilder<SignatureTypeBuilder<SignatureBuilder>>(
                new SignatureTypeBuilder<SignatureBuilder>(this));
        }

        public GenericTypeArgumentsBuilder<SignatureBuilder> MethodSpecificationSignature(int genericArgumentCount)
        {
            Builder.WriteByte((byte)SignatureKind.MethodSpecification);
            Builder.WriteCompressedInteger((uint)genericArgumentCount);

            return new GenericTypeArgumentsBuilder<SignatureBuilder>(this, genericArgumentCount);
        }

        public CustomModifiersBuilder<ReturnTypeBuilder<ParametersBuilder<SignatureBuilder>>> MethodDefinitionSignature(byte header, int genericParameterCount, int parameterCount)
        {
            // TODO: header
            Builder.WriteByte(header);

            if (genericParameterCount > 0)
            {
                Builder.WriteCompressedInteger((uint)genericParameterCount);
            }

            Builder.WriteCompressedInteger((uint)parameterCount);

            return new CustomModifiersBuilder<ReturnTypeBuilder<ParametersBuilder<SignatureBuilder>>>(
                new ReturnTypeBuilder<ParametersBuilder<SignatureBuilder>>(
                    new ParametersBuilder<SignatureBuilder>(this, parameterCount)));
        }

        public CustomAttributeSignatureBuilder<SignatureBuilder> CustomAttributeSignature()
        {
            Builder.WriteUInt16(0x0001);
            return new CustomAttributeSignatureBuilder<SignatureBuilder>(this);
        }

        public PermissionSetBuilder<SignatureBuilder> PermissionSetBlob(int attributeCount)
        {
            Builder.WriteByte((byte)'.');
            Builder.WriteCompressedInteger((uint)attributeCount);

            return new PermissionSetBuilder<SignatureBuilder>(this, attributeCount);
        }

        public NamedArgumentsBuilder<SignatureBuilder> NamedArgumentsSignatureBuilder(int argumentCount)
        {
            return new NamedArgumentsBuilder<SignatureBuilder>(this, argumentCount, CountFormat.Compressed);
        }

        public LocalVariablesBuilder<SignatureBuilder> LocalVariableSignatureBuilder(int count)
        {
            Builder.WriteByte((byte)SignatureKind.LocalVariables);
            Builder.WriteCompressedInteger((uint)count);
            return new LocalVariablesBuilder<SignatureBuilder>(this, count);
        }
    }

    internal struct LocalVariablesBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;
        private readonly int _count;

        public LocalVariablesBuilder(T continuation, int count)
        {
            if (count < 0)
            {
                throw new InvalidOperationException();
            }

            _count = count;
            _continuation = continuation;
        }

        public LocalVariableBuilder<LocalVariablesBuilder<T>> AddVariable()
        {
            return new LocalVariableBuilder<LocalVariablesBuilder<T>>(
                new LocalVariablesBuilder<T>(_continuation, _count - 1));
        }

        public T EndVariables()
        {
            if (_count > 0)
            {
                throw new InvalidOperationException();
            }

            return _continuation;
        }
    }

    internal struct LocalVariableBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        public LocalVariableBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public CustomModifiersBuilder<LocalVariableTypeBuilder<T>> ModifiedType()
        {
            return new CustomModifiersBuilder<LocalVariableTypeBuilder<T>>(
                new LocalVariableTypeBuilder<T>(_continuation));
        }
    }

    internal struct LocalVariableTypeBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        public LocalVariableTypeBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public SignatureTypeBuilder<T> Type(bool isPinned, bool isByRef)
        {
            if (isPinned)
            {
                Builder.WriteByte((byte)SignatureTypeCode.Pinned);
            }

            if (isByRef)
            {
                Builder.WriteByte((byte)SignatureTypeCode.ByReference);
            }

            return new SignatureTypeBuilder<T>(_continuation);
        }

        public T TypedReference()
        {
            Builder.WriteByte((byte)SignatureTypeCode.TypedReference);
            return _continuation;
        }
    }

    internal struct ParameterTypeBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        public ParameterTypeBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public SignatureTypeBuilder<T> Type(bool isByRef)
        {
            if (isByRef)
            {
                Builder.WriteByte((byte)SignatureTypeCode.ByReference);
            }

            return new SignatureTypeBuilder<T>(_continuation);
        }

        /// <summary>
        /// ECMA-335 specification only allows custom modifiers preceding BYREF marker, 
        /// however the C++ compiler emits signatures with modifiers following BYREF as well.
        /// </summary>
        public CustomModifiersBuilder<SignatureTypeBuilder<T>> ModifiedType(bool isByRef)
        {
            if (isByRef)
            {
                Builder.WriteByte((byte)SignatureTypeCode.ByReference);
            }

            return new CustomModifiersBuilder<SignatureTypeBuilder<T>>(new SignatureTypeBuilder<T>(_continuation));
        }

        public T TypedReference()
        {
            Builder.WriteByte((byte)SignatureTypeCode.TypedReference);
            return _continuation;
        }
    }

    internal struct PermissionSetBuilder<T>
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;
        private readonly int _count;

        public PermissionSetBuilder(T continuation, int count)
        {
            if (count < 0)
            {
                throw new InvalidOperationException();
            }

            _count = count;
            _continuation = continuation;
        }

        public PermissionSetBuilder<T> AddPermission(string typeName, BlobBuilder arguments)
        {
            Builder.WriteSerializedString(typeName);
            //return new NamedArgumentsBuilder<T>(_continuation, propertyCount, CountFormat.Compressed);
            Builder.WriteCompressedInteger((uint)arguments.Count);
            arguments.WriteContentTo(Builder);
            return new PermissionSetBuilder<T>(_continuation, _count - 1);
        }

        public T End()
        {
            if (_count > 0)
            {
                throw new InvalidOperationException();
            }

            return _continuation;
        }
    }

    internal struct GenericTypeArgumentsBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;
        private readonly int _count;

        internal GenericTypeArgumentsBuilder(T continuation, int count)
        {
            if (count < 0)
            {
                throw new InvalidOperationException();
            }

            _continuation = continuation;
            _count = count;
        }

        public CustomModifiersBuilder<SignatureTypeBuilder<GenericTypeArgumentsBuilder<T>>> AddArgument()
        {
            return new CustomModifiersBuilder<SignatureTypeBuilder<GenericTypeArgumentsBuilder<T>>>(
                new SignatureTypeBuilder<GenericTypeArgumentsBuilder<T>>(
                    new GenericTypeArgumentsBuilder<T>(_continuation, _count - 1)));
        }

        public T EndArguments()
        {
            if (_count > 0)
            {
                throw new InvalidOperationException();
            }

            return _continuation;
        }
    }

    internal struct CustomAttributeSignatureBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        internal CustomAttributeSignatureBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public FixedArgumentsBuilder<NamedArgumentsBuilder<T>> Arguments(int namedArgumentCount)
        {
            Builder.WriteUInt16(0x0001);
            return new FixedArgumentsBuilder<NamedArgumentsBuilder<T>>(
                new NamedArgumentsBuilder<T>(_continuation, namedArgumentCount, CountFormat.Uncompressed));
        }
    }

    internal struct FixedArgumentsBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        internal FixedArgumentsBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public ElementBuilder<FixedArgumentsBuilder<T>> AddArgument()
        {
            return new ElementBuilder<FixedArgumentsBuilder<T>>(this);
        }

        public T EndArguments()
        {
            return _continuation;
        }
    }

    internal struct ElementBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        internal ElementBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public VectorBuilder<T> Vector()
        {
            Builder.WriteByte((byte)SignatureTypeCode.SZArray);
            return new VectorBuilder<T>(_continuation);
        }

        public CustomAttributeArrayTypeBuilder<VectorBuilder<T>> TaggedVector()
        {
            Builder.WriteByte((byte)SignatureTypeCode.SZArray);
            return new CustomAttributeArrayTypeBuilder<VectorBuilder<T>>(
                new VectorBuilder<T>(_continuation));
        }

        public ScalarBuilder<T> Scalar()
        {
            return new ScalarBuilder<T>(_continuation);
        }

        public CustomAttributeElementTypeBuilder<ScalarBuilder<T>> TaggedScalar()
        {
            return new CustomAttributeElementTypeBuilder<ScalarBuilder<T>>(
                new ScalarBuilder<T>(_continuation));
        }
    }

    internal struct ScalarBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        internal ScalarBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public T NullArray()
        {
            Builder.WriteInt32(-1);
            return _continuation;
        }

        public T Constant(object value)
        {
            string str = value as string;
            if (str != null || value == null)
            {
                String(str);
            }
            else
            {
                Builder.WriteConstant(value);
            }

            return _continuation;
        }

        public T SystemType(string serializedTypeName)
        {
            String(serializedTypeName);
            return _continuation;
        }

        private T String(string value)
        {
            Builder.WriteSerializedString(value);
            return _continuation;
        }
    }

    internal struct ElementsBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;
        private readonly int _count;

        internal ElementsBuilder(T continuation, int count)
        {
            if (count < 0)
            {
                throw new InvalidOperationException();
            }

            _continuation = continuation;
            _count = count;
        }

        public ElementBuilder<ElementsBuilder<T>> AddElement()
        {
            return new ElementBuilder<ElementsBuilder<T>>(new ElementsBuilder<T>(_continuation, _count - 1));
        }

        public T EndElements()
        {
            if (_count > 0)
            {
                throw new InvalidOperationException();
            }

            return _continuation;
        }
    }

    internal struct VectorBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        internal VectorBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public ElementsBuilder<T> Count(int count)
        {
            Builder.WriteUInt32((uint)count);
            return new ElementsBuilder<T>(_continuation, count);
        }
    }
   
    internal struct NameBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        public NameBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public T Name(string name)
        {
            Builder.WriteSerializedString(name);
            return _continuation;
        }
    }

    // non-public
    internal enum CountFormat
    {
        // this is not the first attribute
        None = 0,
        // permission sets
        Compressed = 1,
        // custom attributes
        Uncompressed = 2
    }

    internal struct NamedArgumentsBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;

        private readonly T _continuation;
        private readonly int _count;
        private readonly CountFormat _countFormat;

        internal NamedArgumentsBuilder(T continuation, int count, CountFormat countFormat)
        {
            if (count < 0)
            {
                throw new InvalidOperationException();
            }

            _continuation = continuation;
            _count = count;
            _countFormat = countFormat;
        }

        public NamedArgumentTypeBuilder<NameBuilder<ElementBuilder<NamedArgumentsBuilder<T>>>> AddArgument(bool isField)
        {
            switch (_countFormat)
            {
                case CountFormat.Compressed:
                    Builder.WriteCompressedInteger((uint)_count);
                    break;

                case CountFormat.Uncompressed:
                    Builder.WriteInt32(_count);
                    break;
            }

            Builder.WriteByte(isField ? (byte)0x53 : (byte)0x54);

            return new NamedArgumentTypeBuilder<NameBuilder<ElementBuilder<NamedArgumentsBuilder<T>>>>(
                new NameBuilder<ElementBuilder<NamedArgumentsBuilder<T>>>(
                    new ElementBuilder<NamedArgumentsBuilder<T>>(
                        new NamedArgumentsBuilder<T>(_continuation, _count - 1, CountFormat.None))));
        }

        public T EndArguments()
        {
            if (_count > 0)
            {
                throw new InvalidOperationException();
            }

            return _continuation;
        }
    }

    internal struct NamedArgumentTypeBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        internal NamedArgumentTypeBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public CustomAttributeElementTypeBuilder<T> ScalarType()
        {
            return new CustomAttributeElementTypeBuilder<T>(_continuation);
        }

        public T Object()
        {
            Builder.WriteByte(0x51); // OBJECT
            return _continuation;
        }

        public CustomAttributeArrayTypeBuilder<T> SZArray()
        {
            Builder.WriteInt32((byte)SignatureTypeCode.SZArray);
            return new CustomAttributeArrayTypeBuilder<T>(_continuation);
        }
    }

    internal struct CustomAttributeArrayTypeBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;

        private readonly T _continuation;

        internal CustomAttributeArrayTypeBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public T ObjectArray()
        {
            // SZARRAY OBJECT
            return _continuation;
        }

        public CustomAttributeElementTypeBuilder<T> ElementType()
        {
            // SZARRAY 
            return new CustomAttributeElementTypeBuilder<T>(_continuation);
        }
    }

    internal struct CustomAttributeElementTypeBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;

        private readonly T _continuation;

        internal CustomAttributeElementTypeBuilder(T continuation)
        {
            _continuation = continuation;
        }

        private T WriteByte(byte value)
        {
            Builder.WriteByte(value);
            return _continuation;
        }

        public T Boolean() => WriteByte(0x02);
        public T Char() => WriteByte(0x03);
        public T Int8() => WriteByte(0x04);
        public T UInt8() => WriteByte(0x05);
        public T Int16() => WriteByte(0x06);
        public T UInt16() => WriteByte(0x07);
        public T Int32() => WriteByte(0x08);
        public T UInt32() => WriteByte(0x09);
        public T Int64() => WriteByte(0x0a);
        public T UInt64() => WriteByte(0x0b);
        public T Float32() => WriteByte(0x0c);
        public T Float64() => WriteByte(0x0d);
        public T String() => WriteByte(0x0e);
        public T IntPtr() => WriteByte(0x18);
        public T UIntPtr() => WriteByte(0x19);
               
        public T PrimitiveType(PrimitiveTypeCode type)
        {
            switch (type)
            {
                case PrimitiveTypeCode.Boolean: return Boolean();
                case PrimitiveTypeCode.Char: return Char();
                case PrimitiveTypeCode.Int8: return Int8();
                case PrimitiveTypeCode.UInt8: return UInt8();
                case PrimitiveTypeCode.Int16: return Int16();
                case PrimitiveTypeCode.UInt16: return UInt16();
                case PrimitiveTypeCode.Int32: return Int32();
                case PrimitiveTypeCode.UInt32: return UInt32();
                case PrimitiveTypeCode.Int64: return Int64();
                case PrimitiveTypeCode.UInt64: return UInt64();
                case PrimitiveTypeCode.Float32: return Float32();
                case PrimitiveTypeCode.Float64: return Float64();
                case PrimitiveTypeCode.String: return String();
                case PrimitiveTypeCode.IntPtr: return IntPtr();
                case PrimitiveTypeCode.UIntPtr: return UIntPtr();

                default:
                    throw new InvalidOperationException();
            }
        }

        public T SystemType() => WriteByte(0x50);

        public T Enum(string enumTypeName)
        {
            Builder.WriteByte(0x55);
            Builder.WriteSerializedString(enumTypeName);
            return _continuation;
        }
    }

    internal struct SignatureTypeBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        public SignatureTypeBuilder(T continuation)
        {
            _continuation = continuation;
        }

        private T WriteByte(byte value)
        {
            Builder.WriteByte(value);
            return _continuation;
        }

        private void ClassOrValue(bool isValueType)
        {
            Builder.WriteByte(isValueType ? (byte)0x11 : (byte)0x12); // CLASS|VALUETYPE
        }

        public T Void() => WriteByte((byte)SignatureTypeCode.Void);
        public T Boolean() => WriteByte((byte)SignatureTypeCode.Boolean);
        public T Char() => WriteByte((byte)SignatureTypeCode.Char);
        public T Int8() => WriteByte((byte)SignatureTypeCode.SByte);
        public T UInt8() => WriteByte((byte)SignatureTypeCode.Byte);
        public T Int16() => WriteByte((byte)SignatureTypeCode.Int16);
        public T UInt16() => WriteByte((byte)SignatureTypeCode.UInt16);
        public T Int32() => WriteByte((byte)SignatureTypeCode.Int32);
        public T UInt32() => WriteByte((byte)SignatureTypeCode.UInt32);
        public T Int64() => WriteByte((byte)SignatureTypeCode.Int64);
        public T UInt64() => WriteByte((byte)SignatureTypeCode.UInt64);
        public T Float32() => WriteByte((byte)SignatureTypeCode.Single);
        public T Float64() => WriteByte((byte)SignatureTypeCode.Double);
        public T String() => WriteByte((byte)SignatureTypeCode.String);
        public T IntPtr() => WriteByte((byte)SignatureTypeCode.IntPtr);
        public T UIntPtr() => WriteByte((byte)SignatureTypeCode.UIntPtr);

        public T PrimitiveType(PrimitiveTypeCode type)
        {
            switch (type)
            {
                case PrimitiveTypeCode.Void: return Void();
                case PrimitiveTypeCode.Boolean: return Boolean();
                case PrimitiveTypeCode.Char: return Char();
                case PrimitiveTypeCode.Int8: return Int8();
                case PrimitiveTypeCode.UInt8: return UInt8();
                case PrimitiveTypeCode.Int16: return Int16();
                case PrimitiveTypeCode.UInt16: return UInt16();
                case PrimitiveTypeCode.Int32: return Int32();
                case PrimitiveTypeCode.UInt32: return UInt32();
                case PrimitiveTypeCode.Int64: return Int64();
                case PrimitiveTypeCode.UInt64: return UInt64();
                case PrimitiveTypeCode.Float32: return Float32();
                case PrimitiveTypeCode.Float64: return Float64();
                case PrimitiveTypeCode.String: return String();
                case PrimitiveTypeCode.IntPtr: return IntPtr();
                case PrimitiveTypeCode.UIntPtr: return UIntPtr();
                default:
                    throw new InvalidOperationException();
            }
        }

        public T Object() => WriteByte((byte)SignatureTypeCode.Object);

        public CustomModifiersBuilder<SignatureTypeBuilder<ArrayShapeBuilder<T>>> Array()
        {
            Builder.WriteByte((byte)SignatureTypeCode.Array);

            return new CustomModifiersBuilder<SignatureTypeBuilder<ArrayShapeBuilder<T>>>(
                new SignatureTypeBuilder<ArrayShapeBuilder<T>>(
                    new ArrayShapeBuilder<T>(_continuation)));
        }

        public T TypeDefOrRefOrSpec(bool isValueType, uint typeRefDefSpecCodedIndex)
        {
            ClassOrValue(isValueType);
            Builder.WriteCompressedInteger(typeRefDefSpecCodedIndex);
            return _continuation;
        }

        public MethodSignatureBuilder<T> FunctionPointer()
        {
            Builder.WriteByte((byte)SignatureTypeCode.FunctionPointer);
            return new MethodSignatureBuilder<T>(_continuation);
        }

        public GenericTypeArgumentsBuilder<T> GenericInstantiation(bool isValueType, uint typeRefDefSpecCodedIndex, int genericArgumentCount)
        {
            Builder.WriteByte((byte)SignatureTypeCode.GenericTypeInstance);
            ClassOrValue(isValueType);
            Builder.WriteCompressedInteger(typeRefDefSpecCodedIndex);
            Builder.WriteCompressedInteger((uint)genericArgumentCount);
            return new GenericTypeArgumentsBuilder<T>(_continuation, genericArgumentCount);
        }

        public T GenericMethodTypeParameter(int parameterIndex)
        {
            Builder.WriteByte((byte)SignatureTypeCode.GenericMethodParameter);
            Builder.WriteCompressedInteger((uint)parameterIndex);
            return _continuation;
        }

        public T GenericTypeParameter(uint parameterIndex)
        {
            Builder.WriteByte((byte)SignatureTypeCode.GenericTypeParameter);
            Builder.WriteCompressedInteger(parameterIndex);
            return _continuation;
        }

        public CustomModifiersBuilder<SignatureTypeBuilder<T>> Pointer()
        {
            Builder.WriteByte((byte)SignatureTypeCode.Pointer);
            return new CustomModifiersBuilder<SignatureTypeBuilder<T>>(this);
        }

        public CustomModifiersBuilder<SignatureTypeBuilder<T>> SZArray()
        {
            Builder.WriteByte((byte)SignatureTypeCode.SZArray);
            return new CustomModifiersBuilder<SignatureTypeBuilder<T>>(this);
        }
    }

    internal struct CustomModifiersBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        public CustomModifiersBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public CustomModifiersBuilder<T> AddModifier(bool isOptional, uint typeDefRefSpecCodedIndex)
        {
            if (isOptional)
            {
                Builder.WriteByte(0x20);
            }
            else
            {
                Builder.WriteByte(0x1f);
            }

            Builder.WriteCompressedInteger(typeDefRefSpecCodedIndex);
            return this;
        }

        public T EndModifiers() => _continuation;
    }

    internal struct ArrayShapeBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        public ArrayShapeBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public T Shape(int rank, ImmutableArray<int> sizes, ImmutableArray<int> lowerBounds)
        {
            Builder.WriteCompressedInteger((uint)rank);
            Builder.WriteCompressedInteger((uint)sizes.Length);
            foreach (int size in sizes)
            {
                Builder.WriteCompressedInteger((uint)size);
            }

            if (lowerBounds.IsDefault)
            {
                Builder.WriteCompressedInteger((uint)rank);
                for (int i = 0; i < rank; i++)
                {
                    Builder.WriteCompressedSignedInteger(0);
                }
            }
            else
            {
                Builder.WriteCompressedInteger((uint)lowerBounds.Length);
                foreach (int lowerBound in lowerBounds)
                {
                    Builder.WriteCompressedSignedInteger(lowerBound);
                }
            }

            return _continuation;
        }
    }

    internal struct ReturnTypeBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        public ReturnTypeBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public SignatureTypeBuilder<T> Type()
        {
            return new SignatureTypeBuilder<T>(_continuation);
        }

        public SignatureTypeBuilder<T> ByRefType()
        {
            Builder.WriteByte((byte)SignatureTypeCode.ByReference);
            return new SignatureTypeBuilder<T>(_continuation);
        }

        public T TypedReference()
        {
            Builder.WriteByte((byte)SignatureTypeCode.TypedReference);
            return _continuation;
        }

        public T Void()
        {
            Builder.WriteByte((byte)SignatureTypeCode.Void);
            return _continuation;
        }
    }

    internal struct ParameterBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;

        public ParameterBuilder(T continuation)
        {
            _continuation = continuation;
        }

        public CustomModifiersBuilder<ParameterTypeBuilder<T>> ModifiedType()
        {
            return new CustomModifiersBuilder<ParameterTypeBuilder<T>>(new ParameterTypeBuilder<T>(_continuation));
        }
    }

    internal struct ParametersBuilder<T> : ISignatureBuilder
        where T : ISignatureBuilder
    {
        public BlobBuilder Builder => _continuation.Builder;
        private readonly T _continuation;
        private readonly int _count;

        internal ParametersBuilder(T continuation, int count)
        {
            if (count < 0)
            {
                throw new InvalidOperationException();
            }

            _continuation = continuation;
            _count = count;
        }

        public ParameterBuilder<ParametersBuilder<T>> AddParameter()
        {
            return new ParameterBuilder<ParametersBuilder<T>>(
                    new ParametersBuilder<T>(_continuation, _count - 1));
        }

        public T EndParameters()
        {
            if (_count > 0)
            {
                throw new InvalidOperationException();
            }

            return _continuation;
        }
    }

}
