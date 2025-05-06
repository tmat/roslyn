// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis;

internal static class IReadOnlyListExtensions
{
    public static IReadOnlyList<T> ToReadOnlyList<T>(this IList<T> list)
    {
        if (list is IReadOnlyList<T> readOnlyList)
        {
            return readOnlyList;
        }

        return new ReadOnlyList<T>(list);
    }

    public static T Last<T>(this IReadOnlyList<T> list)
        => list[list.Count - 1];

    public static int IndexOf<T>(this IReadOnlyList<T> list, T value, int startIndex = 0)
    {
        for (var index = startIndex; index < list.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(list[index], value))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed class ReadOnlyList<T>(IList<T> list) : IReadOnlyList<T>
    {
        public T this[int index] => list[index];
        public int Count => list.Count;
        public IEnumerator<T> GetEnumerator() => list.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => list.GetEnumerator();
    }

    public static bool HasDuplicates<T>(this IReadOnlyList<T> builder)
        => builder.HasDuplicates(static x => x);

    public static bool HasDuplicates<T, U>(this IReadOnlyList<T> builder, Func<T, U> selector)
    {
        switch (builder.Count)
        {
            case 0:
            case 1:
                return false;

            case 2:
                return EqualityComparer<U>.Default.Equals(selector(builder[0]), selector(builder[1]));

            default:
                {
                    using var _ = PooledHashSet<U>.GetInstance(out var set);

                    foreach (var element in builder)
                    {
                        if (!set.Add(selector(element)))
                            return true;
                    }

                    return false;
                }
        }
    }
}
