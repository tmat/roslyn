// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Serialization;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis.Text
{
    [DataContract]
    internal readonly record struct SourceTextData
    {
        [DataMember]
        public required SourceHashAlgorithm ChecksumAlgorithm { get; init; }

        [DataMember]
        public required Encoding Encoding { get; init; }

        [DataMember]
        public required string StorageName { get; init; }

        [DataMember]
        public required long StorageOffset { get; init; }

        [DataMember]
        public required long StorageLength { get; init; }

        public ValueTask<SourceText> DeserializeAsync(
            ITemporaryStorageServiceInternal storageService,
            CancellationToken cancellationToken)
        {
            var storage2 = (ITemporaryStorageService2)storageService;
            var storage = storage2.AttachTemporaryTextStorage(StorageName, StorageOffset, StorageLength, ChecksumAlgorithm, Encoding);

            var serializableSourceText = (storage is ITemporaryTextStorageWithName storageWithName) ?
                new SerializableSourceText(storageWithName) :
                new SerializableSourceText(storage.ReadText(cancellationToken));

            return serializableSourceText.GetTextAsync(cancellationToken);
        }
    }
}
