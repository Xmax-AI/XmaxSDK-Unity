//
//  UplinkVideoStatistics.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>Immutable uplink video metrics copied from a native callback.</summary>
    ///
    /// <remarks>Null metrics are unavailable; zero remains a valid measured value.</remarks>
    public sealed class UplinkVideoStatistics
    {

        /// <summary>The encoded width, in pixels.</summary>
        public int? Width { get; }

        /// <summary>The encoded height, in pixels.</summary>
        public int? Height { get; }

        /// <summary>The capture frame rate, in frames per second.</summary>
        public int? CaptureFrameRate { get; }

        /// <summary>The encoder output frame rate, in frames per second.</summary>
        public int? EncodeFrameRate { get; }

        /// <summary>The sending frame rate, in frames per second.</summary>
        public int? SendFrameRate { get; }

        /// <summary>The actual sending bitrate, in kbps.</summary>
        public int? BitrateKbps { get; }

        /// <summary>The video packet loss before network resilience, from zero to 100 percent.</summary>
        public int? PacketLossPercent { get; }

        /// <summary>Creates a snapshot of video statistics.</summary>
        public UplinkVideoStatistics(
            int? width = null,
            int? height = null,
            int? captureFrameRate = null,
            int? encodeFrameRate = null,
            int? sendFrameRate = null,
            int? bitrateKbps = null,
            int? packetLossPercent = null
        )
        {
            Width = width;
            Height = height;
            CaptureFrameRate = captureFrameRate;
            EncodeFrameRate = encodeFrameRate;
            SendFrameRate = sendFrameRate;
            BitrateKbps = bitrateKbps;
            PacketLossPercent = packetLossPercent;
        }
    }
}
