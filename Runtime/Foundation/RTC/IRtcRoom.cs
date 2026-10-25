//
//  IRtcRoom.cs
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
    /// Serves one native room with callbacks delivered on its creating Unity thread.
    /// </summary>
    ///
    /// <remarks>Frame buffers are owned by the adapter and may be reused after callback delivery.</remarks>
    internal interface IRtcRoom
    {

        /// <summary>Occurs when native room join succeeds.</summary>
        event Action Joined;

        /// <summary>Occurs when native connection or signaling fails.</summary>
        event Action<XmaxException> FatalError;

        /// <summary>Occurs when local directional network ratings change.</summary>
        event Action<RtcNetworkQuality> NetworkQualityChanged;

        /// <summary>Occurs for copied measurements of the current local video stream.</summary>
        event Action<UplinkVideoStatistics> UplinkVideoStatisticsChanged;

        /// <summary>Occurs for copied measurements of a resolved remote account.</summary>
        event Action<DownlinkVideoStatistics> DownlinkVideoStatisticsChanged;

        /// <summary>Occurs when native encoding becomes limited or recovers.</summary>
        event Action<RtcPerformanceAlarm> PerformanceAlarm;

        /// <summary>Occurs when an account publishes a video stream.</summary>
        event Action<RemoteStream> VideoPublished;

        /// <summary>Occurs when an account stops publishing video.</summary>
        event Action<RemoteStream> VideoUnpublished;

        /// <summary>Occurs for a copied decoded frame with a resolved application account.</summary>
        event Action<RemoteStream, XmaxVideoFrame> FrameReceived;

        /// <summary>Starts native join; completion is reported through Joined or FatalError.</summary>
        void Join(
            RoomJoinConfiguration info
        );

        /// <summary>Creates ordered signaling after join completion.</summary>
        void BeginSignaling();

        /// <summary>Invalidates callbacks, closes signaling, and leaves the native room.</summary>
        Task LeaveAsync();

        /// <summary>Requests external video publication after successful room join.</summary>
        void PublishLocalVideo();

        /// <summary>Subscribes to video for a mapped application account.</summary>
        void SubscribeVideo(
            RemoteStream stream
        );

        /// <summary>Submits every packet of one ordered signaling message.</summary>
        Task SendRoomMessageAsync(
            string message,
            CancellationToken token,
            string key,
            bool reportFailure
        );

        /// <summary>Discards coalescible trajectories that have not started sending.</summary>
        void DiscardPendingTracks();
    }
}
