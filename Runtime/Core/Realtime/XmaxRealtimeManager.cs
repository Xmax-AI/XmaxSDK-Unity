//
//  XmaxRealtimeManager.cs
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
    /// Coordinates external video, connection, generation, and transport quality on the creating Unity main
    /// thread.
    /// </summary>
    ///
    /// <remarks>
    /// Create managers and call their APIs on the Unity main thread. Await asynchronous operations without
    /// blocking that thread. Local preview does not require credentials; online configuration is validated when
    /// connecting.
    /// </remarks>
    public sealed class XmaxRealtimeManager : IXmaxRealtimeManager
    {

        /// <summary>
        /// The immutable client service and authentication configuration.
        /// </summary>
        private readonly XmaxConfiguration _configuration;

        /// <summary>
        /// The exclusive operation and cleanup coordinator.
        /// </summary>
        private readonly XmaxRealtimeLifecycleManager _lifecycle;

        /// <summary>The assembled graph sharing lifecycle, transport, and rendering dependencies.</summary>
        private readonly XmaxRealtimeComponents _components;

        /// <summary>
        /// The room signaling and media transport controller.
        /// </summary>
        private readonly IStreamController _stream;

        /// <summary>
        /// The server session and RTC connection coordinator.
        /// </summary>
        private readonly XmaxRealtimeConnectionManager _connection;

        /// <summary>
        /// The generation conditions and first-frame coordinator.
        /// </summary>
        private readonly XmaxRealtimeGenerationManager _generation;

        /// <summary>
        /// The local media stream and frame normalization controller.
        /// </summary>
        private readonly IMediaController _media;

        /// <summary>
        /// The remote track and first-frame readiness controller.
        /// </summary>
        private readonly IRenderController _render;

        /// <summary>
        /// The shared startup diagnostics timer.
        /// </summary>
        private readonly RealtimeTiming _timing;

        /// <summary>
        /// The stable media baseline, unchanged by transport adaptation.
        /// </summary>
        private RealtimeVideoFormat _videoFormat;

        /// <summary>
        /// The generation canvas, independent of transport quality.
        /// </summary>
        private RealtimeVideoFormat _generationFormat;

        /// <summary>
        /// Whether one automatic adaptation already owns an operation.
        /// </summary>
        private bool _qualityPending;

        /// <summary>The revision rejecting superseded uplink statistics during subscriber reentrancy.</summary>
        private long _uplinkStatisticsRevision;

        /// <summary>The revision rejecting superseded downlink statistics during subscriber reentrancy.</summary>
        private long _downlinkStatisticsRevision;

        /// <summary>
        /// The observed automatic adaptation task.
        /// </summary>
        private Task _adaptation = Task.CompletedTask;

        /// <summary>
        /// The applied uplink level, or null outside an active connection.
        /// </summary>
        public RealtimeQualityLevel? UplinkQualityLevel => IsConnected ? _connection.QualityConfiguration?.UplinkLevel : null;

        /// <summary>
        /// The submitted downlink level, or null outside an active connection.
        /// </summary>
        public RealtimeQualityLevel? DownlinkQualityLevel => IsConnected ? _connection.QualityConfiguration?.DownlinkLevel : null;

        /// <summary>
        /// The stable generation canvas, or null before connecting.
        /// </summary>
        public RealtimeVideoFormat? GenerationVideoFormat => IsConnected ? _generationFormat : (RealtimeVideoFormat?)null;

        /// <summary>
        /// A snapshot of requested transport formats, separate from measured frame statistics.
        /// </summary>
        public RealtimeQualityConfiguration QualityConfiguration => IsConnected ? _connection.QualityConfiguration : null;

        /// <summary>
        /// Occurs after a transport configuration has been successfully submitted.
        /// </summary>
        public event Action<RealtimeQualityConfiguration> QualityChanged;

        /// <summary>
        /// Whether this manager currently has a connected session.
        /// </summary>
        private bool IsConnected => _connection.ActiveSession != null &&
            (CurrentState.ConnectionState == RealtimeConnectionState.Connected ||
             CurrentState.ConnectionState == RealtimeConnectionState.Generating);

        /// <summary>
        /// The immutable model and adaptive quality settings.
        /// </summary>
        public RealtimeConfiguration Options { get; }

        /// <summary>
        /// The latest committed connection and generation state.
        /// </summary>
        public RealtimeState CurrentState => _lifecycle.CurrentState;

        /// <summary>
        /// The current valid local stream, or null before creation or after closing.
        /// </summary>
        public RealtimeMediaStream LocalStream => _media.LocalStream;

        /// <summary>
        /// Occurs when state changes. Registration immediately delivers the current state; completed operations release admission before notification.
        /// </summary>
        public event Action<RealtimeState> StateChanged
        {
            add
            {
                _lifecycle.RequireThread();
                _lifecycle.StateChanged += value;
                EventDispatch.Raise(
                    value,
                    CurrentState
                );
            }
            remove
            {
                _lifecycle.RequireThread();
                _lifecycle.StateChanged -= value;
            }
        }

        /// <summary>
        /// Occurs for valid frames of the active generation. Clone frames retained after the callback.
        /// </summary>
        public event Action<XmaxVideoFrame> RemoteFrameReceived
        {
            add
            {
                _render.FrameReceived += value;
            }
            remove
            {
                _render.FrameReceived -= value;
            }
        }

        /// <inheritdoc/>
        public void SetRemoteMediaFrameListener(
            Action<RealtimeMediaFrame> listener
        )
        {
            _lifecycle.RequireThread();
            _render.SetRemoteMediaFrameListener(listener);
        }

        /// <summary>
        /// Occurs after fatal background failure cleanup. Awaited operation failures are returned through their
        /// tasks.
        /// </summary>
        public event Action<XmaxException> ErrorOccurred
        {
            add
            {
                _lifecycle.RequireThread();
                _lifecycle.Failed += value;
            }
            remove
            {
                _lifecycle.RequireThread();
                _lifecycle.Failed -= value;
            }
        }

        /// <summary>
        /// Occurs when server session closure fails after local connection resources have been released.
        /// </summary>
        public event Action<XmaxException> CleanupWarning;

        /// <summary>
        /// Occurs when the active connection's directional network ratings change.
        /// </summary>
        public event Action<RealtimeNetworkQuality> NetworkQualityChanged;

        /// <summary>Occurs for measured uplink video statistics, or null when unpublished or disconnected.</summary>
        public event Action<UplinkVideoStatistics> UplinkVideoStatisticsChanged;

        /// <summary>Occurs for the current result user's video statistics, or null when its binding clears.</summary>
        public event Action<DownlinkVideoStatistics> DownlinkVideoStatisticsChanged;

        /// <summary>Occurs when device encoding becomes limited or recovers, with a suggested format when valid.</summary>
        public event Action<RealtimePerformanceAlarm> PerformanceAlarm;

        /// <summary>
        /// Creates a realtime manager on the Unity main thread without starting a server session.
        /// </summary>
        ///
        /// <remarks>
        /// Create managers and call their APIs on the Unity main thread. Await asynchronous operations without
        /// blocking that thread. Local preview does not require credentials; online configuration is validated
        /// when connecting.
        /// </remarks>
        public XmaxRealtimeManager(
            string apiKey,
            RealtimeModel model = RealtimeModel.X2_1_1_Preview_CN,
            string baseUrl = XmaxConfiguration.DefaultBaseUrl,
            XmaxLoggerOption loggerOptions = XmaxLoggerOption.None
        )
            : this(
                new XmaxClient(new XmaxConfiguration(
                    apiKey,
                    baseUrl,
                    loggerOptions
                )),
                new RealtimeConfiguration(model)
            )
        {
        }

        /// <summary>
        /// Creates a realtime manager on the Unity main thread without starting a server session.
        /// </summary>
        ///
        /// <remarks>
        /// Create managers and call their APIs on the Unity main thread. Await asynchronous operations without
        /// blocking that thread. Local preview does not require credentials; online configuration is validated
        /// when connecting.
        /// </remarks>
        private XmaxRealtimeManager(
            XmaxClient client,
            RealtimeConfiguration options
        )
            : this(
                client.Configuration,
                options
            )
        {
        }

        /// <summary>
        /// Creates a realtime manager on the Unity main thread without starting a server session.
        /// </summary>
        ///
        /// <remarks>
        /// Create managers and call their APIs on the Unity main thread. Await asynchronous operations without
        /// blocking that thread. Local preview does not require credentials; online configuration is validated
        /// when connecting.
        /// </remarks>
        internal XmaxRealtimeManager(
            XmaxConfiguration configuration,
            RealtimeConfiguration options
        )
            : this(
                configuration,
                options,
                null,
                null
            )
        {
        }

        /// <summary>
        /// Creates a realtime manager on the Unity main thread without starting a server session.
        /// </summary>
        ///
        /// <remarks>
        /// Create managers and call their APIs on the Unity main thread. Await asynchronous operations without
        /// blocking that thread. Local preview does not require credentials; online configuration is validated
        /// when connecting.
        /// </remarks>
        internal XmaxRealtimeManager(
            XmaxConfiguration configuration,
            RealtimeConfiguration options,
            IRealtimeSessionService sessions,
            IStreamController stream,
            RealtimeTiming timing = null
        )
            : this(
                configuration,
                options,
                XmaxRealtimeFactory.Make(
                    configuration,
                    sessions,
                    stream,
                    timing
                )
            )
        {
        }

        /// <summary>Creates a realtime facade using the complete supplied component graph.</summary>
        internal XmaxRealtimeManager(
            XmaxConfiguration configuration,
            RealtimeConfiguration options,
            XmaxRealtimeComponents components
        )
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _components = components ?? throw new ArgumentNullException(nameof(components));
            _lifecycle = _components.Lifecycle;
            _stream = _components.Stream;
            _connection = _components.Connection;
            _generation = _components.Generation;
            _media = _components.Media;
            _render = _components.Render;
            _timing = _components.Timing;
            _connection.QualityChanged += quality => EventDispatch.Raise(
                QualityChanged,
                quality
            );
            _stream.QualityController.UplinkVideoStatisticsChanged += ReceiveUplinkStatistics;
            _stream.QualityController.DownlinkVideoStatisticsChanged += ReceiveDownlinkStatistics;
            _stream.QualityController.PerformanceAlarm += alarm =>
            {
                var session = _connection.ActiveSession;
                if (session != null && !_lifecycle.IsTerminating)
                    EventDispatch.Raise(
                        PerformanceAlarm,
                        alarm,
                        () => ReferenceEquals(
                            session,
                            _connection.ActiveSession
                        ) && !_lifecycle.IsTerminating
                    );
            };
            _stream.NetworkQualitySampled += ObserveQuality;
            _stream.NetworkQualityChanged += quality =>
            {
                var session = _connection.ActiveSession;
                if (!_lifecycle.IsTerminating && session != null)
                    EventDispatch.Raise(
                        NetworkQualityChanged,
                        quality,
                        () => ReferenceEquals(
                            session,
                            _connection.ActiveSession
                        ) && !_lifecycle.IsTerminating
                    );
            };
            _connection.CleanupError += error => EventDispatch.Raise(
                CleanupWarning,
                error
            );
        }

        /// <summary>Delivers available uplink measurements or a reset while rejecting superseded callbacks.</summary>
        private void ReceiveUplinkStatistics(
            UplinkVideoStatistics statistics
        )
        {
            if (statistics != null && (!IsConnected || _lifecycle.IsTerminating))
                return;
            var revision = ++_uplinkStatisticsRevision;
            EventDispatch.Raise(
                UplinkVideoStatisticsChanged,
                statistics,
                () => revision == _uplinkStatisticsRevision &&
                (statistics == null || IsConnected && !_lifecycle.IsTerminating)
            );
        }

        /// <summary>Delivers result-user measurements or a reset while rejecting superseded callbacks.</summary>
        private void ReceiveDownlinkStatistics(
            DownlinkVideoStatistics statistics
        )
        {
            if (statistics != null && (!IsConnected || _lifecycle.IsTerminating))
                return;
            var revision = ++_downlinkStatisticsRevision;
            EventDispatch.Raise(
                DownlinkVideoStatisticsChanged,
                statistics,
                () => revision == _downlinkStatisticsRevision &&
                (statistics == null || IsConnected && !_lifecycle.IsTerminating)
            );
        }

        /// <summary>
        /// Creates or replaces an external input stream while disconnected. Media sizing preserves its aspect
        /// ratio within model limits.
        /// </summary>
        ///
        /// <remarks>
        /// The explicit format describes source dimensions and rate. VideoFormat on the returned stream is the
        /// resolved four-aligned media baseline. Your app owns camera permissions, capture, and input pacing.
        /// </remarks>
        public RealtimeMediaStream CreateLocalExternalStream()
        {
            _lifecycle.RequireAvailable();
            var capabilities = Options.Definition.Capabilities;

            return CreateLocalExternalStream(capabilities.DefaultCameraVideoFormat);
        }

        /// <summary>
        /// Creates or replaces an external input stream while disconnected. Media sizing preserves its aspect
        /// ratio within model limits.
        /// </summary>
        ///
        /// <remarks>
        /// The explicit format describes source dimensions and rate. VideoFormat on the returned stream is the
        /// resolved four-aligned media baseline. Your app owns camera permissions, capture, and input pacing.
        /// </remarks>
        public RealtimeMediaStream CreateLocalExternalStream(
            RealtimeVideoFormat format
        )
        {
            _lifecycle.RequireAvailable();
            if (_lifecycle.HasOperation || _connection.ActiveSession != null)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Disconnect before replacing the local source."
                );

            ValidateVideoFormat(format);

            return _lifecycle.RunMedia(() => _media.CreateExternalStream(
                    format,
                    ResolveMediaFormat(format)
                ));
        }

        /// <summary>Binds and starts a host-owned video source without creating an online session.</summary>
        ///
        /// <remarks>Call on the Unity main thread. The source delivers frames through its sink; disconnect retains the source and close stops it.</remarks>
        public Task<RealtimeMediaStream> CreateExternalVideoStreamAsync(
            IRealtimeExternalVideoSource source,
            CancellationToken cancellationToken = default
        )
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            return _lifecycle.RunAsync(
                RealtimeOperation.Media,
                async operation =>
                {
                    if (_connection.ActiveSession != null || LocalStream != null)
                        throw new XmaxException(
                            XmaxErrorCode.InvalidConfiguration,
                            "Disconnect and stop the current local stream before binding an external source."
                        );
                    var requested = source.VideoFormat;
                    ValidateVideoFormat(requested);
                    var format = ResolveMediaFormat(requested);
                    _lifecycle.Commit(
                        new RealtimeState(RealtimeConnectionState.Preparing),
                        operation
                    );
                    var local = await _media.CreateExternalStreamAsync(
                        source,
                        format,
                        operation.Token
                    );
                    operation.EnsureCurrent();
                    _lifecycle.Complete(
                        new RealtimeState(RealtimeConnectionState.Ready),
                        operation
                    );
                    return local;
                },
                cancellationToken
            );
        }

        /// <summary>Stops the local external source or direct-input stream and returns to Idle after disconnection.</summary>
        public Task StopExternalVideoStreamAsync(
            CancellationToken cancellationToken = default
        )
        {
            return _lifecycle.RunAsync(
                RealtimeOperation.Media,
                async operation =>
                {
                    if (_connection.ActiveSession != null)
                        throw new XmaxException(
                            XmaxErrorCode.InvalidConfiguration,
                            "Disconnect realtime before stopping the external stream."
                        );
                    operation.FailureScope = TerminationScope.All;
                    await _media.CloseAsync();
                    operation.EnsureCurrent();
                    _lifecycle.Complete(
                        new RealtimeState(RealtimeConnectionState.Idle),
                        operation
                    );
                },
                cancellationToken
            );
        }

        /// <summary>
        /// Creates a server session and joins RTC. Failure or cancellation rolls back the connection while
        /// preserving local media.
        /// </summary>
        ///
        /// <remarks>
        /// The stream must belong to this manager. Dimension overloads create a local external stream. Online
        /// RTC is supported only in Android Player.
        /// </remarks>
        public Task<RealtimeMediaStream> ConnectAsync(
            RealtimeMediaStream localStream,
            CancellationToken cancellationToken = default
        )
        {
            _lifecycle.RequireAvailable();

            return ConnectCoreAsync(
                _media.RequireOwned(localStream),
                false,
                cancellationToken
            );
        }

        /// <summary>
        /// Creates a server session and joins RTC. Failure or cancellation rolls back the connection while
        /// preserving local media.
        /// </summary>
        ///
        /// <remarks>
        /// The stream must belong to this manager. Dimension overloads create a local external stream. Online
        /// RTC is supported only in Android Player.
        /// </remarks>
        public Task<RealtimeMediaStream> ConnectAsync(
            RealtimeVideoFormat videoFormat,
            CancellationToken cancellationToken = default
        )
            => ConnectCoreAsync(
                videoFormat,
                true,
                cancellationToken
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
        public Task<RealtimeMediaStream> ConnectAsync(
            int width,
            int height,
            int fps,
            CancellationToken cancellationToken = default
        )
            => ConnectAsync(
                new RealtimeVideoFormat(
                width,
                height,
                fps
            ),
                cancellationToken
            );

        /// <summary>
        /// Validates configuration and admits an exclusive connection operation.
        /// </summary>
        private async Task<RealtimeMediaStream> ConnectCoreAsync(
            RealtimeVideoFormat format,
            bool createSource,
            CancellationToken cancellationToken
        )
        {
            return await _lifecycle.RunAsync(
                RealtimeOperation.Connection,
                async operation =>
                {
                    if (_connection.ActiveSession != null)
                        throw new XmaxException(
                            XmaxErrorCode.InvalidConfiguration,
                            "Realtime connection is already open."
                        );
                    var remote = await PerformConnectAsync(
                        format,
                        createSource,
                        operation,
                        true
                    );
                    return remote;
                },
                cancellationToken
            );
        }

        /// <summary>Establishes a connection and stages or commits its state within the admitted operation.</summary>
        private async Task<RealtimeMediaStream> PerformConnectAsync(
            RealtimeVideoFormat format,
            bool createSource,
            XmaxRealtimeLifecycleManager.Operation operation,
            bool completesOperation = false
        )
        {
            ValidateVideoFormat(format);
            _configuration.Validate();
            _stream.ValidatePlatform();
            operation.FailureScope = TerminationScope.Connection;
            var sourceFormat = format;
            format = ResolveMediaFormat(format);
            if (createSource)
                _media.CreateExternalStream(
                    sourceFormat,
                    format
                );
            _videoFormat = format;
            _generationFormat = ResolveGenerationFormat(format);
            var quality = _connection.PrepareQuality(
                format,
                _generationFormat,
                Options,
                operation.EnsureCurrent
            );
            _generation.ResetContext();
            _lifecycle.Commit(
                new RealtimeState(RealtimeConnectionState.Connecting),
                operation
            );
            var remote = await _connection.ConnectAsync(
                Options.Definition.Name,
                quality.Uplink,
                operation.Token
            );
            operation.EnsureCurrent();
            var state = new RealtimeState(
                RealtimeConnectionState.Connected,
                _connection.ActiveSession.SessionUid
            );
            if (completesOperation)
                _lifecycle.Complete(
                    state,
                    operation
                );
            else
                _lifecycle.Commit(
                    state,
                    operation
                );
            return remote;
        }

        /// <summary>
        /// Normalizes direct input to the media baseline and transmits it during generation startup or execution.
        /// </summary>
        ///
        /// <remarks>
        /// Do not modify pixel arrays or strides during submission. Timestamps are in microseconds and are
        /// converted to milliseconds for RTC. Rotation is metadata; your app supplies correctly oriented input.
        /// </remarks>
        public void PushVideoFrame(
            XmaxVideoFrame frame
        )
        {
            _lifecycle.RequireAvailable();
            RequireConnection();
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            frame.Validate();
            _stream.PushFrame(_media.NormalizeFrame(frame));
        }

        /// <summary>
        /// Submits RGBA input to the active connection after normalizing its dimensions to the media baseline.
        /// </summary>
        ///
        /// <remarks>
        /// Do not modify pixel arrays or strides during submission. Timestamps are in microseconds and are
        /// converted to milliseconds for RTC. Rotation is metadata; your app supplies correctly oriented input.
        /// </remarks>
        public void PushRgbaFrame(
            byte[] rgba,
            int width,
            int height,
            long timestampMicroseconds = 0,
            int stride = 0,
            XmaxVideoRotation rotation = XmaxVideoRotation.Rotation0
        )
        {
            PushVideoFrame(XmaxVideoFrame.CreateRgba(
                    rgba,
                    width,
                    height,
                    stride,
                    timestampMicroseconds,
                    rotation
                ));
        }

        /// <summary>
        /// Submits I420 input to the active connection after normalizing its dimensions to the media baseline.
        /// </summary>
        ///
        /// <remarks>
        /// Do not modify pixel arrays or strides during submission. Timestamps are in microseconds and are
        /// converted to milliseconds for RTC. Rotation is metadata; your app supplies correctly oriented input.
        /// </remarks>
        public void PushI420Frame(
            byte[] y,
            byte[] u,
            byte[] v,
            int width,
            int height,
            long timestampMicroseconds = 0,
            int strideY = 0,
            int strideU = 0,
            int strideV = 0,
            XmaxVideoRotation rotation = XmaxVideoRotation.Rotation0
        )
        {
            PushVideoFrame(XmaxVideoFrame.CreateI420(
                    y,
                    u,
                    v,
                    width,
                    height,
                    strideY,
                    strideU,
                    strideV,
                    timestampMicroseconds,
                    rotation
                ));
        }

        /// <summary>
        /// Submits normalized media coordinates to the confirmed generation task.
        /// </summary>
        ///
        /// <remarks>
        /// Each Vector2 ranges from top-left (0, 0) to bottom-right (1, 1) across the full media.
        /// Values must be finite. Transport quality changes do not change this coordinate space.
        /// </remarks>
        public void SendTracks(
            IReadOnlyList<Vector2> tracks
        )
        {
            _lifecycle.RequireAvailable();
            _generation.SendTracks(tracks);
        }

        /// <summary>Starts generation or updates active conditions; a newer call replaces unfinished generation work.</summary>
        ///
        /// <remarks>Initial startup failures release the connection. Cancelling an update preserves active generation.</remarks>
        public Task StartGenerationAsync(
            RealtimeContext context = null,
            bool faceRestoration = false,
            CancellationToken cancellationToken = default
        )
        {
            return _lifecycle.RunGenerationAsync(
                async operation =>
                {
                    RequireConnection();
                    _generation.ValidateContext(context);
                    var initial = _generation.TaskId == null;
                    if (initial)
                        _timing.Begin();
                    try
                    {
                        await PerformGenerationAsync(
                            context,
                            faceRestoration,
                            operation
                        );
                    }
                    catch (Exception exception)
                    {
                        if (initial)
                            _timing.FinishFailure(exception);
                        throw;
                    }
                },
                cancellationToken
            );
        }

        /// <summary>Starts generation with explicit caller cancellation.</summary>
        public Task StartGenerationAsync(
            RealtimeContext context,
            CancellationToken cancellationToken
        )
            => StartGenerationAsync(
                context,
                false,
                cancellationToken
            );

        /// <summary>Connects an owned local stream if needed and starts or updates generation.</summary>
        ///
        /// <remarks>Conditions are validated before creating a billable session. Newer requests replace pending generation work.</remarks>
        public Task<RealtimeMediaStream> StartGenerationAsync(
            RealtimeMediaStream localStream,
            RealtimeContext context,
            bool faceRestoration = false,
            CancellationToken cancellationToken = default
        )
        {
            return _lifecycle.RunGenerationAsync(
                async operation =>
                {
                    var format = _media.RequireOwned(localStream);
                    _generation.ValidateContext(context);
                    var initial = _generation.TaskId == null;
                    if (initial)
                        _timing.Begin();
                    try
                    {
                        var remote = _connection.CurrentRemoteStream;
                        if (_connection.ActiveSession == null)
                            remote = await PerformConnectAsync(
                                format,
                                false,
                                operation
                            );
                        operation.EnsureCurrent();
                        if (remote == null)
                            throw new XmaxException(
                                XmaxErrorCode.RtcError,
                                "Realtime connection has no remote stream."
                            );
                        await PerformGenerationAsync(
                            context,
                            faceRestoration,
                            operation
                        );
                        return remote;
                    }
                    catch (Exception exception)
                    {
                        if (initial)
                            _timing.FinishFailure(exception);
                        throw;
                    }
                },
                cancellationToken
            );
        }

        /// <summary>Connects and starts generation with explicit caller cancellation.</summary>
        public Task<RealtimeMediaStream> StartGenerationAsync(
            RealtimeMediaStream localStream,
            RealtimeContext context,
            CancellationToken cancellationToken
        ) => StartGenerationAsync(
            localStream,
            context,
            false,
            cancellationToken
        );

        /// <summary>Applies conditions or starts generation and stages readiness after result confirmation and the first frame.</summary>
        private async Task PerformGenerationAsync(
            RealtimeContext context,
            bool faceRestoration,
            XmaxRealtimeLifecycleManager.Operation operation
        )
        {
            var initial = _generation.TaskId == null;
            if (initial)
                operation.FailureScope = TerminationScope.Connection;
            var taskId = await _generation.StartAsync(
                context,
                _videoFormat,
                operation.Token,
                faceRestoration
            );
            operation.EnsureCurrent();
            if (!initial)
                return;
            _connection.CompleteInitialQuality();
            operation.EnsureCurrent();
            _lifecycle.Complete(
                new RealtimeState(
                    RealtimeConnectionState.Generating,
                    _connection.ActiveSession.SessionUid,
                    taskId
                ),
                operation
            );
            _timing.Finish(taskId);
        }

        /// <summary>Starts or updates generation using prompt and reference conditions.</summary>
        public Task StartGenerationAsync(
            string prompt,
            string referencePath = null,
            bool faceRestoration = false,
            CancellationToken cancellationToken = default
        )
            => StartGenerationAsync(
                new RealtimeContext(
                prompt,
                referencePath
            ),
                faceRestoration,
                cancellationToken
            );

        /// <summary>Starts or updates generation using prompt conditions and explicit cancellation.</summary>
        public Task StartGenerationAsync(
            string prompt,
            string referencePath,
            CancellationToken cancellationToken
        )
            => StartGenerationAsync(
                prompt,
                referencePath,
                false,
                cancellationToken
            );

        /// <summary>Disconnects realtime and returns to Ready when local media remains, or Idle otherwise.</summary>
        public Task DisconnectAsync() => _lifecycle.TerminateAsync(TerminationScope.Connection);

        /// <summary>Disconnects realtime with an explicit normal or orientation-change reason.</summary>
        public Task DisconnectAsync(
            RealtimeReason reason
        )
        {
            if (reason == null)
                throw new ArgumentNullException(nameof(reason));
            return _lifecycle.TerminateAsync(
                TerminationScope.Connection,
                reason.Error,
                reason
            );
        }

        /// <summary>Releases connection and local media and returns to Idle.</summary>
        public Task CloseAsync() => _lifecycle.TerminateAsync(TerminationScope.All);

        /// <summary>
        /// Validates the resolved media baseline and merged uplink bitrate settings without creating a session.
        /// </summary>
        private void ValidateVideoFormat(
            RealtimeVideoFormat format
        )
        {
            EncodingBitratePolicy.Resolve(ResolveMediaFormat(format));
        }

        /// <summary>
        /// Resolves the external media baseline, preserving explicit bitrate and frame-rate settings.
        /// </summary>
        private RealtimeVideoFormat ResolveMediaFormat(
            RealtimeVideoFormat format
        )
        {
            format.Validate();
            var size = _components.MediaService.ResolveMediaSize(
                Options.Model,
                format.Width,
                format.Height
            );
            return format.Resized(
                size.Width,
                size.Height
            );
        }

        /// <summary>
        /// Resolves the server generation canvas independently of transport quality.
        /// </summary>
        private RealtimeVideoFormat ResolveGenerationFormat(
            RealtimeVideoFormat baseline
        )
        {
            var size = _components.MediaService.ResolveGenerationSize(
                Options.Model,
                baseline.Width,
                baseline.Height
            );
            return baseline.Resized(
                size.Width,
                size.Height
            );
        }

        /// <summary>
        /// Changes uplink encoding while allowing subsequent automatic adaptation.
        /// </summary>
        public Task SetUplinkQualityLevelAsync(
            RealtimeQualityLevel level,
            CancellationToken cancellationToken = default
        )
            => SetQualityLevelAsync(
                level,
                false,
                cancellationToken
            );

        /// <summary>
        /// Changes return dimensions while allowing subsequent automatic adaptation.
        /// </summary>
        public Task SetDownlinkQualityLevelAsync(
            RealtimeQualityLevel level,
            CancellationToken cancellationToken = default
        )
            => SetQualityLevelAsync(
                level,
                true,
                cancellationToken
            );

        /// <summary>
        /// Admits a manual configuration operation for the current generation.
        /// </summary>
        private async Task SetQualityLevelAsync(
            RealtimeQualityLevel level,
            bool downlink,
            CancellationToken cancellationToken
        )
        {
            QualityVideoFormat.ValidateLevel(level);
            await _lifecycle.RunAsync(
                RealtimeOperation.Configuration,
                async operation =>
                {
                    if (CurrentState.ConnectionState != RealtimeConnectionState.Generating)
                        throw new XmaxException(
                            XmaxErrorCode.InvalidConfiguration,
                            "Quality levels can only be changed while generating."
                        );
                    await _connection.ApplyQualityLevelAsync(
                        level,
                        downlink,
                        _generation.TaskId,
                        operation.Token,
                        operation.EnsureCurrent
                    );
                },
                cancellationToken
            );
        }

        /// <summary>Queues a sampled network rating before public callbacks while retaining its original time and task identity.</summary>
        private void ObserveQuality(
            RealtimeNetworkQuality quality,
            double sampledAt
        )
        {
            if (!Options.IsAdaptiveQualityEnabled || CurrentState.ConnectionState != RealtimeConnectionState.Generating)
                return;
            if (_qualityPending)
                return;
            if (_lifecycle.HasOperation || _lifecycle.IsTerminating)
            {
                ResetQualitySamples();
                return;
            }
            _qualityPending = true;
            _adaptation = AdaptQualityAsync(
                quality,
                sampledAt,
                _generation.TaskId
            );
        }

        /// <summary>Admits a timestamped sample and applies directional changes only to the generation that produced it.</summary>
        private async Task AdaptQualityAsync(
            RealtimeNetworkQuality quality,
            double sampledAt,
            string taskId
        )
        {
            try
            {
                await Task.Yield();
                var accepted = await _lifecycle.RunAdaptiveAsync(async operation =>
                {
                    if (CurrentState.TaskId != taskId)
                        return;
                    var proposal = _stream.QualityController.Propose(
                        quality,
                        sampledAt
                    );
                    if (proposal.Uplink.HasValue)
                    {
                        try
                        {
                            await _connection.ApplyQualityLevelAsync(
                                proposal.Uplink.Value,
                                false,
                                taskId,
                                operation.Token,
                                operation.EnsureCurrent
                            );
                        }
                        catch (XmaxException exception)
                        {
                            XmaxRealtimeErrorManager.Report(exception);
                        }
                    }
                    operation.EnsureCurrent();
                    if (proposal.Downlink.HasValue)
                    {
                        try
                        {
                            await _connection.ApplyQualityLevelAsync(
                                proposal.Downlink.Value,
                                true,
                                taskId,
                                operation.Token,
                                operation.EnsureCurrent
                            );
                        }
                        catch (XmaxException exception)
                        {
                            XmaxRealtimeErrorManager.Report(exception);
                        }
                    }
                });
                if (!accepted && CurrentState.TaskId == taskId)
                    ResetQualitySamples();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (CurrentState.TaskId == taskId)
                    ResetQualitySamples();
                XmaxRealtimeErrorManager.Report(exception);
            }
            finally
            {
                _qualityPending = false;
            }
        }

        /// <summary>
        /// Discards samples from periods when quality adaptation was unavailable.
        /// </summary>
        private void ResetQualitySamples()
        {
            _stream.QualityController.ResetSamples();
        }

        /// <summary>
        /// Rejects calls without a connected or generating session.
        /// </summary>
        private void RequireConnection()
        {
            if (_connection.ActiveSession == null ||
                (CurrentState.ConnectionState != RealtimeConnectionState.Connected &&
                 CurrentState.ConnectionState != RealtimeConnectionState.Generating))
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "Realtime connection is not open."
                );
        }

    }
}
