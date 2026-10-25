//
//  StreamController.cs
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
    /// Coordinates ordered room signaling, result publication confirmation, and generated frames.
    /// </summary>
    internal sealed class StreamController : IStreamController
    {

        /// <summary>
        /// The neutral RTC transport.
        /// </summary>
        private readonly IRtcManager _rtc;

        /// <summary>
        /// The generation confirmation timeout, in milliseconds.
        /// </summary>
        private readonly int _generationTimeout;

        /// <summary>
        /// The shared startup timer.
        /// </summary>
        private readonly RealtimeTiming _timing;

        /// <summary>
        /// The network rating converter and public event source.
        /// </summary>
        private readonly IQualityController _quality;

        /// <summary>The room connection, heartbeat, and generation wire protocol owner.</summary>
        private readonly IRoomController _room;

        /// <summary>The uplink encoder configuration owner.</summary>
        private readonly IEncodingController _encoding;

        /// <inheritdoc/>
        public IQualityController QualityController => _quality;

        /// <summary>
        /// The published result streams available in this room.
        /// </summary>
        private readonly HashSet<RemoteStream> _published = new HashSet<RemoteStream>();

        /// <summary>
        /// The pending result-stream confirmation.
        /// </summary>
        private TaskCompletionSource<bool> _confirmation;

        /// <summary>
        /// The active generation task identifier.
        /// </summary>
        private string _taskId;

        /// <summary>
        /// Whether the entire start signal has been submitted.
        /// </summary>
        private bool _signalSent;

        /// <summary>
        /// The confirmed remote stream for frame forwarding.
        /// </summary>
        private RemoteStream? _matchedStream;

        /// <inheritdoc/>
        public event Action<XmaxException> FatalError;

        /// <inheritdoc/>
        public event Action<RealtimeNetworkQuality> NetworkQualityChanged;

        /// <inheritdoc/>
        public event Action<RealtimeNetworkQuality, double> NetworkQualitySampled;

        /// <inheritdoc/>
        public event Action<XmaxVideoFrame> FrameReceived;

        /// <inheritdoc/>
        public event Action<RemoteStream?> RemoteStreamChanged;

        /// <summary>
        /// Creates a room protocol controller and binds transport callbacks.
        /// </summary>
        internal StreamController(
            IRtcManager rtc,
            int generationTimeout = 30000,
            RealtimeTiming timing = null,
            IRoomController room = null,
            IQualityController quality = null,
            IEncodingController encoding = null,
            IMediaService mediaService = null
        )
        {
            _rtc = rtc;
            _generationTimeout = generationTimeout;
            _timing = timing;
            _quality = quality ?? new QualityController(
                rtc,
                mediaService
            );
            _room = room ?? new RoomController(rtc);
            _encoding = encoding ?? new EncodingController(rtc);
            _quality.NetworkQualitySampled += (
                sample,
                sampledAt
            ) =>
            {
                if (_room.IsConnected)
                    EventDispatch.Raise(
                        NetworkQualitySampled,
                        sample,
                        sampledAt
                    );
            };
            rtc.FatalError += OnFatalError;
            _quality.NetworkQualityChanged += quality =>
            {
                if (_room.IsConnected)
                    EventDispatch.Raise(
                        NetworkQualityChanged,
                        quality
                    );
            };
            rtc.VideoPublished += OnPublished;
            rtc.VideoUnpublished += OnUnpublished;
            rtc.FrameReceived += OnFrame;
        }

        /// <inheritdoc/>
        public void ValidatePlatform() => _rtc.ValidatePlatform();

        /// <inheritdoc/>
        public Task InitializeAsync(
            CancellationToken cancellationToken
        ) => _rtc.InitializeAsync(cancellationToken);

        /// <inheritdoc/>
        public Task ReleaseAsync() => _rtc.DestroyAsync();

        /// <inheritdoc/>
        public void ConfigureMedia(
            RealtimeVideoFormat baseline,
            RealtimeVideoFormat generation,
            RealtimeVideoFormat target
        )
        {
            _room.ConfigureMedia(
                baseline,
                generation,
                target
            );
        }

        /// <inheritdoc/>
        public void ConfigureVideoEncoding(
            RealtimeVideoFormat format
        ) => _encoding.Configure(format);

        /// <inheritdoc/>
        public async Task ConnectAsync(
            RoomJoinConfiguration info,
            RealtimeVideoFormat format,
            CancellationToken cancellationToken
        )
        {
            try
            {
                _timing?.BeginRoomJoin();
                await _room.ConnectAsync(
                    info,
                    format,
                    cancellationToken
                );
                cancellationToken.ThrowIfCancellationRequested();
                _timing?.FinishRoomJoin();
                _timing?.BeginLocalPublication();
                _rtc.PublishLocalVideo();
                _timing?.FinishLocalPublication();
                _quality.SetUplinkStatisticsEnabled(true);
            }
            catch
            {
                await DisconnectAsync();
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task DisconnectAsync()
        {
            try
            {
                await _room.StopHeartbeatAsync();
                await StopGenerationAsync();
            }
            finally
            {
                _published.Clear();
                _quality.Reset();
                await _room.DisconnectAsync();
            }
        }

        /// <inheritdoc/>
        public async Task StartGenerationAsync(
            string taskId,
            RealtimeContext context,
            RealtimeVideoFormat format,
            CancellationToken cancellationToken,
            bool faceRestoration = false
        )
        {
            if (!_room.IsConnected || _taskId != null)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "RTC connection is unavailable or generation is already active."
                );
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(taskId))
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "A generation task identifier is required."
                );
            _taskId = taskId;
            SetRemoteStream(null);
            _signalSent = false;
            _confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var confirmation = _confirmation;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                deadline.CancelAfter(_generationTimeout);
                try
                {
                    _timing?.BeginSignal(taskId);
                    await AsyncDeadline.WaitAsync(
                        _room.StartGenerationAsync(
                            taskId,
                            context,
                            format,
                            deadline.Token,
                            faceRestoration
                        ),
                        _generationTimeout,
                        cancellationToken
                    );
                    cancellationToken.ThrowIfCancellationRequested();
                    RequireTask(taskId);
                    _signalSent = true;
                    ConfirmPublishedStream();
                    var remaining = _generationTimeout - (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);
                    if (remaining <= 0)
                        throw new XmaxException(
                            XmaxErrorCode.Timeout,
                            "Realtime generation start timed out."
                        );
                    await AsyncDeadline.WaitAsync(
                        confirmation.Task,
                        remaining,
                        cancellationToken
                    );
                    return;
                }
                catch (Exception exception)
                {
                    deadline.Cancel();
                    await StopGenerationAsync();
                    if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested &&
                        watch.ElapsedMilliseconds >= _generationTimeout)
                        throw new XmaxException(
                            XmaxErrorCode.Timeout,
                            "Realtime generation start timed out."
                        );
                    throw;
                }
                finally
                {
                    deadline.Cancel();
                }
            }
        }

        /// <inheritdoc/>
        public async Task ChangeGenerationConditionAsync(
            RealtimeContext context,
            RealtimeVideoFormat format,
            string taskId,
            CancellationToken cancellationToken,
            bool faceRestoration = false
        )
        {
            RequireTask(taskId);
            cancellationToken.ThrowIfCancellationRequested();
            await _room.ChangeConditionAsync(
                taskId,
                context,
                format,
                cancellationToken,
                faceRestoration
            );
            cancellationToken.ThrowIfCancellationRequested();
            RequireTask(taskId);
        }

        /// <inheritdoc/>
        public async Task ChangeTargetSizeAsync(
            string taskId,
            RealtimeVideoFormat format,
            CancellationToken cancellationToken
        )
        {
            RequireTask(taskId);
            await _room.ChangeTargetSizeAsync(
                taskId,
                format,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
            RequireTask(taskId);
            _room.CommitTargetSize(format);
        }

        /// <inheritdoc/>
        public void SendTracks(
            IReadOnlyList<RealtimePoint> tracks,
            string taskId
        )
        {
            RequireTask(taskId);
            _room.SendTracks(
                taskId,
                tracks
            );
        }

        /// <inheritdoc/>
        public void PushFrame(
            XmaxVideoFrame frame
        )
        {
            if (_taskId == null)
                return;

            try
            {
                _rtc.PushFrame(frame);
            }
            catch (Exception exception)
            {
                XmaxLogger.Media.Error(() => "外部视频帧推送失败 (External Video Frame Push Failed)\n└─ "
                    + XmaxLogger.Localized(
                        "原因：",
                        "Reason: "
                    ) + ErrorMessageFormatter.Format(exception));
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task StopGenerationAsync()
        {
            var task = _taskId;
            _taskId = null;
            _signalSent = false;
            SetRemoteStream(null);
            _confirmation?.TrySetCanceled();
            _confirmation = null;
            await _room.StopGenerationAsync(task);
        }

        /// <summary>
        /// Checks the room and exact server-assigned result account.
        /// </summary>
        private bool Accept(
            RemoteStream key
        ) => _room.Accept(key);

        /// <summary>
        /// Subscribes to eligible result streams and rebinds an active generation.
        /// </summary>
        private void OnPublished(
            RemoteStream key
        )
        {
            if (!Accept(key))
                return;
            try
            {
                if (_published.Add(key))
                    _rtc.SubscribeVideo(key);
                ConfirmPublishedStream();
            }
            catch (Exception exception)
            {
                OnFatalError(XmaxErrorConverter.Wrap(exception));
            }
        }

        /// <summary>
        /// Invalidates frame forwarding until the result user publishes again.
        /// </summary>
        private void OnUnpublished(
            RemoteStream key
        )
        {
            _published.Remove(key);
            if (_matchedStream.HasValue && _matchedStream.Value.Equals(key))
            {
                SetRemoteStream(null);
            }
        }

        /// <summary>
        /// Confirms a published bot stream only after start signaling has completed.
        /// </summary>
        private void ConfirmPublishedStream()
        {
            if (_taskId == null || !_signalSent)
                return;
            foreach (var stream in _published)
            {
                if (!Accept(stream))
                    continue;
                SetRemoteStream(stream);
                _confirmation?.TrySetResult(true);
                return;
            }
        }

        /// <summary>Updates result ownership before notifying rendering to retire or replace its binding.</summary>
        private void SetRemoteStream(
            RemoteStream? stream
        )
        {
            if (Nullable.Equals(_matchedStream, stream))
                return;

            _matchedStream = stream;
            _quality.SetDownlinkStatisticsUser(stream?.UserId);
            EventDispatch.Raise(
                RemoteStreamChanged,
                stream,
                () => Nullable.Equals(_matchedStream, stream)
            );
        }

        /// <summary>
        /// Forwards only frames from the current confirmed result stream.
        /// </summary>
        private void OnFrame(
            RemoteStream key,
            XmaxVideoFrame frame
        )
        {
            if (_taskId == null || !_matchedStream.HasValue || !_matchedStream.Value.Equals(key))
                return;
            EventDispatch.Raise(
                FrameReceived,
                frame
            );
        }

        /// <summary>
        /// Fails pending confirmation and reports background connection errors.
        /// </summary>
        private void OnFatalError(
            XmaxException exception
        )
        {
            _confirmation?.TrySetException(exception);
            EventDispatch.Raise(
                FatalError,
                exception
            );
        }

        /// <summary>
        /// Rejects operations belonging to an old or stopped generation.
        /// </summary>
        private void RequireTask(
            string taskId
        )
        {
            if (!_room.IsConnected || _taskId == null || _taskId != taskId)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "Realtime generation is not active."
                );
        }

    }
}
