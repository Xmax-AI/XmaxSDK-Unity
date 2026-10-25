//
//  RtcEngineManager.cs
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

    /// <summary>
    /// Defines the Agora application and guards its process-wide engine with an exclusive lease.
    /// </summary>
    internal static class RtcEngineManager
    {

        /// <summary>
        /// The Agora application identifier used by Xmax realtime sessions.
        /// </summary>
        internal const string DefaultAppId = "5588dfe2e7874bd08ea28f4968ec2d40";

        /// <summary>Creates the native platform while the caller holds the exclusive engine lease.</summary>
        internal static IRtcPlatform CreatePlatform() => new AgoraRtcPlatform();

        /// <summary>
        /// The process-wide gate allowing one active RTC engine.
        /// </summary>
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(
            1,
            1
        );

        /// <summary>
        /// Waits for exclusive engine ownership until acquired or canceled.
        /// </summary>
        ///
        /// <returns>A lease that must be disposed after releasing the native engine.</returns>
        internal static async Task<IDisposable> AcquireAsync(
            CancellationToken cancellationToken
        )
        {
            await Gate.WaitAsync(cancellationToken);

            return new Lease();
        }

        /// <summary>
        /// Holds engine ownership until its first disposal.
        /// </summary>
        private sealed class Lease : IDisposable
        {

            /// <summary>
            /// Whether the lease has already returned engine ownership.
            /// </summary>
            private int _released;

            /// <summary>
            /// Returns engine ownership once, ignoring repeated disposal.
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(
                    ref _released,
                    1
                ) == 0)
                    Gate.Release();
            }
        }
    }
}
