// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Collections
{
    internal sealed class SequenceEqualityComparer<T> : IEqualityComparer<ImmutableArray<T>>, IEqualityComparer<IEnumerable<T>>
    {
        public static readonly SequenceEqualityComparer<T> Default = new SequenceEqualityComparer<T>();

        private readonly EqualityComparer<T> _itemComparer;
        private readonly int _maxItemsToHash;

        public SequenceEqualityComparer(EqualityComparer<T>? itemComparer = null, int maxItemsToHash = int.MaxValue)
        {
            _itemComparer = itemComparer ?? EqualityComparer<T>.Default;
            _maxItemsToHash = maxItemsToHash;
        }

        public bool Equals(ImmutableArray<T> x, ImmutableArray<T> y)
            => x.SequenceEqual(y, _itemComparer);

        public bool Equals([AllowNull] IEnumerable<T> x, [AllowNull] IEnumerable<T> y)
            => x.SequenceEqual(y, _itemComparer);

        public int GetHashCode(ImmutableArray<T> obj)
            => Hash.CombineValues(obj, _maxItemsToHash);

        public int GetHashCode([DisallowNull] IEnumerable<T> obj)
            => Hash.CombineValues(obj, _maxItemsToHash);
    }
}
