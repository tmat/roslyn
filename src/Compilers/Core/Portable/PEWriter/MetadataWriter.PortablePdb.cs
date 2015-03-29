﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.Emit;

namespace Microsoft.Cci
{
    partial class MetadataWriter
    {
        private struct DocumentRow
        {
            public uint Name;          // Blob
            public uint HashAlgorithm; // Guid
            public uint Hash;          // Blob
            public uint Language;      // Guid
        }

        private struct MethodBodyRow
        {
            public uint SequencePoints; // Blob
        }

        private struct LocalScopeRow
        {
            public uint Method;         // MethodRid
            public uint ImportScope;    // ImportScopeRid
            public uint VariableList;   // LocalVariableRid
            public uint ConstantList;   // LocalConstantRid
            public uint StartOffset;  
            public uint Length;                  
        }

        private struct LocalVariableRow
        {
            public ushort Attributes;
            public ushort Index;
            public StringIdx Name;     // String
        }

        private struct LocalConstantRow
        {
            public StringIdx Name;     // String
            public uint Value;         // Blob
            public byte TypeCode;
        }

        private struct ImportScopeRow
        {
            public uint Parent;        // ImportScopeRid
            public uint Imports;       // Blob index
        }

        private struct AsyncMethodRow
        {
            public uint KickoffMethod;       // MethodRid
            public uint CatchHandlerOffset;  // 0 = no catch handler, otherwise ILOffset + 1
            public uint Awaits;              // Blob - sequence of uints: (yield offset, result offset, MethodDef rid)+
        }

        private struct CustomDebugInformationRow
        {
            public uint Parent;       // HasCustomDebugInformation coded index
            public uint Kind;         // Guid
            public uint Value;        // Blob
        }

        private readonly List<DocumentRow> _documentTable = new List<DocumentRow>();
        private readonly List<MethodBodyRow> _methodBodyTable = new List<MethodBodyRow>();
        private readonly List<LocalScopeRow> _localScopeTable = new List<LocalScopeRow>();
        private readonly List<LocalVariableRow> _localVariableTable = new List<LocalVariableRow>();
        private readonly List<LocalConstantRow> _localConstantTable = new List<LocalConstantRow>();
        private readonly List<ImportScopeRow> _importScopeTable = new List<ImportScopeRow>();
        private readonly List<AsyncMethodRow> _asyncMethodTable = new List<AsyncMethodRow>();
        private readonly List<CustomDebugInformationRow> _customDebugInformationTable = new List<CustomDebugInformationRow>();

        private void PopulateDebugTableRows()
        {
            var documentIndex = new Dictionary<DebugSourceDocument, int>();
            var scopeIndex = new Dictionary<IImportScope, int>();

            DefineModuleImportScope();

            int methodRid = 1;
            foreach (IMethodDefinition methodDef in this.GetMethodDefs())
            {
                IMethodBody body = methodDef.GetBody(Context);
                if (body == null)
                {
                    _methodBodyTable.Add(default(MethodBodyRow));
                    continue;
                }

                bool isIterator = body.StateMachineTypeName != null;
                bool emitDebugInfo = isIterator || body.HasAnySequencePoints;

                if (!emitDebugInfo)
                {
                    _methodBodyTable.Add(default(MethodBodyRow));
                    continue;
                }

                var bodyImportScope = body.ImportScope;
                int importScopeRid = (bodyImportScope != null) ? GetImportScopeIndex(bodyImportScope, scopeIndex) : 0;

                // documents & sequence points:
                uint sequencePointsBlob = SerializeSequencePoints(body.GetSequencePoints(), documentIndex);
                _methodBodyTable.Add(new MethodBodyRow { SequencePoints = sequencePointsBlob });

                // TODO: order by nesting
                foreach (LocalScope scope in body.LocalScopes)
                {
                    _localScopeTable.Add(new LocalScopeRow
                    {
                        Method = (uint)methodRid,
                        ImportScope = (uint)importScopeRid,
                        VariableList = (uint)_localVariableTable.Count,
                        ConstantList = (uint)_localConstantTable.Count,
                        StartOffset = (uint)scope.StartOffset,
                        Length = (uint)scope.Length
                    });

                    foreach (ILocalDefinition local in scope.Variables)
                    {
                        Debug.Assert(local.SlotIndex >= 0);

                        _localVariableTable.Add(new LocalVariableRow
                        {
                            Attributes = (ushort)local.PdbAttributes,
                            Index = (ushort)local.SlotIndex,
                            Name = debugHeapsOpt.GetStringIndex(local.Name)
                        });

                        SerializeDynamicLocalInfo(local, rowId: _localVariableTable.Count, isConstant: false);
                    }

                    foreach (ILocalDefinition constant in scope.Constants)
                    {
                        var mdConstant = constant.CompileTimeValue;
                        Debug.Assert(mdConstant != null);

                        _localConstantTable.Add(new LocalConstantRow
                        {
                            Name = debugHeapsOpt.GetStringIndex(constant.Name),
                            Value = debugHeapsOpt.GetConstantBlobIndex(mdConstant.Value),
                            TypeCode = (byte)GetConstantTypeCode(mdConstant.Value)
                        });

                        SerializeDynamicLocalInfo(constant, rowId: _localConstantTable.Count, isConstant: true);
                    }
                }

                var asyncDebugInfo = body.AsyncDebugInfo;
                if (asyncDebugInfo != null)
                {
                    // TODO: sort by KickoffMethod
                    _asyncMethodTable.Add(new AsyncMethodRow
                    {
                        KickoffMethod = GetMethodDefIndex(asyncDebugInfo.KickoffMethod),
                        CatchHandlerOffset = (uint)(asyncDebugInfo.CatchHandlerOffset + 1),
                        Awaits = SerializeAwaitsBlob(asyncDebugInfo, methodRid),
                    });
                }

                SerializeStateMachineLocalScopes(body, methodRid);

                // delta doesn't need this information - we use information recorded by previous generation emit
                if (Context.ModuleBuilder.CommonCompilation.Options.EnableEditAndContinue && !IsFullMetadata)
                {
                    SerializeEncMethodDebugInformation(body, methodRid);
                }

                methodRid++;
            }
        }

        private static uint HasCustomDebugInformation(HasCustomDebugInformationTag tag, int rowId)
        {
            return (uint)(rowId << 5) | (uint)tag;
        }

        private enum HasCustomDebugInformationTag
        {
            MethodDef = 0,
            Module = 7,
            LocalVariable = 24,
            LocalConstant = 25,
        }

        #region ImportScope

        private const int ModuleImportScopeRid = 1;

        private void SerializeImport(BinaryWriter writer, AssemblyReferenceAlias alias)
        {
            // <import> ::= AliasAssemblyReference <alias> <target-assembly>
            writer.WriteByte((byte)ImportDefinitionKind.AliasAssemblyReference);
            writer.WriteCompressedUInt(debugHeapsOpt.GetBlobIndexUtf8(alias.Name));
            writer.WriteCompressedUInt(GetAssemblyRefIndex(alias.Assembly));     
        }

        private void SerializeImport(BinaryWriter writer, UsedNamespaceOrType import)
        {
            if (import.TargetXmlNamespaceOpt != null)
            {
                Debug.Assert(import.TargetNamespaceOpt == null);
                Debug.Assert(import.TargetAssemblyOpt == null);
                Debug.Assert(import.TargetTypeOpt == null);

                // <import> ::= ImportXmlNamespace <alias> <target-namespace>
                writer.WriteByte((byte)ImportDefinitionKind.ImportXmlNamespace);
                writer.WriteCompressedUInt(debugHeapsOpt.GetBlobIndexUtf8(import.AliasOpt));
                writer.WriteCompressedUInt(debugHeapsOpt.GetBlobIndexUtf8(import.TargetXmlNamespaceOpt));
            }
            else if (import.TargetTypeOpt != null)
            {
                Debug.Assert(import.TargetNamespaceOpt == null);
                Debug.Assert(import.TargetAssemblyOpt == null);

                if (import.AliasOpt != null)
                {
                    // <import> ::= AliasType <alias> <target-type>
                    writer.WriteByte((byte)ImportDefinitionKind.AliasType);
                    writer.WriteCompressedUInt(debugHeapsOpt.GetBlobIndexUtf8(import.AliasOpt));
                }
                else
                {
                    // <import> ::= ImportType <target-type>
                    writer.WriteByte((byte)ImportDefinitionKind.ImportType);
                }

                writer.WriteCompressedUInt(GetTypeDefOrRefCodedIndex(import.TargetTypeOpt, treatRefAsPotentialTypeSpec: true)); // TODO: index in release build         
            }
            else if (import.TargetNamespaceOpt != null)
            {
                if (import.TargetAssemblyOpt != null)
                {
                    if (import.AliasOpt != null)
                    {
                        // <import> ::= AliasAssemblyNamespace <alias> <target-assembly> <target-namespace>
                        writer.WriteByte((byte)ImportDefinitionKind.AliasAssemblyNamespace);
                        writer.WriteCompressedUInt(debugHeapsOpt.GetBlobIndexUtf8(import.AliasOpt));
                    }
                    else
                    {
                        // <import> ::= ImportAssemblyNamespace <target-assembly> <target-namespace>
                        writer.WriteByte((byte)ImportDefinitionKind.ImportAssemblyNamespace);
                    }

                    writer.WriteCompressedUInt(GetAssemblyRefIndex(import.TargetAssemblyOpt));
                }
                else
                {
                    if (import.AliasOpt != null)
                    {
                        // <import> ::= AliasNamespace <alias> <target-namespace>
                        writer.WriteByte((byte)ImportDefinitionKind.AliasNamespace);
                        writer.WriteCompressedUInt(debugHeapsOpt.GetBlobIndexUtf8(import.AliasOpt));
                    }
                    else
                    {
                        // <import> ::= ImportNamespace <target-namespace>
                        writer.WriteByte((byte)ImportDefinitionKind.ImportNamespace);
                    }
                }

                // TODO: cache?
                string namespaceName = TypeNameSerializer.BuildQualifiedNamespaceName(import.TargetNamespaceOpt);
                writer.WriteCompressedUInt(debugHeapsOpt.GetBlobIndexUtf8(namespaceName));
            } 
            else
            {
                // <import> ::= ImportReferenceAlias <alias>
                Debug.Assert(import.AliasOpt != null);
                Debug.Assert(import.TargetAssemblyOpt == null);

                writer.WriteByte((byte)ImportDefinitionKind.ImportAssemblyReferenceAlias);
                writer.WriteCompressedUInt(debugHeapsOpt.GetBlobIndexUtf8(import.AliasOpt));
            }
        }

        private void DefineModuleImportScope()
        {
            // module-level import scope:
            MemoryStream imports = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(imports);

            SerializeModuleDefaultNamespace();

            foreach (AssemblyReferenceAlias alias in module.GetAssemblyReferenceAliases(Context))
            {
                SerializeImport(writer, alias);
            }

            foreach (UsedNamespaceOrType import in module.GetImports(Context))
            {
                SerializeImport(writer, import);
            }

            _importScopeTable.Add(new ImportScopeRow
            {
                Parent = 0,
                Imports = debugHeapsOpt.GetBlobIndex(imports)
            });

            Debug.Assert(_importScopeTable.Count == ModuleImportScopeRid);
        }

        private int GetImportScopeIndex(IImportScope scope, Dictionary<IImportScope, int> scopeIndex)
        {
            int scopeRid;
            if (scopeIndex.TryGetValue(scope, out scopeRid))
            {
                // scoep is already indexed:
                return scopeRid;
            }

            var parent = scope.Parent;
            int parentScopeRid = (parent != null) ? GetImportScopeIndex(scope.Parent, scopeIndex) : ModuleImportScopeRid;

            _importScopeTable.Add(new ImportScopeRow
            {
                Parent = (uint)parentScopeRid,
                Imports = SerializeImportsBlob(scope)
            });

            var rid = _importScopeTable.Count;
            scopeIndex.Add(scope, rid);
            return rid;
        }

        private uint SerializeImportsBlob(IImportScope scope)
        {
            MemoryStream imports = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(imports);

            foreach (UsedNamespaceOrType import in scope.GetUsedNamespaces(Context))
            {
                SerializeImport(writer, import);
            }

            return debugHeapsOpt.GetBlobIndex(imports);
        }

        private void SerializeModuleDefaultNamespace()
        {
            if (module.DefaultNamespace == null)
            {
                return;
            }

            _customDebugInformationTable.Add(new CustomDebugInformationRow
            {
                Parent = HasCustomDebugInformation(HasCustomDebugInformationTag.Module, 0),
                Kind = debugHeapsOpt.GetGuidIndex(PortableCustomDebugInfoKinds.DefaultNamespace),
                Value = debugHeapsOpt.GetBlobIndexUtf8(module.DefaultNamespace)
            });
        }

        #endregion

        #region Locals

        private void SerializeDynamicLocalInfo(ILocalDefinition local, int rowId, bool isConstant)
        {
            var dynamicFlags = local.DynamicTransformFlags;
            if (dynamicFlags.IsDefault)
            {
                return;
            }

            var value = SerializeBitVector(dynamicFlags);

            var tag = isConstant ? HasCustomDebugInformationTag.LocalConstant : HasCustomDebugInformationTag.LocalVariable;

            _customDebugInformationTable.Add(new CustomDebugInformationRow
            {
                Parent = HasCustomDebugInformation(tag, rowId),
                Kind = debugHeapsOpt.GetGuidIndex(PortableCustomDebugInfoKinds.DynamicLocalVariables),
                Value = debugHeapsOpt.GetBlobIndex(value),
            });
        }

        private static ImmutableArray<byte> SerializeBitVector(ImmutableArray<TypedConstant> vector)
        {
            var builder = ArrayBuilder<byte>.GetInstance();

            int b = 0;
            int shift = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                if ((bool)vector[i].Value)
                {
                    b |= 1 << shift;
                }

                if (shift == 7)
                {
                    builder.Add((byte)b);
                    b = 0;
                    shift = 0;
                }
                else
                {
                    shift++;
                }
            }

            if (b != 0)
            {
                builder.Add((byte)b);
            }
            else
            {
                // trim trailing zeros:
                int lastNonZero = builder.Count - 1;
                while (builder[lastNonZero] == 0)
                {
                    lastNonZero--;
                }

                builder.Clip(lastNonZero + 1);
            }

            return builder.ToImmutableAndFree();
        }

        #endregion
        
        #region State Machines

        private uint SerializeAwaitsBlob(AsyncMethodBodyDebugInfo asyncInfo, int moveNextMethodRid)
        {
            MemoryStream sig = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(sig);

            Debug.Assert(asyncInfo.ResumeOffsets.Length == asyncInfo.YieldOffsets.Length);

            for (int i = 0; i < asyncInfo.ResumeOffsets.Length; i++)
            {
                writer.WriteCompressedUInt((uint)asyncInfo.YieldOffsets[i]);
                writer.WriteCompressedUInt((uint)asyncInfo.ResumeOffsets[i]);
                writer.WriteCompressedUInt((uint)moveNextMethodRid);
            }

            return debugHeapsOpt.GetBlobIndex(sig);
        }

        private void SerializeStateMachineLocalScopes(IMethodBody methodBody, int methodRowId)
        {
            var scopes = methodBody.StateMachineHoistedLocalScopes;
            if (scopes.IsDefaultOrEmpty)
            {
                return;
            }

            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);

            foreach (var scope in scopes)
            {
                writer.WriteUint((uint)scope.StartOffset);
                writer.WriteUint((uint)scope.Length);
            }

            _customDebugInformationTable.Add(new CustomDebugInformationRow
            {
                Parent = HasCustomDebugInformation(HasCustomDebugInformationTag.MethodDef, methodRowId),
                Kind = debugHeapsOpt.GetGuidIndex(PortableCustomDebugInfoKinds.StateMachineHoistedLocalScopes),
                Value = debugHeapsOpt.GetBlobIndex(stream),
            });
        }

        #endregion

        #region Sequence Points

        private uint SerializeSequencePoints(ImmutableArray<SequencePoint> sequencePoints, Dictionary<DebugSourceDocument, int> documentIndex)
        {
            if (sequencePoints.Length == 0)
            {
                return 0;
            }

            MemoryStream sig = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(sig);

            uint currentDocumentRowId = GetOrAddDocument(sequencePoints[0].Document, documentIndex);

            // first record:
            writer.WriteCompressedUInt(currentDocumentRowId);
            writer.WriteCompressedUInt((uint)sequencePoints[0].Offset);
            SerializeDeltaLinesAndColumns(writer, sequencePoints[0]);
            writer.WriteCompressedUInt((uint)sequencePoints[0].StartLine);
            writer.WriteCompressedUInt((uint)sequencePoints[0].StartColumn);

            for (int i = 1; i < sequencePoints.Length; i++)
            {
                uint documentRowId = GetOrAddDocument(sequencePoints[i].Document, documentIndex);
                if (documentRowId != currentDocumentRowId)
                {
                    // document record:
                    writer.WriteCompressedUInt(0);
                    writer.WriteCompressedUInt(documentRowId);
                    currentDocumentRowId = documentRowId;
                }

                // subsequent record:
                writer.WriteCompressedUInt((uint)(sequencePoints[i].Offset - sequencePoints[i - 1].Offset));
                SerializeDeltaLinesAndColumns(writer, sequencePoints[i]);

                if (sequencePoints[i].IsHidden)
                {
                    continue;
                }

                writer.WriteCompressedSignedInteger(sequencePoints[i].StartLine - sequencePoints[i - 1].StartLine);
                writer.WriteCompressedSignedInteger(sequencePoints[i].StartColumn - sequencePoints[i - 1].StartColumn);
            }

            return debugHeapsOpt.GetBlobIndex(sig);
        }

        private void SerializeDeltaLinesAndColumns(BinaryWriter writer, SequencePoint sequencePoint)
        {
            int deltaLines = sequencePoint.EndLine - sequencePoint.StartLine;
            int deltaColumns = sequencePoint.EndColumn - sequencePoint.StartColumn;

            // only hidden sequence points have zero width
            Debug.Assert(deltaLines != 0 || deltaColumns != 0 || sequencePoint.IsHidden);

            writer.WriteCompressedUInt((uint)deltaLines);

            if (deltaLines == 0)
            {
                writer.WriteCompressedUInt((uint)deltaColumns);
            }
            else
            {
                writer.WriteCompressedSignedInteger(deltaColumns);
            }
        }

        #endregion

        #region Documents

        private uint GetOrAddDocument(DebugSourceDocument document, Dictionary<DebugSourceDocument, int> index)
        {
            int documentRowId;
            if (!index.TryGetValue(document, out documentRowId))
            {
                documentRowId = _documentTable.Count + 1;
                index.Add(document, documentRowId);

                var checksumAndAlgorithm = document.ChecksumAndAlgorithm;
                _documentTable.Add(new DocumentRow
                {
                    Name = SerializeDocumentName(document.Location),
                    HashAlgorithm = checksumAndAlgorithm.Item1.IsDefault ? 0 : debugHeapsOpt.GetGuidIndex(checksumAndAlgorithm.Item2),
                    Hash = checksumAndAlgorithm.Item1.IsDefault ? 0 : debugHeapsOpt.GetBlobIndex(checksumAndAlgorithm.Item1),
                    Language = debugHeapsOpt.GetGuidIndex(document.Language),
                });
            }

            return (uint)documentRowId;
        }

        private static readonly char[] Separator1 = { '/' };
        private static readonly char[] Separator2 = { '\\' };

        private uint SerializeDocumentName(string name)
        {
            if (name.Length == 0)
            {
                return 0;
            }

            MemoryStream sig = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(sig);

            int c1 = Count(name, Separator1[0]);
            int c2 = Count(name, Separator2[0]);
            char[] separator = (c1 >= c2) ? Separator1 : Separator2;

            writer.WriteByte((byte)separator[0]);

            // TODO: avoid allocations
            foreach (var part in name.Split(separator))
            {
                var partIndex = debugHeapsOpt.GetBlobIndex(ImmutableArray.Create(s_utf8Encoding.GetBytes(part)));
                writer.WriteCompressedUInt(partIndex);
            }

            return debugHeapsOpt.GetBlobIndex(sig);
        }

        private static int Count(string str, char c)
        {
            int count = 0;
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == c)
                {
                    count++;
                }
            }

            return count;
        }

        #endregion

        #region Edit and Continue

        private void SerializeEncMethodDebugInformation(IMethodBody methodBody, int methodRowId)
        {
            var encInfo = GetEncMethodDebugInfo(methodBody);

            if (!encInfo.LocalSlots.IsDefaultOrEmpty)
            {
                MemoryStream stream = new MemoryStream();
                BinaryWriter writer = new BinaryWriter(stream);

                encInfo.SerializeLocalSlots(writer);

                _customDebugInformationTable.Add(new CustomDebugInformationRow
                {
                    Parent = HasCustomDebugInformation(HasCustomDebugInformationTag.MethodDef, methodRowId),
                    Kind = debugHeapsOpt.GetGuidIndex(PortableCustomDebugInfoKinds.EncLocalSlotMap),
                    Value = debugHeapsOpt.GetBlobIndex(stream),
                });
            }

            if (!encInfo.Lambdas.IsDefaultOrEmpty)
            {
                MemoryStream stream = new MemoryStream();
                BinaryWriter writer = new BinaryWriter(stream);

                encInfo.SerializeLambdaMap(writer);

                _customDebugInformationTable.Add(new CustomDebugInformationRow
                {
                    Parent = HasCustomDebugInformation(HasCustomDebugInformationTag.MethodDef, methodRowId),
                    Kind = debugHeapsOpt.GetGuidIndex(PortableCustomDebugInfoKinds.EncLambdaAndClosureMap),
                    Value = debugHeapsOpt.GetBlobIndex(stream),
                });
            }
        }

        #endregion

        #region Table Serialization

        private void SerializeDocumentTable(BinaryWriter writer, MetadataSizes metadataSizes)
        {
            foreach (var row in _documentTable)
            {
                writer.WriteReference(row.Name, metadataSizes.BlobIndexSize);
                writer.WriteReference(row.HashAlgorithm, metadataSizes.GuidIndexSize);
                writer.WriteReference(row.Hash, metadataSizes.BlobIndexSize);
                writer.WriteReference(row.Language, metadataSizes.GuidIndexSize);
            }
        }

        private void SerializeMethodBodyTable(BinaryWriter writer, MetadataSizes metadataSizes)
        {
            foreach (var row in _methodBodyTable)
            {
                writer.WriteReference(row.SequencePoints, metadataSizes.BlobIndexSize);
            }
        }

        private void SerializeLocalScopeTable(BinaryWriter writer, MetadataSizes metadataSizes)
        {
            foreach (var row in _localScopeTable)
            {
                writer.WriteReference(row.Method, metadataSizes.MethodDefIndexSize);
                writer.WriteReference(row.ImportScope, metadataSizes.ImportScopeIndexSize);
                writer.WriteReference(row.VariableList, metadataSizes.LocalVariableIndexSize);
                writer.WriteReference(row.ConstantList, metadataSizes.LocalConstantIndexSize);
                writer.WriteUint(row.StartOffset);
                writer.WriteUint(row.Length);
            }
        }

        private void SerializeLocalVariableTable(BinaryWriter writer, MetadataSizes metadataSizes)
        {
            foreach (var row in _localVariableTable)
            {
                writer.WriteUshort(row.Attributes);
                writer.WriteUshort(row.Index);
                writer.WriteReference(debugHeapsOpt.ResolveStringIndex(row.Name), metadataSizes.StringIndexSize);
            }
        }

        private void SerializeLocalConstantTable(BinaryWriter writer, MetadataSizes metadataSizes)
        {
            foreach (var row in _localConstantTable)
            {
                writer.WriteReference(debugHeapsOpt.ResolveStringIndex(row.Name), metadataSizes.StringIndexSize);
                writer.WriteReference(row.Value, metadataSizes.BlobIndexSize);
                writer.WriteByte(row.TypeCode);
            }
        }

        private void SerializeImportScopeTable(BinaryWriter writer, MetadataSizes metadataSizes)
        {
            foreach (var row in _importScopeTable)
            {
                writer.WriteReference(row.Parent, metadataSizes.ImportScopeIndexSize);
                writer.WriteReference(row.Imports, metadataSizes.BlobIndexSize);
            }
        }

        private void SerializeAsyncMethodTable(BinaryWriter writer, MetadataSizes metadataSizes)
        {
            foreach (var row in _asyncMethodTable)
            {
                writer.WriteReference(row.KickoffMethod, metadataSizes.MethodDefIndexSize);
                writer.WriteUint(row.CatchHandlerOffset);
                writer.WriteReference(row.Awaits, metadataSizes.BlobIndexSize);
            }
        }

        private void SerializeCustomDebugInformationTable(BinaryWriter writer, MetadataSizes metadataSizes)
        {
            foreach (var row in _customDebugInformationTable)
            {
                writer.WriteReference(row.Parent, metadataSizes.HasCustomDebugInformationSize);
                writer.WriteReference(row.Kind, metadataSizes.GuidIndexSize);
                writer.WriteReference(row.Value, metadataSizes.BlobIndexSize);
            }
        }

        #endregion
    }
}
