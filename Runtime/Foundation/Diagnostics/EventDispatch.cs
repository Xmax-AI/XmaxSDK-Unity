//
//  EventDispatch.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;

namespace Xmax.SDK
{

    /// <summary>
    /// Isolates subscriber failures and optionally rejects stale notifications after reentrancy.
    /// </summary>
    internal static class EventDispatch
    {

        /// <summary>Notifies parameterless listeners while isolating subscriber failures.</summary>
        internal static void Raise(
            Action handlers
        )
        {
            if (handlers == null)
                return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception exception)
                {
                    XmaxLogger.Realtime.Failure(exception);
                }
            }
        }

        /// <summary>
        /// Notifies valid subscribers in order while isolating individual subscriber failures.
        /// </summary>
        internal static void Raise<T>(
            Action<T> handlers,
            T value,
            Func<bool> isCurrent = null
        )
        {
            if (handlers == null)
                return;

            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                if (isCurrent != null && !isCurrent())
                    break;

                try
                {
                    handler(value);
                }
                catch (Exception exception)
                {
                    XmaxLogger.Realtime.Failure(exception);
                }
            }
        }

        /// <summary>
        /// Notifies two-argument listeners while isolating subscriber failures.
        /// </summary>
        internal static void Raise<TFirst, TSecond>(
            Action<TFirst, TSecond> handlers,
            TFirst first,
            TSecond second
        )
        {
            if (handlers == null)
                return;
            foreach (Action<TFirst, TSecond> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(
                        first,
                        second
                    );
                }
                catch (Exception exception)
                {
                    XmaxLogger.Realtime.Failure(exception);
                }
            }
        }
    }
}
