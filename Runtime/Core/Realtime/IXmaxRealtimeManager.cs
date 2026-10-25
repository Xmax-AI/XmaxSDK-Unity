//
//  IXmaxRealtimeManager.cs
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
using UnityEngine;

namespace Xmax.SDK
{

    /// <summary>
    /// Coordinates external media, realtime generation, and cleanup on the creating Unity main thread.
    /// </summary>
    ///
    /// <remarks>
    /// Create managers and call their APIs on the Unity main thread. Await asynchronous operations without
    /// blocking that thread. Local preview does not require credentials; online configuration is validated when
    /// connecting.
    /// </remarks>
    public interface IXmaxRealtimeManager
    {

        /// <summary>
        /// The immutable model and adaptive quality settings.
        /// </summary>
        RealtimeConfiguration Options { get; }

        /// <summary>
        /// The latest committed connection and generation state.
        /// </summary>
        RealtimeState CurrentState { get; }

        /// <summary>
        /// The applied uplink level, or null outside an active connection.
        /// </summary>
        RealtimeQualityLevel? UplinkQualityLevel { get; }

        /// <summary>
        /// The submitted downlink level, or null outside an active connection.
        /// </summary>
        RealtimeQualityLevel? DownlinkQualityLevel { get; }

        /// <summary>
        /// The current stable generation canvas.
        /// </summary>
        RealtimeVideoFormat? GenerationVideoFormat { get; }

        /// <summary>
        /// A snapshot of the successfully submitted transport settings.
        /// </summary>
        RealtimeQualityConfiguration QualityConfiguration { get; }

        /// <summary>
        /// Occurs after transport settings have been successfully submitted.
        /// </summary>
        event Action<RealtimeQualityConfiguration> QualityChanged;

        /// <summary>
        /// Changes uplink encoding while generating; later automatic adaptation remains enabled.
        /// </summary>
        Task SetUplinkQualityLevelAsync(
            RealtimeQualityLevel level,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Changes return dimensions while generating; completion means signaling was submitted.
        /// </summary>
        Task SetDownlinkQualityLevelAsync(
            RealtimeQualityLevel level,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// The current valid local stream, or null before creation or after closing.
        /// </summary>
        RealtimeMediaStream LocalStream { get; }

        /// <summary>
        /// Occurs when state changes. Registration immediately delivers the current state; subscriber exceptions do not interrupt SDK cleanup.
        /// </summary>
        event Action<RealtimeState> StateChanged;

        /// <summary>
        /// Occurs for valid frames of the active generation. Clone frames retained after the callback.
        /// </summary>
        event Action<XmaxVideoFrame> RemoteFrameReceived;

        /// <summary>Sets the remote video packet handler; passing null removes it.</summary>
        ///
        /// <remarks>
        /// Call this method on the Unity main thread. Callbacks run on a serial background queue and own
        /// their pixel arrays. Do not call Unity APIs from the callback. Output is bounded to 256 packets
        /// and 32 MiB; dropped frames mark the next packet as discontinuous. Passing null discards queued
        /// output; an in-flight callback may finish.
        /// </remarks>
        void SetRemoteMediaFrameListener(
            Action<RealtimeMediaFrame> listener
        );

        /// <summary>
        /// Occurs after fatal background failure cleanup. Awaited operation failures are returned through their
        /// tasks.
        /// </summary>
        event Action<XmaxException> ErrorOccurred;

        /// <summary>
        /// Occurs when server session closure fails after local connection resources have been released.
        /// </summary>
        event Action<XmaxException> CleanupWarning;

        /// <summary>
        /// Occurs when the active connection's directional network ratings change.
        /// </summary>
        event Action<RealtimeNetworkQuality> NetworkQualityChanged;

        /// <summary>Occurs for measured uplink video statistics, or null when unpublished or disconnected.</summary>
        event Action<UplinkVideoStatistics> UplinkVideoStatisticsChanged;

        /// <summary>Occurs for the current result user's video statistics, or null when its binding clears.</summary>
        event Action<DownlinkVideoStatistics> DownlinkVideoStatisticsChanged;

        /// <summary>Occurs when device encoding becomes limited or recovers, with a suggested format when valid.</summary>
        event Action<RealtimePerformanceAlarm> PerformanceAlarm;

        /// <summary>
        /// Creates or replaces an external input stream while disconnected. Media sizing preserves its aspect
        /// ratio within model limits.
        /// </summary>
        ///
        /// <remarks>
        /// The explicit format describes source dimensions and rate. VideoFormat on the returned stream is the
        /// resolved four-aligned media baseline. Your app owns camera permissions, capture, and input pacing.
        /// </remarks>
        RealtimeMediaStream CreateLocalExternalStream();

        /// <summary>
        /// Creates or replaces an external input stream while disconnected. Media sizing preserves its aspect
        /// ratio within model limits.
        /// </summary>
        ///
        /// <remarks>
        /// The explicit format describes source dimensions and rate. VideoFormat on the returned stream is the
        /// resolved four-aligned media baseline. Your app owns camera permissions, capture, and input pacing.
        /// </remarks>
        RealtimeMediaStream CreateLocalExternalStream(
            RealtimeVideoFormat format
        );

        /// <summary>Binds and starts a host-owned video source without creating an online session.</summary>
        ///
        /// <remarks>Start and stop run on the Unity main thread; frames may arrive through the sink from any capture thread.</remarks>
        Task<RealtimeMediaStream> CreateExternalVideoStreamAsync(
            IRealtimeExternalVideoSource source,
            CancellationToken cancellationToken = default
        );

        /// <summary>Stops the local external source or direct-input stream after realtime has been disconnected.</summary>
        Task StopExternalVideoStreamAsync(
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Creates a server session and joins RTC. Failure or cancellation rolls back the connection while
        /// preserving local media.
        /// </summary>
        ///
        /// <remarks>
        /// The stream must belong to this manager. Dimension overloads create a local external stream. Online
        /// RTC is supported only in Android Player.
        /// </remarks>
        Task<RealtimeMediaStream> ConnectAsync(
            RealtimeMediaStream localStream,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Creates a server session and joins RTC. Failure or cancellation rolls back the connection while
        /// preserving local media.
        /// </summary>
        ///
        /// <remarks>
        /// The stream must belong to this manager. Dimension overloads create a local external stream. Online
        /// RTC is supported only in Android Player.
        /// </remarks>
        Task<RealtimeMediaStream> ConnectAsync(
            RealtimeVideoFormat format,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Normalizes an external frame to the media baseline and transmits it during generation startup or execution.
        /// </summary>
        ///
        /// <remarks>
        /// Requires an active connection. Frames supplied before generation starts are not transmitted.
        /// Source-bound streams receive input through their external frame sink.
        /// Do not modify pixel arrays or strides during submission. Timestamps are in microseconds and are
        /// converted to milliseconds for RTC. Rotation is metadata; your app supplies correctly oriented input.
        /// </remarks>
        void PushVideoFrame(
            XmaxVideoFrame frame
        );

        /// <summary>
        /// Submits normalized media coordinates to the confirmed generation task.
        /// </summary>
        ///
        /// <remarks>
        /// Each Vector2 ranges from top-left (0, 0) to bottom-right (1, 1) across the full media.
        /// Values must be finite. Transport quality changes do not change this coordinate space.
        /// </remarks>
        void SendTracks(
            IReadOnlyList<Vector2> tracks
        );

        /// <summary>
        /// Starts or updates generation. Initial startup waits for result-stream confirmation and the first
        /// valid remote frame.
        /// </summary>
        ///
        /// <remarks>
        /// The first generation requires conditions. Null reuses the last successful conditions. Updating an
        /// active generation retains its task. Newer public generation requests replace unfinished requests.
        /// Startup failure or caller cancellation releases the connection. Condition updates complete after
        /// queue admission; cancellation does not retract admitted signaling or stop the existing generation.
        /// </remarks>
        Task StartGenerationAsync(
            RealtimeContext context = null,
            bool faceRestoration = false,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Starts or updates generation. Initial startup waits for result-stream confirmation and the first
        /// valid remote frame.
        /// </summary>
        ///
        /// <remarks>
        /// The first generation requires conditions. Null reuses the last successful conditions. Updating an
        /// active generation retains its task. Newer public generation requests replace unfinished requests.
        /// Startup failure or caller cancellation releases the connection. Condition updates complete after
        /// queue admission; cancellation does not retract admitted signaling or stop the existing generation.
        /// </remarks>
        Task<RealtimeMediaStream> StartGenerationAsync(
            RealtimeMediaStream localStream,
            RealtimeContext context,
            bool faceRestoration = false,
            CancellationToken cancellationToken = default
        );

        /// <summary>Starts or updates generation with explicit caller cancellation.</summary>
        Task StartGenerationAsync(
            RealtimeContext context,
            CancellationToken cancellationToken
        );

        /// <summary>Connects and starts generation with explicit caller cancellation.</summary>
        Task<RealtimeMediaStream> StartGenerationAsync(
            RealtimeMediaStream localStream,
            RealtimeContext context,
            CancellationToken cancellationToken
        );

        /// <summary>
        /// Stops generation, leaves the RTC room, and closes the session while retaining the engine and local preview.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>Disconnects realtime with an explicit normal, orientation-change, or failure reason.</summary>
        Task DisconnectAsync(
            RealtimeReason reason
        );

        /// <summary>
        /// Disconnects and invalidates local media. The manager can be reused after creating a new local
        /// stream.
        /// </summary>
        Task CloseAsync();
    }
}
