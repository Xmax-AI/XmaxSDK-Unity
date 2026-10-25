//
//  RtcNetworkQuality.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>A vendor-neutral RTC network quality rating.</summary>
    internal enum RtcQualityLevel
    {

        /// <summary>No recognized network quality rating is available.</summary>
        Unknown,

        /// <summary>The network quality is excellent.</summary>
        Excellent,

        /// <summary>The network quality is good.</summary>
        Good,

        /// <summary>The network quality is fair.</summary>
        Poor,

        /// <summary>The network quality is bad.</summary>
        Bad,

        /// <summary>The network quality is very bad.</summary>
        VeryBad,

        /// <summary>The network connection is unavailable.</summary>
        Down
    }

    /// <summary>A vendor-neutral snapshot of local directional ratings and available transport measurements.</summary>
    internal readonly struct RtcNetworkQuality
    {

        /// <summary>The local sending direction network rating.</summary>
        internal readonly RtcQualityLevel Uplink;

        /// <summary>The local receiving direction network rating.</summary>
        internal readonly RtcQualityLevel Downlink;

        /// <summary>The reported packet-loss ratio, or zero when unavailable.</summary>
        internal readonly double PacketLossRatio;

        /// <summary>The reported round-trip latency, in milliseconds, or zero when unavailable.</summary>
        internal readonly int RoundTripMilliseconds;

        /// <summary>The estimated available bandwidth, in bits per second, or zero when unavailable.</summary>
        internal readonly int BandwidthBitsPerSecond;

        /// <summary>Creates a snapshot of the supplied network ratings and transport measurements.</summary>
        internal RtcNetworkQuality(
            RtcQualityLevel uplink,
            RtcQualityLevel downlink,
            double packetLossRatio,
            int rtt,
            int bandwidth
        )
        {
            Uplink = uplink;
            Downlink = downlink;
            PacketLossRatio = packetLossRatio;
            RoundTripMilliseconds = rtt;
            BandwidthBitsPerSecond = bandwidth;
        }
    }
}
