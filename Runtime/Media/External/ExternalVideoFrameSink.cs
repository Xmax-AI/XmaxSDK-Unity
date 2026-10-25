//
//  ExternalVideoFrameSink.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Threading;

namespace Xmax.SDK
{

    /// <summary>Copies and serializes source frames through a bounded Unity-thread mailbox.</summary>
    internal sealed class ExternalVideoFrameSink : IRealtimeExternalFrameSink
    {

        /// <summary>The lock protecting frame submission, timestamps, and binding validity.</summary>
        private readonly object _gate = new object();

        /// <summary>The source format required for submitted frame dimensions.</summary>
        private readonly RealtimeVideoFormat _sourceFormat;

        /// <summary>The Unity context receiving frames and failures.</summary>
        private readonly SynchronizationContext _context;

        /// <summary>The current binding's preview and publication callback.</summary>
        private readonly Action<XmaxVideoFrame> _receive;

        /// <summary>The current binding's fatal media error callback.</summary>
        private readonly Action<XmaxException> _failed;

        /// <summary>Whether this binding accepts frames and its first failure.</summary>
        private bool _active = true;

        /// <summary>The last accepted timestamp, in microseconds.</summary>
        private long _lastTimestamp = -1;

        /// <summary>The revision invalidating already-posted frame and failure callbacks.</summary>
        private long _revision;

        /// <summary>The latest copied frame waiting for the Unity thread.</summary>
        private XmaxVideoFrame _pending;

        /// <summary>Whether a mailbox delivery is already queued.</summary>
        private bool _scheduled;

        /// <summary>The first media failure, retained for startup validation.</summary>
        private XmaxException _failure;

        /// <inheritdoc/>
        public RealtimeVideoFormat VideoFormat { get; }

        /// <inheritdoc/>
        public bool IsRequestingFrames
        {
            get
            {
                lock (_gate)
                    return _active;
            }
        }

        /// <summary>The source failure observed during binding, or null while startup remains valid.</summary>
        internal XmaxException Failure
        {
            get
            {
                lock (_gate)
                    return _failure;
            }
        }

        /// <summary>Creates an external frame binding with a stable media baseline and Unity dispatch context.</summary>
        internal ExternalVideoFrameSink(
            RealtimeVideoFormat sourceFormat,
            RealtimeVideoFormat videoFormat,
            SynchronizationContext context,
            Action<XmaxVideoFrame> receive,
            Action<XmaxException> failed
        )
        {
            _sourceFormat = sourceFormat;
            VideoFormat = videoFormat;
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _receive = receive;
            _failed = failed;
        }

        /// <inheritdoc/>
        public void PushVideo(
            XmaxVideoFrame frame
        )
        {
            if (frame == null)
            {
                ReportError(new ArgumentNullException(nameof(frame)));
                return;
            }

            var schedule = false;
            Exception failure = null;
            lock (_gate)
            {
                if (!_active || frame.TimestampMicroseconds < 0 || frame.TimestampMicroseconds <= _lastTimestamp)
                    return;
                try
                {
                    frame.Validate();
                    if (frame.Width != _sourceFormat.Width || frame.Height != _sourceFormat.Height ||
                        frame.Rotation != XmaxVideoRotation.Rotation0)
                        throw new XmaxException(
                            XmaxErrorCode.MediaError,
                            "External frames must be upright and match source.VideoFormat dimensions."
                        );
                    _pending = frame.Clone();
                    _lastTimestamp = frame.TimestampMicroseconds;
                    if (!_scheduled)
                    {
                        _scheduled = true;
                        schedule = true;
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }
            if (failure != null)
                ReportError(failure);
            else if (schedule)
                _context.Post(
                    _ => Deliver(),
                    null
                );
        }

        /// <summary>Delivers the latest copied frame while its binding remains active.</summary>
        private void Deliver()
        {
            lock (_gate)
            {
                _scheduled = false;
                var frame = _pending;
                _pending = null;
                if (!_active || frame == null)
                    return;

                // Failure or invalidation must wait for an in-flight submission to finish.
                try
                {
                    _receive(frame);
                }
                catch (Exception exception)
                {
                    ReportError(exception);
                }
            }
        }

        /// <inheritdoc/>
        public void ReportError(
            Exception error
        )
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            long revision;
            XmaxException failure;
            lock (_gate)
            {
                if (!_active)
                    return;
                _active = false;
                _pending = null;
                failure = _failure = new XmaxException(
                    XmaxErrorCode.MediaError,
                    "External video source failed: " + error.Message,
                    innerException: error,
                    severity: XmaxErrorSeverity.Fatal
                );
                revision = _revision;
            }
            _context.Post(
                _ =>
                {
                    lock (_gate)
                        if (revision != _revision)
                            return;
                    _failed(failure);
                },
                null
            );
        }

        /// <summary>Waits for in-flight copying and rejects pending frames and failures from this binding.</summary>
        internal void Invalidate()
        {
            lock (_gate)
            {
                _active = false;
                _revision++;
                _pending = null;
            }
        }
    }
}
