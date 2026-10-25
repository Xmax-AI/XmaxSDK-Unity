//
//  MediaController.cs
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

    /// <summary>Owns local input bindings, source lifecycle, and stable preview and transmission dimensions.</summary>
    internal sealed class MediaController : IMediaController
    {

        /// <summary>The callback enforcing the creating Unity thread.</summary>
        private readonly Action _requireThread;

        /// <summary>The callback publishing normalized frames to an active connection.</summary>
        private readonly Action<XmaxVideoFrame> _publish;

        /// <summary>The engine preparation performed before starting an asynchronous external source.</summary>
        private readonly Func<CancellationToken, Task> _initialize;

        /// <summary>The engine release performed after local source shutdown.</summary>
        private readonly Func<Task> _release;

        /// <summary>The Unity context used by externally captured frames and failures.</summary>
        private readonly SynchronizationContext _context;

        /// <summary>The revision invalidating replaced or closed streams and callbacks.</summary>
        private long _revision;

        /// <summary>The resizer producing stable preview and RTC input dimensions.</summary>
        private VideoFrameScaler _scaler = new VideoFrameScaler();

        /// <summary>The host-owned source bound to the current local stream.</summary>
        private IRealtimeExternalVideoSource _source;

        /// <summary>The frame sink belonging to the bound source.</summary>
        private ExternalVideoFrameSink _sink;

        /// <summary>The current source stop task, shared by repeated cleanup requests.</summary>
        private Task _stopping = Task.CompletedTask;

        /// <inheritdoc/>
        public RealtimeMediaStream LocalStream { get; private set; }

        /// <inheritdoc/>
        public event Action<XmaxException> FatalError;

        /// <summary>Creates a media owner with Unity-thread admission and frame publication callbacks.</summary>
        internal MediaController(
            Action requireThread,
            Action<XmaxVideoFrame> publish,
            Func<CancellationToken, Task> initialize = null,
            Func<Task> release = null
        )
        {
            _requireThread = requireThread;
            _publish = publish;
            _initialize = initialize ?? (_ => Task.CompletedTask);
            _release = release ?? (() => Task.CompletedTask);
            _context = SynchronizationContext.Current;
        }

        /// <inheritdoc/>
        public RealtimeMediaStream CreateExternalStream(
            RealtimeVideoFormat sourceFormat,
            RealtimeVideoFormat format
        )
        {
            _requireThread();
            if (_source != null || !_stopping.IsCompleted)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Stop the bound external source before creating another stream."
                );
            return CreateStream(
                sourceFormat,
                format,
                true
            );
        }

        /// <inheritdoc/>
        public async Task<RealtimeMediaStream> CreateExternalStreamAsync(
            IRealtimeExternalVideoSource source,
            RealtimeVideoFormat format,
            CancellationToken token
        )
        {
            _requireThread();
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (_context == null)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Bind external sources on the Unity main thread with its synchronization context."
                );
            if (LocalStream != null || _source != null || !_stopping.IsCompleted)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Stop the current local stream before binding an external source."
                );
            token.ThrowIfCancellationRequested();
            var sourceFormat = source.VideoFormat;
            sourceFormat.Validate();
            var stream = CreateStream(
                sourceFormat,
                format,
                false
            );
            var revision = _revision;
            _source = source;
            var sink = _sink = new ExternalVideoFrameSink(
                sourceFormat,
                format,
                _context,
                frame =>
                {
                    if (revision == _revision)
                        SubmitFrame(
                            frame,
                            revision,
                            stream.VideoTrack,
                            format
                        );
                },
                error =>
                {
                    if (revision == _revision)
                        EventDispatch.Raise(
                            FatalError,
                            error
                        );
                }
            );
            try
            {
                await _initialize(token);
                token.ThrowIfCancellationRequested();
                await source.StartAsync(
                    sink,
                    token
                );
                token.ThrowIfCancellationRequested();
                if (!sink.IsRequestingFrames)
                    throw sink.Failure ?? new XmaxException(
                        XmaxErrorCode.MediaError,
                        "External video source failed during startup."
                    );
                return stream;
            }
            catch
            {
                await CloseAsync();
                throw;
            }
        }

        /// <summary>Creates a local track whose input accepts either direct frames or a source binding.</summary>
        private RealtimeMediaStream CreateStream(
            RealtimeVideoFormat sourceFormat,
            RealtimeVideoFormat format,
            bool acceptsDirectFrames
        )
        {
            format.Validate();
            var revision = ++_revision;
            var track = new RealtimeVideoTrack("video-local");
            track.UpdateVideoFormat(format);
            _scaler = new VideoFrameScaler();
            Action<XmaxVideoFrame> push = acceptsDirectFrames
                ? frame => SubmitFrame(
                    frame,
                    revision,
                    track,
                    format
                )
                : (Action<XmaxVideoFrame>)null;
            LocalStream = new RealtimeMediaStream(
                RealtimeStreamId.Local,
                track,
                format,
                push
            );
            LocalStream.SourceVideoFormat = sourceFormat;
            return LocalStream;
        }

        /// <summary>Normalizes and delivers a current frame without allowing stale preview callbacks to publish it.</summary>
        private void SubmitFrame(
            XmaxVideoFrame frame,
            long revision,
            RealtimeVideoTrack track,
            RealtimeVideoFormat format
        )
        {
            _requireThread();
            if (revision != _revision)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "This local stream has been closed or replaced."
                );
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            frame.Validate();
            frame = _scaler.Resize(
                frame,
                format.Width,
                format.Height
            );
            track.RaiseFrame(
                frame,
                () => revision == _revision && (_sink == null || _sink.IsRequestingFrames)
            );
            if (revision == _revision && (_sink == null || _sink.IsRequestingFrames))
                _publish(frame);
        }

        /// <inheritdoc/>
        public XmaxVideoFrame NormalizeFrame(
            XmaxVideoFrame frame
        )
        {
            if (_source != null)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Submit source-bound frames through the sink supplied to the external video source."
                );
            if (LocalStream == null)
                return frame;
            var format = LocalStream.VideoFormat.Value;
            return _scaler.Resize(
                frame,
                format.Width,
                format.Height
            );
        }

        /// <inheritdoc/>
        public RealtimeVideoFormat RequireOwned(
            RealtimeMediaStream stream
        )
        {
            if (stream == null || !ReferenceEquals(
                stream,
                LocalStream
            ))
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Use a local stream created by this realtime manager."
                );
            return stream.VideoFormat.Value;
        }

        /// <inheritdoc/>
        public Task CloseAsync()
        {
            _requireThread();
            _sink?.Invalidate();
            _sink = null;
            var source = _source;
            _source = null;
            _revision++;
            LocalStream = null;
            _scaler = new VideoFrameScaler();
            if (_stopping.IsCompleted)
                _stopping = StopSourceAsync(source);
            return _stopping;
        }

        /// <summary>Finishes host source shutdown after local binding invalidation, preserving best-effort cleanup.</summary>
        private async Task StopSourceAsync(
            IRealtimeExternalVideoSource source
        )
        {
            try
            {
                if (source != null)
                    await source.StopAsync();
            }
            catch (Exception exception)
            {
                XmaxLogger.Media.Warn(() => "External video source stop failed: " + ErrorMessageFormatter.Format(exception));
            }
            finally
            {
                await _release();
            }
        }
    }
}
