//
//  IStreamController.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>
    /// The room signaling, result confirmation, and media transport coordinator.
    /// </summary>
    internal interface IStreamController
    {

        /// <summary>The connection's transport quality policies and history owner.</summary>
        IQualityController QualityController { get; }

        /// <summary>
        /// Occurs when an active connection encounters an unrecoverable background failure.
        /// </summary>
        event Action<XmaxException> FatalError;

        /// <summary>
        /// Occurs when the active connection's directional network ratings change.
        /// </summary>
        event Action<RealtimeNetworkQuality> NetworkQualityChanged;

        /// <summary>Occurs before public quality delivery with the sample's monotonic time in seconds.</summary>
        event Action<RealtimeNetworkQuality, double> NetworkQualitySampled;

        /// <summary>
        /// Occurs for an eligible video frame. Clone frames retained beyond the callback.
        /// </summary>
        event Action<XmaxVideoFrame> FrameReceived;

        /// <summary>Occurs when the confirmed remote stream changes; null invalidates its frames and waits.</summary>
        event Action<RemoteStream?> RemoteStreamChanged;

        /// <summary>
        /// Rejects RTC startup outside Android Player before any server session is created.
        /// </summary>
        void ValidatePlatform();

        /// <summary>Prepares the exclusive RTC engine before local source startup or session creation.</summary>
        Task InitializeAsync(
            CancellationToken cancellationToken
        );

        /// <summary>Releases the RTC engine after local media and connection shutdown; safe to repeat.</summary>
        Task ReleaseAsync();

        /// <summary>
        /// Joins the prepared session's RTC room and enables external video publication.
        /// Failure or cancellation rolls back the room while preserving local media and the engine.
        /// </summary>
        ///
        /// <remarks>
        /// Core creates the server session and owns its rollback. Online RTC is supported only in Android Player.
        /// </remarks>
        Task ConnectAsync(
            RoomJoinConfiguration info,
            RealtimeVideoFormat format,
            CancellationToken cancellationToken
        );

        /// <summary>
        /// Stops generation and leaves the RTC room while retaining the engine for local media.
        /// Core closes the associated server session.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Sends start signaling and confirms a matching published result stream; first-frame readiness is
        /// awaited by rendering.
        /// </summary>
        ///
        /// <remarks>
        /// The first generation requires conditions. Null reuses the last successful conditions. Updating an
        /// active generation retains its task. Newer public generation requests replace unfinished requests.
        /// Startup failure or caller cancellation releases the connection; cancelling a condition update
        /// preserves the existing generation.
        /// </remarks>
        Task StartGenerationAsync(
            string taskId,
            RealtimeContext context,
            RealtimeVideoFormat format,
            CancellationToken cancellationToken,
            bool faceRestoration = false
        );

        /// <summary>
        /// Submits conditions for the existing generation task without restarting it.
        /// </summary>
        ///
        /// <remarks>
        /// Completes when admitted to the signaling queue. Native send failures trigger connection cleanup.
        /// Cancellation or submission rejection preserves generation; admitted packets continue sending.
        /// </remarks>
        Task ChangeGenerationConditionAsync(
            RealtimeContext context,
            RealtimeVideoFormat format,
            string taskId,
            CancellationToken cancellationToken,
            bool faceRestoration = false
        );

        /// <summary>
        /// Invalidates the current generation and submits its stop signal while keeping the RTC room.
        /// </summary>
        Task StopGenerationAsync();

        /// <summary>
        /// Submits an external frame while a generation task exists, including startup; ignores frames otherwise.
        /// </summary>
        void PushFrame(
            XmaxVideoFrame frame
        );

        /// <summary>
        /// Submits normalized full-media coordinates for the active generation.
        /// </summary>
        ///
        /// <remarks>
        /// Coordinates range from top-left (0, 0) to bottom-right (1, 1).
        /// Transport quality changes do not change this coordinate space.
        /// </remarks>
        void SendTracks(
            IReadOnlyList<RealtimePoint> tracks,
            string taskId
        );

        /// <summary>
        /// Sets the media baseline, generation canvas, and initial return format.
        /// </summary>
        void ConfigureMedia(
            RealtimeVideoFormat baseline,
            RealtimeVideoFormat generation,
            RealtimeVideoFormat target
        );

        /// <summary>
        /// Applies the local uplink encoder format.
        /// </summary>
        void ConfigureVideoEncoding(
            RealtimeVideoFormat format
        );

        /// <summary>
        /// Submits new return dimensions for the current generation task.
        /// </summary>
        Task ChangeTargetSizeAsync(
            string taskId,
            RealtimeVideoFormat format,
            CancellationToken cancellationToken
        );
    }
}
