//
//  RealtimeVideoTrack.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;

namespace Xmax.SDK
{

    /// <summary>
    /// A video track delivering frames through synchronous callbacks.
    /// </summary>
    public sealed class RealtimeVideoTrack
    {

        /// <summary>
        /// The track identifier or stream direction.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The media baseline with requested return dimensions, distinct from measured frame dimensions or rate.
        /// </summary>
        public RealtimeVideoFormat? VideoFormat { get; private set; }

        /// <summary>
        /// Updates expected dimensions after a successful configuration submission.
        /// </summary>
        internal void UpdateVideoFormat(
            RealtimeVideoFormat format
        ) => VideoFormat = format;

        /// <summary>
        /// Occurs for an eligible video frame. Clone frames retained beyond the callback.
        /// </summary>
        public event Action<XmaxVideoFrame> FrameReceived;

        /// <summary>
        /// Creates a video track.
        /// </summary>
        internal RealtimeVideoTrack(
            string id
        )
        {
            Id = string.IsNullOrWhiteSpace(id) ? "video-remote" : id;
        }

        /// <summary>
        /// Delivers frames while the current binding remains valid across subscriber reentrancy.
        /// </summary>
        internal void RaiseFrame(
            XmaxVideoFrame frame,
            Func<bool> isCurrent = null
        )
        {
            EventDispatch.Raise(
                FrameReceived,
                frame,
                isCurrent
            );
        }
    }
}
