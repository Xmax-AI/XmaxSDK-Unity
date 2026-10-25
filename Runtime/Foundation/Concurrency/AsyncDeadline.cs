//
//  AsyncDeadline.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>Waits for tasks with an independent deadline and caller cancellation.</summary>
    internal static class AsyncDeadline
    {

        /// <summary>Waits for completion, timeout, or cancellation without automatically stopping the underlying task.</summary>
        ///
        /// <remarks>The owner must cancel or observe the underlying work when waiting ends early.</remarks>
        internal static async Task WaitAsync(
            Task task,
            int milliseconds,
            CancellationToken cancellationToken
        )
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var delay = Task.Delay(
                    milliseconds,
                    timeout.Token
                );
                var completed = await Task.WhenAny(
                    task,
                    delay
                );
                timeout.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                if (completed != task)
                    throw new XmaxException(
                        XmaxErrorCode.Timeout,
                        "Realtime operation timed out."
                    );

                await task;
            }
        }
    }
}
