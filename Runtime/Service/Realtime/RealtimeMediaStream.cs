//
//  RealtimeMediaStream.cs
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
    /// A local external media input or remote generated video stream.
    /// </summary>
    public sealed class RealtimeMediaStream
    {

        /// <summary>
        /// The track identifier or stream direction.
        /// </summary>
        public RealtimeStreamId Id { get; }

        /// <summary>
        /// The stream's video track.
        /// </summary>
        public RealtimeVideoTrack VideoTrack { get; }

        /// <summary>
        /// The stable media baseline for local streams or requested return format for remote streams.
        /// </summary>
        public RealtimeVideoFormat? VideoFormat => VideoTrack.VideoFormat;

        /// <summary>
        /// The source format supplied by your app before media sizing.
        /// </summary>
        public RealtimeVideoFormat? SourceVideoFormat { get; internal set; }

        /// <summary>
        /// The direct local input callback, or null for remote tracks and source-bound streams.
        /// </summary>
        private readonly Action<XmaxVideoFrame> _push;

        /// <summary>
        /// Submits a direct-input frame for local preview and RTC transmission during generation startup or execution.
        /// </summary>
        ///
        /// <remarks>
        /// Source-bound streams receive frames through the sink passed to IRealtimeExternalVideoSource.StartAsync.
        /// Call on the creating Unity main thread while this stream is valid. Frames may be resized into
        /// reusable SDK buffers. Clone a frame retained beyond a callback. Local preview continues while
        /// disconnected.
        /// </remarks>
        public void PushVideoFrame(
            XmaxVideoFrame frame
        )
        {
            if (_push == null)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Push through a direct-input local stream or the sink supplied to your external video source."
                );

            _push(frame);
        }

        /// <summary>
        /// Creates a media stream with an optional local input callback.
        /// </summary>
        internal RealtimeMediaStream(
            RealtimeStreamId id,
            RealtimeVideoTrack videoTrack,
            RealtimeVideoFormat? videoFormat = null,
            Action<XmaxVideoFrame> push = null
        )
        {
            Id = id;
            VideoTrack = videoTrack;
            if (videoFormat.HasValue)
                videoTrack.UpdateVideoFormat(videoFormat.Value);
            _push = push;
        }
    }
}
