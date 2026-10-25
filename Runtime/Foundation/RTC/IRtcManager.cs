//
//  IRtcManager.cs
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
    /// The vendor-neutral room, video, and signaling transport interface.
    /// </summary>
    internal interface IRtcManager
    {

        /// <summary>
        /// Occurs when an active connection encounters an unrecoverable background failure.
        /// </summary>
        event Action<XmaxException> FatalError;

        /// <summary>
        /// Occurs when the active connection's directional network ratings change.
        /// </summary>
        event Action<RtcNetworkQuality> NetworkQualityChanged;

        /// <summary>Occurs for copied measurements of the current local video stream.</summary>
        event Action<UplinkVideoStatistics> UplinkVideoStatisticsChanged;

        /// <summary>Occurs for copied measurements of a resolved remote account.</summary>
        event Action<DownlinkVideoStatistics> DownlinkVideoStatisticsChanged;

        /// <summary>Occurs when native encoding becomes limited or recovers.</summary>
        event Action<RtcPerformanceAlarm> PerformanceAlarm;

        /// <summary>
        /// Occurs when a remote user publishes video in the current room.
        /// </summary>
        event Action<RemoteStream> VideoPublished;

        /// <summary>
        /// Occurs when a remote user stops publishing video in the current room.
        /// </summary>
        event Action<RemoteStream> VideoUnpublished;

        /// <summary>
        /// Occurs for an eligible video frame. Clone frames retained beyond the callback.
        /// </summary>
        event Action<RemoteStream, XmaxVideoFrame> FrameReceived;

        /// <summary>
        /// Rejects RTC startup outside Android Player before any server session is created.
        /// </summary>
        void ValidatePlatform();

        /// <summary>Acquires and initializes the exclusive engine before an online session is created.</summary>
        ///
        /// <remarks>Local preview outside Android Player leaves native RTC unloaded.</remarks>
        Task InitializeAsync(
            CancellationToken cancellationToken
        );

        /// <summary>Leaves the room and releases the engine and its exclusive lease.</summary>
        Task DestroyAsync();

        /// <summary>
        /// Joins a room using the initialized engine before external video publication.
        /// </summary>
        Task JoinAsync(
            RoomJoinConfiguration info,
            VideoEncodingConfiguration configuration,
            CancellationToken cancellationToken
        );

        /// <summary>
        /// Invalidates room callbacks and cancels signaling while retaining the engine for local media.
        /// </summary>
        Task LeaveAsync();

        /// <summary>Requests external video publication after successful room join.</summary>
        void PublishLocalVideo();

        /// <summary>
        /// Subscribes to the named remote user's video in the current room.
        /// </summary>
        void SubscribeVideo(
            RemoteStream key
        );

        /// <summary>
        /// Submits an external frame to the joined RTC room.
        /// </summary>
        void PushFrame(
            XmaxVideoFrame frame
        );

        /// <summary>
        /// Submits an ordered room message, completing after all native packets have been sent.
        /// </summary>
        ///
        /// <remarks>Cancellation stops waiting without cancelling admitted packets; leaving the room cancels the queue.</remarks>
        Task SendRoomMessageAsync(
            string message,
            CancellationToken cancellationToken = default,
            string replacingPendingMessage = null,
            bool reportFailure = true
        );

        /// <summary>
        /// Discards queued trajectories without dropping control messages.
        /// </summary>
        void DiscardPendingTracks();

        /// <summary>
        /// Applies a validated uplink encoding configuration.
        /// </summary>
        void ConfigureVideoEncoding(
            VideoEncodingConfiguration configuration
        );

    }
}
