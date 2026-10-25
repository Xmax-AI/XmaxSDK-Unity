//
//  RealtimeMediaFrame.cs
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

    /// <summary>An owned remote video packet delivered on a serial background queue.</summary>
    ///
    /// <remarks>
    /// The pixel arrays belong to this packet and may be retained after the callback. StreamId changes
    /// when a published remote source is rebound. The first packet, timestamp regressions, format changes,
    /// and delivery drops mark the following packet as discontinuous. Video duration uses consecutive
    /// timestamps, falling back to 1/24 second rounded to microseconds for the first or an invalid interval.
    /// </remarks>
    public sealed class RealtimeMediaFrame
    {

        /// <summary>The identity of the bound remote stream, independent of its generation task identifier.</summary>
        public Guid StreamId { get; }

        /// <summary>Whether the receiver should reset continuity before processing this packet.</summary>
        public bool IsDiscontinuous { get; }

        /// <summary>The copied video pixels and receiver presentation timestamp.</summary>
        public XmaxVideoFrame VideoFrame { get; }

        /// <summary>The receiver presentation timestamp, in microseconds.</summary>
        public long TimestampMicroseconds => VideoFrame.TimestampMicroseconds;

        /// <summary>The resolved frame duration in microseconds.</summary>
        public long? DurationMicroseconds { get; }

        /// <summary>Creates a packet owning its supplied frame.</summary>
        internal RealtimeMediaFrame(
            Guid streamId,
            bool isDiscontinuous,
            XmaxVideoFrame videoFrame,
            long? durationMicroseconds = null
        )
        {
            StreamId = streamId;
            IsDiscontinuous = isDiscontinuous;
            VideoFrame = videoFrame;
            DurationMicroseconds = durationMicroseconds;
        }
    }
}
