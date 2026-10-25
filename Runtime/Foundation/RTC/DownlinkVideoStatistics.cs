//
//  DownlinkVideoStatistics.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>Immutable downlink video metrics copied from a native callback.</summary>
    ///
    /// <remarks>Null metrics are unavailable; zero remains a valid measured value.</remarks>
    public sealed class DownlinkVideoStatistics
    {

        /// <summary>The application account providing the measured remote stream.</summary>
        public string UserId { get; }

        /// <summary>The received width, in pixels.</summary>
        public int? Width { get; }

        /// <summary>The received height, in pixels.</summary>
        public int? Height { get; }

        /// <summary>The decoder output frame rate, in frames per second.</summary>
        public int? DecodeFrameRate { get; }

        /// <summary>The native RTC rendering frame rate, in frames per second.</summary>
        public int? RenderFrameRate { get; }

        /// <summary>The actual receiving bitrate, in kbps.</summary>
        public int? BitrateKbps { get; }

        /// <summary>The video packet loss after network resilience, from zero to 100 percent.</summary>
        public int? PacketLossPercent { get; }

        /// <summary>The RTC media delay in milliseconds, excluding complete AI generation time.</summary>
        public int? EndToEndDelayMs { get; }

        /// <summary>Creates a snapshot of video statistics.</summary>
        public DownlinkVideoStatistics(
            string userId,
            int? width = null,
            int? height = null,
            int? decodeFrameRate = null,
            int? renderFrameRate = null,
            int? bitrateKbps = null,
            int? packetLossPercent = null,
            int? endToEndDelayMs = null
        )
        {
            UserId = userId;
            Width = width;
            Height = height;
            DecodeFrameRate = decodeFrameRate;
            RenderFrameRate = renderFrameRate;
            BitrateKbps = bitrateKbps;
            PacketLossPercent = packetLossPercent;
            EndToEndDelayMs = endToEndDelayMs;
        }

        /// <summary>Copies a callback snapshot using its resolved application account.</summary>
        internal DownlinkVideoStatistics WithUserId(
            string userId
        ) => new DownlinkVideoStatistics(
            userId,
            Width,
            Height,
            DecodeFrameRate,
            RenderFrameRate,
            BitrateKbps,
            PacketLossPercent,
            EndToEndDelayMs
        );
    }
}
