//
//  IRenderController.cs
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
    /// Owns remote track binding, first-frame readiness, and frame validity for Core workflows.
    /// </summary>
    ///
    /// <remarks>
    /// Call members on the creating Unity main thread. Frame arrays may be reused after delivery;
    /// clone frames before retaining them beyond a callback or a subsequent frame.
    /// </remarks>
    internal interface IRenderController
    {

        /// <summary>Replaces or removes the copied packet listener delivered on a serial background queue.</summary>
        void SetRemoteMediaFrameListener(
            Action<RealtimeMediaFrame> listener
        );

        /// <summary>The latest borrowed frame for the current generation, or null after reset.</summary>
        XmaxVideoFrame LatestFrame { get; }

        /// <summary>Occurs for valid frames after delivery to the bound remote track.</summary>
        event Action<XmaxVideoFrame> FrameReceived;

        /// <summary>Invalidates previous rendering and binds the new track; null clears the binding.</summary>
        void Bind(
            RealtimeVideoTrack track
        );

        /// <summary>Rebinds the confirmed remote stream, discarding old output and cancelling old frame waits.</summary>
        void SetRemoteStream(
            RemoteStream? stream
        );

        /// <summary>Clears the previous generation and creates its first-frame readiness signal.</summary>
        void BeginGeneration();

        /// <summary>Waits for a valid frame, failing on cancellation, rendering reset, or the deadline.</summary>
        ///
        /// <param name="cancellationToken">The caller's cancellation token.</param>
        /// <param name="timeout">The first-frame deadline, in milliseconds.</param>
        Task WaitUntilRemoteFrameReadyAsync(
            CancellationToken cancellationToken,
            int timeout = 10000
        );

        /// <summary>Validates and forwards a borrowed frame while the generation remains current.</summary>
        void Receive(
            XmaxVideoFrame frame
        );

        /// <summary>Invalidates generation callbacks, cancels frame waits, and clears the latest frame.</summary>
        void ResetGeneration();

        /// <summary>Resets generation rendering and releases the track binding.</summary>
        void Reset();
    }
}
