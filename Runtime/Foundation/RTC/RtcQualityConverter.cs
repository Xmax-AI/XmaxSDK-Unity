//
//  RtcQualityConverter.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using Agora.Rtc;

namespace Xmax.SDK
{

    /// <summary>
    /// Converts Agora network quality ratings and actual video measurements into SDK values.
    /// </summary>
    internal static class RtcQualityConverter
    {

        /// <summary>
        /// Maps documented quality values, including a disconnected link.
        /// </summary>
        internal static RtcQualityLevel Level(
            int quality
        ) => quality >= 1 && quality <= 6
            ? (RtcQualityLevel)quality : RtcQualityLevel.Unknown;

        /// <summary>
        /// Creates a directional quality update; unavailable measurements remain zero.
        /// </summary>
        internal static RtcNetworkQuality Convert(
            int uplink,
            int downlink
        ) =>
            new RtcNetworkQuality(
                Level(uplink),
                Level(downlink),
                0,
                0,
                0
            );

        /// <summary>Copies actual uplink measurements, preserving unavailable values as null.</summary>
        internal static UplinkVideoStatistics UplinkVideoStatistics(
            LocalVideoStats stats
        ) => new UplinkVideoStatistics(
            Positive(stats.encodedFrameWidth),
            Positive(stats.encodedFrameHeight),
            Nonnegative(stats.captureFrameRate),
            Nonnegative(stats.encoderOutputFrameRate),
            Nonnegative(stats.sentFrameRate),
            Nonnegative(stats.sentBitrate),
            Percent(stats.txPacketLossRate)
        );

        /// <summary>Copies actual downlink measurements without substituting a configured target.</summary>
        internal static DownlinkVideoStatistics DownlinkVideoStatistics(
            RemoteVideoStats stats
        ) => new DownlinkVideoStatistics(
            null,
            Positive(stats.width),
            Positive(stats.height),
            Nonnegative(stats.decoderOutputFrameRate),
            Nonnegative(stats.rendererOutputFrameRate),
            Nonnegative(stats.receivedBitrate),
            Percent(stats.packetLossRate),
            Nonnegative(stats.e2eDelay)
        );

        /// <summary>Returns a positive dimension, or null if unavailable.</summary>
        private static int? Positive(
            int value
        ) => value > 0 ? value : (int?)null;

        /// <summary>Returns a nonnegative measurement, or null if unavailable.</summary>
        private static int? Nonnegative(
            int value
        ) => value >= 0 ? value : (int?)null;

        /// <summary>Returns a valid packet-loss percentage.</summary>
        private static int? Percent(
            int value
        ) => value >= 0 && value <= 100 ? value : (int?)null;
    }
}
