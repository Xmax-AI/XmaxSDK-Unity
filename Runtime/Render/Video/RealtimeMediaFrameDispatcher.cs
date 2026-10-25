//
//  RealtimeMediaFrameDispatcher.cs
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

namespace Xmax.SDK
{

    /// <summary>Copies remote video into a bounded serial queue independent of Unity rendering callbacks.</summary>
    internal sealed class RealtimeMediaFrameDispatcher
    {

        /// <summary>The lock protecting registration, timeline, pending packets, and byte accounting.</summary>
        private readonly object _gate = new object();

        /// <summary>The packets awaiting background delivery.</summary>
        private readonly Queue<RealtimeMediaFrame> _pending = new Queue<RealtimeMediaFrame>();

        /// <summary>The maximum number of queued and in-flight packets.</summary>
        private const int MaximumPendingFrames = 256;

        /// <summary>The maximum copied pixel bytes retained by queued and in-flight packets.</summary>
        private const long MaximumPendingBytes = 32 * 1024 * 1024;

        /// <summary>The current output handler, replaced or removed on the Unity thread.</summary>
        private Action<RealtimeMediaFrame> _listener;

        /// <summary>The version rejecting packets selected before listener replacement.</summary>
        private long _version;

        /// <summary>The stream identity, or null while no confirmed remote source is bound.</summary>
        private Guid? _streamId;

        /// <summary>The previous receiver timestamp used to detect regressions.</summary>
        private long? _lastTimestamp;

        /// <summary>The previous dimensions, pixel format, and orientation.</summary>
        private string _lastFormat;

        /// <summary>Whether the next accepted packet must begin a new continuous segment.</summary>
        private bool _discontinuous = true;

        /// <summary>The bytes retained by queued and currently executing packets.</summary>
        private long _pendingBytes;

        /// <summary>The number of queued and currently executing packets.</summary>
        private int _pendingFrames;

        /// <summary>Whether a background worker owns queue delivery.</summary>
        private bool _running;

        /// <summary>Replaces the handler and discards queued output; an already running callback may finish.</summary>
        internal void SetListener(
            Action<RealtimeMediaFrame> listener
        )
        {
            lock (_gate)
            {
                _listener = listener;
                ResetTimeline();
            }
        }

        /// <summary>Creates or clears a stream identity and discards output belonging to its predecessor.</summary>
        internal void SetStream(
            bool active
        )
        {
            lock (_gate)
            {
                _streamId = active ? Guid.NewGuid() : (Guid?)null;
                ResetTimeline();
            }
        }

        /// <summary>Resets continuity and queued output while preserving accounting for an executing callback.</summary>
        private void ResetTimeline()
        {
            _version++;
            _lastTimestamp = null;
            _lastFormat = null;
            _discontinuous = true;
            while (_pending.Count > 0)
            {
                _pendingBytes -= ByteCount(_pending.Dequeue().VideoFrame);
                _pendingFrames--;
            }
        }

        /// <summary>Copies a valid frame if capacity permits, marking continuity after any discarded packet.</summary>
        internal void Dispatch(
            XmaxVideoFrame frame
        )
        {
            lock (_gate)
            {
                if (!_streamId.HasValue || _listener == null)
                    return;
                if (frame.TimestampMicroseconds < 0)
                {
                    _discontinuous = true;
                    return;
                }

                var format = frame.Width + ":" + frame.Height + ":" + (int)frame.PixelFormat + ":" + (int)frame.Rotation;
                var discontinuous = _discontinuous || frame.IsDiscontinuous ||
                    (_lastTimestamp.HasValue && frame.TimestampMicroseconds <= _lastTimestamp.Value) ||
                    (_lastFormat != null && format != _lastFormat);
                var duration = _lastTimestamp.HasValue ? frame.TimestampMicroseconds - _lastTimestamp.Value : 0;
                if (duration <= 0 || duration >= 1000000)
                    duration = 41667;
                _lastTimestamp = frame.TimestampMicroseconds;
                _lastFormat = format;
                var bytes = ByteCount(frame);
                if (_pendingFrames >= MaximumPendingFrames || bytes > MaximumPendingBytes - _pendingBytes)
                {
                    _discontinuous = true;
                    return;
                }

                _pending.Enqueue(new RealtimeMediaFrame(
                        _streamId.Value,
                        discontinuous,
                        frame.Clone(),
                        duration
                    ));
                _pendingBytes += bytes;
                _pendingFrames++;
                _discontinuous = false;
                if (!_running)
                {
                    _running = true;
                    ThreadPool.QueueUserWorkItem(_ => Drain());
                }
            }
        }

        /// <summary>Delivers packets in order, isolating callback failures and releasing byte reservations.</summary>
        private void Drain()
        {
            while (true)
            {
                RealtimeMediaFrame frame;
                Action<RealtimeMediaFrame> callback;
                long version;
                long bytes;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        _running = false;
                        return;
                    }
                    frame = _pending.Dequeue();
                    bytes = ByteCount(frame.VideoFrame);
                    callback = _listener;
                    version = _version;
                }

                try
                {
                    lock (_gate)
                        if (version != _version || _streamId != frame.StreamId)
                            callback = null;
                    EventDispatch.Raise(
                        callback,
                        frame
                    );
                }
                finally
                {
                    lock (_gate)
                    {
                        _pendingBytes -= bytes;
                        _pendingFrames--;
                    }
                }
            }
        }

        /// <summary>Returns the retained pixel-plane size without depending on tightly packed row strides.</summary>
        private static long ByteCount(
            XmaxVideoFrame frame
        )
        {
            long bytes = 0;
            foreach (var plane in frame.Planes)
                bytes += plane.LongLength;
            return bytes;
        }
    }
}
