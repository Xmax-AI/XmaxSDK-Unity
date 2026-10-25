//
//  IRoomController.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>Owns RTC room connection, generation signaling, and room heartbeat lifetime.</summary>
    internal interface IRoomController
    {

        /// <summary>Whether RTC join and signaling initialization have completed.</summary>
        bool IsConnected { get; }

        /// <summary>Sets independent media baseline, generation canvas, and return dimensions.</summary>
        void ConfigureMedia(
            RealtimeVideoFormat baseline,
            RealtimeVideoFormat generation,
            RealtimeVideoFormat target
        );

        /// <summary>Joins the room and starts its heartbeat after successful connection.</summary>
        Task ConnectAsync(
            RoomJoinConfiguration info,
            RealtimeVideoFormat format,
            CancellationToken token
        );

        /// <summary>Stops heartbeat, invalidates credentials, and leaves RTC.</summary>
        Task DisconnectAsync();

        /// <summary>Stops heartbeat before generation-stop signaling and room teardown.</summary>
        Task StopHeartbeatAsync();

        /// <summary>Returns whether a publication belongs to the expected result account in this room.</summary>
        bool Accept(
            RemoteStream stream
        );

        /// <summary>Sends all start packets for the specified generation task.</summary>
        Task StartGenerationAsync(
            string taskId,
            RealtimeContext context,
            RealtimeVideoFormat format,
            CancellationToken token,
            bool faceRestoration = false
        );

        /// <summary>Submits new conditions for an existing task.</summary>
        Task ChangeConditionAsync(
            string taskId,
            RealtimeContext context,
            RealtimeVideoFormat format,
            CancellationToken token,
            bool faceRestoration = false
        );

        /// <summary>Submits independent return dimensions for an existing task.</summary>
        Task ChangeTargetSizeAsync(
            string taskId,
            RealtimeVideoFormat format,
            CancellationToken token
        );

        /// <summary>Retains submitted return dimensions after the generation owner validates task identity.</summary>
        void CommitTargetSize(
            RealtimeVideoFormat format
        );

        /// <summary>Sends termination for a generation task, observing recoverable send failures.</summary>
        Task StopGenerationAsync(
            string taskId
        );

        /// <summary>Queues normalized interactions while coalescing older unsent trajectories.</summary>
        void SendTracks(
            string taskId,
            IReadOnlyList<RealtimePoint> tracks
        );
    }
}
