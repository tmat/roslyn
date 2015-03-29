// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

extern alias PDB;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.DiaSymReader;
using PDB::Roslyn.Test.PdbUtilities;
using PDB::Microsoft.DiaSymReader;
using Roslyn.Test.Utilities;
using Xunit;
using System.Reflection.Metadata.Ecma335;

namespace Microsoft.CodeAnalysis.Test.Utilities
{
    public static class SharedCompilationUtils
    {
        internal static CompilationTestData.MethodData GetMethodData(this CompilationTestData data, string qualifiedMethodName)
        {
            var methodData = default(CompilationTestData.MethodData);
            var map = data.Methods;

            if (!map.TryGetValue(qualifiedMethodName, out methodData))
            {
                // caller may not have specified parameter list, so try to match parameterless method
                if (!map.TryGetValue(qualifiedMethodName + "()", out methodData))
                {
                    // now try to match single method with any parameter list
                    var keys = map.Keys.Where(k => k.StartsWith(qualifiedMethodName + "(", StringComparison.Ordinal));
                    if (keys.Count() == 1)
                    {
                        methodData = map[keys.First()];
                    }
                    else if (keys.Count() > 1)
                    {
                        throw new AmbiguousMatchException(
                            "Could not determine best match for method named: " + qualifiedMethodName + Environment.NewLine +
                            String.Join(Environment.NewLine, keys.Select(s => "    " + s)) + Environment.NewLine);
                    }
                }
            }

            if (methodData.ILBuilder == null)
            {
                throw new KeyNotFoundException("Could not find ILBuilder matching method '" + qualifiedMethodName + "'. Existing methods:\r\n" + string.Join("\r\n", map.Keys));
            }

            return methodData;
        }

        internal static void VerifyIL(
            this CompilationTestData.MethodData method,
            string expectedIL,
            [CallerLineNumber]int expectedValueSourceLine = 0,
            [CallerFilePath]string expectedValueSourcePath = null)
        {
            const string moduleNamePlaceholder = "{#Module#}";
            string actualIL = GetMethodIL(method);
            if (expectedIL.IndexOf(moduleNamePlaceholder) >= 0)
            {
                var module = method.Method.ContainingModule;
                var moduleName = Path.GetFileNameWithoutExtension(module.Name);
                expectedIL = expectedIL.Replace(moduleNamePlaceholder, moduleName);
            }
            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL, actualIL, escapeQuotes: true, expectedValueSourcePath: expectedValueSourcePath, expectedValueSourceLine: expectedValueSourceLine);
        }

        internal static void VerifyPdb(
            this Compilation compilation,
            string expectedPdb,
            [CallerLineNumber]int expectedValueSourceLine = 0,
            [CallerFilePath]string expectedValueSourcePath = null)
        {
            VerifyPdb(compilation, "", expectedPdb, expectedValueSourceLine, expectedValueSourcePath);
        }

        internal static void VerifyPdb(
            this Compilation compilation,
            string qualifiedMethodName,
            string expectedPdb,
            [CallerLineNumber]int expectedValueSourceLine = 0,
            [CallerFilePath]string expectedValueSourcePath = null)
        {
            string actualPdb = GetPdbXml(compilation, qualifiedMethodName);
            XmlElementDiff.AssertEqual(ParseExpectedPdbXml(expectedPdb), XElement.Parse(actualPdb), expectedValueSourcePath, expectedValueSourceLine, expectedIsXmlLiteral: false);
        }

        private static XElement ParseExpectedPdbXml(string str)
        {
            return XElement.Parse(string.IsNullOrWhiteSpace(str) ? "<symbols></symbols>" : str);
        }

        internal static void VerifyPdb(
            this Compilation compilation,
            XElement expectedPdb,
            [CallerLineNumber]int expectedValueSourceLine = 0,
            [CallerFilePath]string expectedValueSourcePath = null)
        {
            VerifyPdb(compilation, "", expectedPdb, expectedValueSourceLine, expectedValueSourcePath);
        }

        internal static void VerifyPdb(
            this Compilation compilation,
            string qualifiedMethodName,
            XElement expectedPdb,
            [CallerLineNumber]int expectedValueSourceLine = 0,
            [CallerFilePath]string expectedValueSourcePath = null)
        {
            XElement actualPdb = XElement.Parse(GetPdbXml(compilation, qualifiedMethodName));
            XmlElementDiff.AssertEqual(expectedPdb, actualPdb, expectedValueSourcePath, expectedValueSourceLine, expectedIsXmlLiteral: true);
        }

        internal static string GetPdbXml(Compilation compilation, string qualifiedMethodName = "")
        {
            string actual = null;
            var nativePEStream = new MemoryStream();
            var nativePdbStream = new MemoryStream();
            compilation.Emit(nativePEStream, nativePdbStream);

            nativePdbStream.Position = 0;
            nativePEStream.Position = 0;

            actual = PdbToXmlConverter.ToXml(nativePdbStream, nativePEStream, PdbToXmlOptions.ResolveTokens | PdbToXmlOptions.ThrowOnError, methodName: qualifiedMethodName);
            ValidateDebugDirectory(nativePEStream, compilation.AssemblyName + ".pdb");

            var portablePEStream = new MemoryStream();
            var portablePdbStream = new MemoryStream();
            compilation.Emit(portablePEStream, portablePdbStream, options: EmitOptions.Default.WithDebugInformationFormat(DebugInformationFormat.PortablePdb));

            // TODO: determinism
            // PE streams shall be equal:
            // AssertEx.Equal(nativePEStream.ToArray(), portablePEStream.ToArray());
            ValidateDebugDirectory(portablePEStream, compilation.AssemblyName + ".pdb");
            
            ValidatePortablePdb(nativePdbStream, portablePdbStream.ToArray(), portablePEStream);

            return actual;
        }

        private static unsafe void ValidatePortablePdb(Stream symStream, byte[] pdbImage, Stream peStream)
        {
            fixed (byte* pdbPtr = pdbImage)
            {
                var pdbReader = new MetadataReader(pdbPtr, pdbImage.Length);

                peStream.Position = 0;
                symStream.Position = 0;

                using (PEReader peReader = new PEReader(peStream))
                using (SymReader symReader = new SymReader(symStream))
                {
                    var mdReader = peReader.GetMetadataReader();

                    ValidateDocuments(symReader, pdbReader);
                    ValidateSequencePoints(symReader, pdbReader, mdReader);
                    ValidateLocalScopes(symReader, pdbReader, mdReader);
                    ValidateAsyncMethods(symReader, pdbReader, mdReader);
                }
            }
        }

        private static void ValidateDocuments(ISymUnmanagedReader symReader, MetadataReader pdbReader)
        {
            var symDocumentByName = symReader.GetDocuments().ToDictionary(sd => sd.GetName(), sd => sd);
            Assert.Equal(symDocumentByName.Count, pdbReader.Documents.Count);

            foreach (var documentHandle in pdbReader.Documents)
            {
                var document = pdbReader.GetDocument(documentHandle);
                var name = document.GetNameString();
                var symDocument = symDocumentByName[name];

                var hash = pdbReader.GetBlobContent(document.Hash);
                var symHash = symDocument.GetChecksum();
                Assert.Equal(symHash, hash);

                var language = pdbReader.GetGuid(document.Language);
                var symLanguage = symDocument.GetLanguage();
                Assert.Equal(symLanguage, language);

                var hashAlgorithm = document.HashAlgorithm;
                var symHashAlgorithm = symDocument.GetHashAlgorithm();
                Assert.Equal(symLanguage, language);
            }
        }

        private static void ValidateSequencePoints(ISymUnmanagedReader symReader, MetadataReader pdbReader, MetadataReader mdReader)
        {
            foreach (var methodDefHandle in mdReader.MethodDefinitions)
            {
                var methodBody = pdbReader.GetMethodBody(methodDefHandle);
                var symMethod = symReader.GetMethod(MetadataTokens.GetToken(mdReader, methodDefHandle));

                var sequencePointReader = pdbReader.GetSequencePointsReader(methodBody.SequencePoints);
                var symSequencePointReader = symMethod.GetSequencePoints().GetEnumerator();

                while (sequencePointReader.MoveNext())
                {
                    Assert.True(symSequencePointReader.MoveNext());

                    var sequencePoint = sequencePointReader.Current;
                    var symSequencePoint = symSequencePointReader.Current;

                    Assert.Equal(sequencePoint.StartLine, symSequencePoint.StartLine);
                    Assert.Equal(sequencePoint.StartColumn, symSequencePoint.StartColumn);
                    Assert.Equal(sequencePoint.EndLine, symSequencePoint.EndLine);
                    Assert.Equal(sequencePoint.EndColumn, symSequencePoint.EndColumn);

                    var documentName = pdbReader.GetDocument(sequencePoint.Document).GetNameString();
                    var symDocumentName = symSequencePoint.Document.GetName();
                    Assert.Equal(documentName, documentName);
                }

                Assert.False(symSequencePointReader.MoveNext());
            }
        }

        private static void ValidateLocalScopes(ISymUnmanagedReader symReader, MetadataReader pdbReader, MetadataReader mdReader)
        {
            foreach (var localScopesByMethod in pdbReader.LocalScopes.GroupBy(lsh => pdbReader.GetLocalScope(lsh).Method))
            {
                var methodDefHandle = localScopesByMethod.Key;

                var symLocalScopes = symReader.GetMethod(MetadataTokens.GetToken(methodDefHandle)).GetAllScopes();
                Assert.Equal(symLocalScopes.Length, localScopesByMethod.Count());

                int i = 0;
                foreach (var localScopeHandle in localScopesByMethod)
                {
                    var symLocalScope = symLocalScopes[i++];
                    var localScope = pdbReader.GetLocalScope(localScopeHandle);

                    Assert.Equal(symLocalScope.GetStartOffset(), localScope.StartOffset);
                    Assert.Equal(symLocalScope.GetEndOffset(), localScope.StartOffset + localScope.Length); // TODO: adjust for VB

                    ValidateLocalVariables(pdbReader, symLocalScope.GetLocals(), localScope.GetLocalVariables());
                    ValidateLocalConstants(pdbReader, symLocalScope.GetConstants(), localScope.GetLocalConstants());

                    // TODO: group namespaces, VB, C# forwards...
                    //var symImportScopes = symLocalScope.GetNamespaces();
                    //var importScopeHandle = localScope.ImportScope;
                    //while (!importScopeHandle.IsNil)
                    //{
                    //    var importScope = pdbReader.GetImportScope(importScopeHandle);

                    //    importScopeHandle = importScope.Parent;
                    //}
                }
            }
        }

        private static void ValidateLocalVariables(MetadataReader pdbReader, ImmutableArray<ISymUnmanagedVariable> symVariables, LocalVariableHandleCollection variables)
        {
            Assert.Equal(symVariables.Length, variables.Count);

            int v = 0;
            foreach (var variableHandle in variables)
            {
                var symVariable = symVariables[v++];
                var variable = pdbReader.GetLocalVariable(variableHandle);

                Assert.Equal(symVariable.GetName(), pdbReader.GetString(variable.Name));
                Assert.Equal(symVariable.GetSlot(), variable.Index);
                Assert.Equal(symVariable.GetAttributes(), (int)variable.Attributes);
            }
        }

        private static void ValidateLocalConstants(MetadataReader pdbReader, ImmutableArray<ISymUnmanagedConstant> symConstants, LocalConstantHandleCollection constants)
        {
            Assert.Equal(symConstants.Length, constants.Count);

            int c = 0;
            foreach (var constantHandle in constants)
            {
                var symConstant = symConstants[c++];
                var constant = pdbReader.GetLocalConstant(constantHandle);

                Assert.Equal(symConstant.GetName(), pdbReader.GetString(constant.Name));

                // C# and VB don't produce custom typed constants
                Assert.NotEqual(0, (int)constant.TypeCode);

                var value = GetConstantValue(pdbReader, constant.TypeCode, constant.Value);
                var symValue = symConstant.GetValue();
                Assert.Equal(symValue, value);
            }
        }

        private static object GetConstantValue(MetadataReader mdReader, ConstantTypeCode typeCode, BlobHandle handle)
        {
            // Partition II section 22.9:
            //
            // Type shall be exactly one of: ELEMENT_TYPE_BOOLEAN, ELEMENT_TYPE_CHAR, ELEMENT_TYPE_I1, 
            // ELEMENT_TYPE_U1, ELEMENT_TYPE_I2, ELEMENT_TYPE_U2, ELEMENT_TYPE_I4, ELEMENT_TYPE_U4, 
            // ELEMENT_TYPE_I8, ELEMENT_TYPE_U8, ELEMENT_TYPE_R4, ELEMENT_TYPE_R8, or ELEMENT_TYPE_STRING; 
            // or ELEMENT_TYPE_CLASS with a Value of zero  (23.1.16)

            // In addition reads Decimal and DateTime types used in debug metadata.

            BlobReader reader = mdReader.GetBlobReader(handle);
            switch (typeCode)
            {
                case ConstantTypeCode.Boolean:
                    return reader.ReadByte();

                case ConstantTypeCode.Char:
                    return reader.ReadChar();

                case ConstantTypeCode.SByte:
                    return reader.ReadSByte();

                case ConstantTypeCode.Int16:
                    return reader.ReadInt16();

                case ConstantTypeCode.Int32:
                    return reader.ReadInt32();

                case ConstantTypeCode.Int64:
                    return reader.ReadInt64();

                case ConstantTypeCode.Byte:
                    return reader.ReadByte();

                case ConstantTypeCode.UInt16:
                    return reader.ReadUInt16();

                case ConstantTypeCode.UInt32:
                    return reader.ReadUInt32();

                case ConstantTypeCode.UInt64:
                    return reader.ReadUInt64();

                case ConstantTypeCode.Single:
                    return reader.ReadSingle();

                case ConstantTypeCode.Double:
                    return reader.ReadDouble();

                case ConstantTypeCode.String:
                    // A null string constant is represented as an ELEMENT_TYPE_CLASS.
                    return (reader.Length == 0) ? "" : reader.ReadUTF16(reader.Length);

                case ConstantTypeCode.NullReference:
                    // TODO: Error checking; verify that the value is all zero bytes;
                    return ConstantValue.Null;

                case Cci.Constants.ConstantTypeCode_Decimal:
                    var signAndScale = reader.ReadByte();
                    return new decimal(
                        reader.ReadInt32(), 
                        reader.ReadInt32(), 
                        reader.ReadInt32(), 
                        isNegative: (signAndScale & 0x80) != 0,
                        scale: (byte)(signAndScale & 0x7f));

                case Cci.Constants.ConstantTypeCode_DateTime:
                    return new DateTime(reader.ReadInt64());

                default:
                    throw new BadImageFormatException("Invalid constant type code");
            }
        }

        private static void ValidateAsyncMethods(ISymUnmanagedReader symReader, MetadataReader pdbReader, MetadataReader mdReader)
        {
            var symAsyncMethodCount = mdReader.MethodDefinitions.Count(
                h => symReader.GetMethod(MetadataTokens.GetToken(h)).AsAsync() != null);

            Assert.Equal(symAsyncMethodCount, pdbReader.AsyncMethods.Count);

            // TODO:
            //foreach (var asyncMethodHandle in pdbReader.AsyncMethods)
            //{
            //    var asyncMethod = pdbReader.GetAsyncMethod(asyncMethodHandle);
            //    var symAsyncMethod = symReader.GetMethod(MetadataTokens.GetToken(asyncMethod.MoveNextMethod)).AsAsync();
            //    Assert.NotNull(symAsyncMethod);
            //}
        }

        private static void ValidateDebugDirectory(MemoryStream peStream, string pdbPath)
        {
            peStream.Seek(0, SeekOrigin.Begin);
            PEReader peReader = new PEReader(peStream);

            var debugDirectory = peReader.PEHeaders.PEHeader.DebugTableDirectory;

            int position;
            Assert.True(peReader.PEHeaders.TryGetDirectoryOffset(debugDirectory, out position));
            Assert.Equal(0x1c, debugDirectory.Size);

            byte[] buffer = new byte[debugDirectory.Size];
            peStream.Read(buffer, 0, buffer.Length);

            peStream.Position = position;
            var reader = new BinaryReader(peStream);

            int characteristics = reader.ReadInt32();
            Assert.Equal(0, characteristics);

            uint timeDateStamp = reader.ReadUInt32();

            uint version = reader.ReadUInt32();
            Assert.Equal(0u, version);

            int type = reader.ReadInt32();
            Assert.Equal(2, type);

            int sizeOfData = reader.ReadInt32();
            int rvaOfRawData = reader.ReadInt32();

            int section = peReader.PEHeaders.GetContainingSectionIndex(rvaOfRawData);
            var sectionHeader = peReader.PEHeaders.SectionHeaders[section];

            int pointerToRawData = reader.ReadInt32();
            Assert.Equal(pointerToRawData, sectionHeader.PointerToRawData + rvaOfRawData - sectionHeader.VirtualAddress);

            peStream.Position = pointerToRawData;

            Assert.Equal((byte)'R', reader.ReadByte());
            Assert.Equal((byte)'S', reader.ReadByte());
            Assert.Equal((byte)'D', reader.ReadByte());
            Assert.Equal((byte)'S', reader.ReadByte());

            byte[] guidBlob = new byte[16];
            reader.Read(guidBlob, 0, guidBlob.Length);

            Assert.Equal(1u, reader.ReadUInt32());

            byte[] pathBlob = new byte[sizeOfData - 24 - 1];
            reader.Read(pathBlob, 0, pathBlob.Length);
            var actualPath = Encoding.UTF8.GetString(pathBlob);
            Assert.Equal(pdbPath, actualPath);
            Assert.Equal(0, reader.ReadByte());
        }

        internal static string GetMethodIL(this CompilationTestData.MethodData method)
        {
            return ILBuilderVisualizer.ILBuilderToString(method.ILBuilder);
        }

        internal static EditAndContinueMethodDebugInformation GetEncDebugInfo(this CompilationTestData.MethodData methodData)
        {
            // TODO:
            return new EditAndContinueMethodDebugInformation(
                0,
                Cci.MetadataWriter.GetLocalSlotDebugInfos(methodData.ILBuilder.LocalSlotManager.LocalsInOrder()),
                closures: ImmutableArray<ClosureDebugInfo>.Empty,
                lambdas: ImmutableArray<LambdaDebugInfo>.Empty);
        }

        internal static Func<MethodDefinitionHandle, EditAndContinueMethodDebugInformation> EncDebugInfoProvider(this CompilationTestData.MethodData methodData)
        {
            return _ => methodData.GetEncDebugInfo();
        }

        public static DisposableFile IlasmTempAssembly(string declarations, bool appendDefaultHeader = true)
        {
            string assemblyPath;
            string pdbPath;
            IlasmTempAssembly(declarations, appendDefaultHeader, includePdb: false, assemblyPath: out assemblyPath, pdbPath: out pdbPath);
            Assert.NotNull(assemblyPath);
            Assert.Null(pdbPath);
            return new DisposableFile(assemblyPath);
        }

        public static void IlasmTempAssembly(string declarations, bool appendDefaultHeader, bool includePdb, out string assemblyPath, out string pdbPath)
        {
            if (declarations == null) throw new ArgumentNullException("declarations");

            using (var sourceFile = new DisposableFile(extension: ".il"))
            {
                string sourceFileName = Path.GetFileNameWithoutExtension(sourceFile.Path);

                assemblyPath = Path.Combine(
                    TempRoot.Root,
                    Path.ChangeExtension(Path.GetFileName(sourceFile.Path), "dll"));

                string completeIL;
                if (appendDefaultHeader)
                {
                    completeIL = string.Format(
@".assembly '{0}' {{}} 

.assembly extern mscorlib 
{{
  .publickeytoken = (B7 7A 5C 56 19 34 E0 89)
  .ver 4:0:0:0
}} 

{1}",
                        sourceFileName,
                        declarations);
                }
                else
                {
                    completeIL = declarations.Replace("<<GeneratedFileName>>", sourceFileName);
                }

                sourceFile.WriteAllText(completeIL);

                var ilasmPath = Path.Combine(
                    Path.GetDirectoryName(typeof(object).Assembly.Location),
                    "ilasm.exe");

                var arguments = string.Format(
                    "\"{0}\" /DLL /OUT=\"{1}\"",
                    sourceFile.Path,
                    assemblyPath);

                if (includePdb)
                {
                    pdbPath = Path.ChangeExtension(assemblyPath, "pdb");
                    arguments += string.Format(" /PDB=\"{0}\"", pdbPath);
                }
                else
                {
                    pdbPath = null;
                }

                var result = ProcessLauncher.Run(ilasmPath, arguments);

                if (result.ContainsErrors)
                {
                    throw new ArgumentException(
                        "The provided IL cannot be compiled." + Environment.NewLine +
                        ilasmPath + " " + arguments + Environment.NewLine +
                        result,
                        "declarations");
                }
            }
        }

#if OUT_OF_PROC_PEVERIFY
        /// <summary>
        /// Saves <paramref name="assembly"/> to a temp file and runs PEVerify out-of-proc.
        /// </summary>
        /// <returns>
        /// Return <c>null</c> if verification succeeds, return error messages otherwise.
        /// </returns>
        public static string RunPEVerify(byte[] assembly)
        {
            if (assembly == null) throw new ArgumentNullException("assembly");

            var pathToPEVerify = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"Microsoft SDKs\Windows\v7.0A\Bin\NETFX 4.0 Tools\PEVerify.exe");

            using (var tempDll = new TempFile("*.dll"))
            {
                File.WriteAllBytes(tempDll.FileName, assembly);

                var result = ProcessLauncher.Run(pathToPEVerify, "\"" + tempDll.FileName + "\"");
                return result.ContainsErrors
                           ? result.ToString()
                           : null;
            }
        }
#endif
    }

    static public class SharedResourceHelpers
    {
        public static void CleanupAllGeneratedFiles(string filename)
        {
            // This will cleanup all files with same name but different extension
            // These are often used by command line tests which use temp files.
            // The temp file dispose method cleans up that specific temp file 
            // but anything that was generated from this will not be removed by dispose

            string directory = System.IO.Path.GetDirectoryName(filename);
            string filenamewithoutextension = System.IO.Path.GetFileNameWithoutExtension(filename);
            string searchfilename = filenamewithoutextension + ".*";
            foreach (string f in System.IO.Directory.GetFiles(directory, searchfilename))
            {
                if (System.IO.Path.GetFileName(f) != System.IO.Path.GetFileName(filename))
                {
                    try
                    {
                        System.IO.File.Delete(f);
                    }
                    catch
                    {
                        // Swallow any exceptions as the cleanup should not necessarily block the test
                    }
                }
            }
        }
    }
}
