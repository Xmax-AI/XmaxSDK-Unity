//
//  RtcRemoteVideoSink.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Agora.Rtc;

namespace Xmax.SDK
{

    /// <summary>Copies native I420 frames into a bounded latest-frame mailbox for Unity-thread delivery.</summary>
    internal sealed class RtcRemoteVideoSink
    {

        /// <summary>The room identifier rejecting frames from other channels.</summary>
        private readonly string _roomId;

        /// <summary>The owning Unity synchronization context.</summary>
        private readonly SynchronizationContext _context;

        /// <summary>The main-thread owned-frame receiver.</summary>
        private readonly Action<uint, XmaxVideoFrame> _received;

        /// <summary>The main-thread media failure receiver.</summary>
        private readonly Action<XmaxException> _failed;

        /// <summary>The lock protecting the mailbox and reusable plane buffers.</summary>
        private readonly object _frameLock = new object();

        /// <summary>The bounded pool of decoded-frame buffers.</summary>
        private readonly Stack<XmaxVideoFrame> _framePool = new Stack<XmaxVideoFrame>();

        /// <summary>The latest frame waiting for main-thread delivery.</summary>
        private XmaxVideoFrame _pendingFrame;

        /// <summary>The native user owning the pending frame.</summary>
        private uint _pendingUid;

        /// <summary>Whether the mailbox already has a scheduled main-thread drain.</summary>
        private bool _framePosted;

        /// <summary>Whether native callbacks and queued delivery are invalidated.</summary>
        private volatile bool _closed;

        /// <summary>The native observer registered by the room adapter.</summary>
        internal IVideoFrameObserver Observer { get; }

        /// <summary>Creates a connection-scoped mailbox with owned-frame delivery.</summary>
        internal RtcRemoteVideoSink(
            string roomId,
            SynchronizationContext context,
            Action<uint, XmaxVideoFrame> received,
            Action<XmaxException> failed
        )
        {
            _roomId = roomId;
            _context = context;
            _received = received;
            _failed = failed;
            Observer = new Frames(this);
        }

        /// <summary>Invalidates native frames and releases pooled buffers before observer removal.</summary>
        internal void Close()
        {
            lock (_frameLock)
            {
                _closed = true;
                _pendingFrame = null;
                _framePosted = false;
                _framePool.Clear();
            }
        }

        /// <summary>
        /// Copies native planes before returning from the media callback, keeping only the latest frame.
        /// </summary>
        internal void Receive(
            string channel,
            uint uid,
            VideoFrame frame
        )
        {
            if (_closed || frame.type != VIDEO_PIXEL_FORMAT.VIDEO_PIXEL_I420 || frame.width <= 0 ||
                frame.height <= 0 || frame.width % 2 != 0 || frame.height % 2 != 0 ||
                frame.yStride < frame.width || frame.uStride < frame.width / 2 || frame.vStride < frame.width / 2)
                return;
            try
            {
                lock (_frameLock)
                {
                    if (_closed || channel != _roomId)
                        return;
                    var discarded = _pendingFrame != null;
                    if (discarded)
                    {
                        _framePool.Push(_pendingFrame);
                        _pendingFrame = null;
                    }
                    XmaxVideoFrame buffer = null;
                    while (_framePool.Count > 0)
                    {
                        var candidate = _framePool.Pop();
                        if (candidate.Width == frame.width && candidate.Height == frame.height)
                        {
                            buffer = candidate;
                            break;
                        }
                    }
                    var planes = buffer?.Planes ?? new[] { new byte[checked(frame.width * frame.height)],
                        new byte[checked(frame.width / 2 * (frame.height / 2))], new byte[checked(frame.width / 2 * (frame.height / 2))] };
                    var pointers = new[] { frame.yBufferPtr, frame.uBufferPtr, frame.vBufferPtr };
                    var strides = new[] { frame.yStride, frame.uStride, frame.vStride };
                    for (var plane = 0; plane < 3; plane++)
                    {
                        var width = frame.width / (plane == 0 ? 1 : 2);
                        var rows = frame.height / (plane == 0 ? 1 : 2);
                        if (pointers[plane] == IntPtr.Zero)
                            return;
                        for (var row = 0; row < rows; row++)
                            Marshal.Copy(
                                IntPtr.Add(
                                    pointers[plane],
                                    checked(row * strides[plane])
                                ),
                                planes[plane],
                                row * width,
                                width
                            );
                    }
                    _pendingFrame = XmaxVideoFrame.CreateI420(
                        planes[0],
                        planes[1],
                        planes[2],
                        frame.width,
                        frame.height,
                        timestampMicroseconds: checked(frame.renderTimeMs * 1000),
                        rotation: (XmaxVideoRotation)frame.rotation
                    );
                    _pendingFrame.IsDiscontinuous = discarded;
                    _pendingUid = uid;
                    if (_framePosted)
                        return;
                    _framePosted = true;
                    _context.Post(
                        _ =>
                        {
                            if (!_closed)
                                DrainFrame();
                        },
                        null
                    );
                }
            }
            catch (Exception exception)
            {
                _context.Post(
                    _ =>
                    {
                        if (!_closed)
                            _failed(XmaxErrorConverter.Wrap(exception));
                    },
                    null
                );
            }
        }

        /// <summary>Delivers the latest owned frame and returns its buffers after callbacks complete.</summary>
        private void DrainFrame()
        {
            XmaxVideoFrame frame;
            uint uid;
            lock (_frameLock)
            {
                frame = _pendingFrame;
                uid = _pendingUid;
                _pendingFrame = null;
                _framePosted = false;
            }
            if (frame == null)
                return;
            try
            {
                _received(
                    uid,
                    frame
                );
            }
            finally
            {
                lock (_frameLock)
                    if (!_closed && _framePool.Count < 3)
                        _framePool.Push(frame);
            }
        }

        /// <summary>Forwards native pre-render frames while their pointers remain valid.</summary>
        private sealed class Frames : IVideoFrameObserver
        {

            /// <summary>The connection-scoped owned-frame mailbox.</summary>
            private readonly RtcRemoteVideoSink _sink;

            /// <summary>Creates the native callback bridge.</summary>
            internal Frames(
                RtcRemoteVideoSink sink
            )
            {
                _sink = sink;
            }

            /// <inheritdoc/>
            public override bool OnRenderVideoFrame(
                string channelId,
                uint remoteUid,
                VideoFrame videoFrame
            )
            {
                _sink.Receive(
                    channelId,
                    remoteUid,
                    videoFrame
                );
                return true;
            }
        }
    }
}
