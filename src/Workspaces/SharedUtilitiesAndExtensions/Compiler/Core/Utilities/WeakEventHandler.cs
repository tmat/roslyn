// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Reflection;

namespace Microsoft.CodeAnalysis;

internal readonly struct WeakEventHandler<TArgs>(EventHandler<TArgs>? handler)
{
    public EventHandler<TArgs>? Handler => handler;

    private static bool CapturesNoState(Delegate d)
        => d.Target == null || d.Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is [];

    /// <summary>
    /// Creates an event handler that holds onto the target weakly.
    /// </summary>
    /// <param name="target">The target that is held weakly, and passed as an argument to the invoker.</param>
    /// <param name="invoker">An action that will receive the event arguments as well as the target instance. 
    /// The invoker itself must not capture any state.</param>
    public static WeakEventHandler<TArgs> Create<TTarget>(TTarget target, Action<TTarget, object?, TArgs> invoker)
        where TTarget : class
    {
        Debug.Assert(CapturesNoState(invoker));

        var weakTarget = new WeakReference<TTarget>(target);

        return new((sender, args) =>
        {
            if (weakTarget.TryGetTarget(out var targ))
            {
                invoker(targ, sender, args);
            }
        });
    }
    /// <summary>
    /// Creates an event handler that holds onto the target weakly.
    /// </summary>
    /// <param name="target">The target that is held weakly, and passed as an argument to the invoker.</param>
    /// <param name="invoker">An action that will receive the event arguments as well as the target instance. 
    /// The invoker itself must not capture any state.</param>
    public static WeakEventHandler<TArgs> Create<TTarget>(TTarget target, Action<TTarget, TArgs> invoker)
        where TTarget : class
    {
        Debug.Assert(CapturesNoState(invoker));

        var weakTarget = new WeakReference<TTarget>(target);

        return new((_, args) =>
        {
            if (weakTarget.TryGetTarget(out var targ))
            {
                invoker(targ, args);
            }
        });
    }

    public static WeakEventHandler<TArgs> operator +(WeakEventHandler<TArgs> left, WeakEventHandler<TArgs> right)
        => new(left.Handler + right.Handler);

    public static WeakEventHandler<TArgs> operator -(WeakEventHandler<TArgs> left, WeakEventHandler<TArgs> right)
        => new(left.Handler - right.Handler);

    public void Invoke(object? sender, TArgs args)
        => handler?.Invoke(sender, args);

    public void Invoke(TArgs args)
        => handler?.Invoke(null, args);
}
