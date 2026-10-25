//
//  RealtimeNetworkQuality.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>A directional network quality rating, independent of transport quality levels.</summary>
    public enum RealtimeNetworkQualityLevel
    {

        /// <summary>The rating is unavailable or unrecognized.</summary>
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

    /// <summary>An immutable snapshot of directional ratings and any available transport measurements.</summary>
    ///
    /// <remarks>Unavailable numerical measurements from the Agora adapter are zero. Ratings describe network conditions rather than the configured L1–L5 transport levels.</remarks>
    public sealed class RealtimeNetworkQuality
    {

        /// <summary>The local uplink network rating.</summary>
        public RealtimeNetworkQualityLevel Uplink { get; }

        /// <summary>The local downlink network rating.</summary>
        public RealtimeNetworkQualityLevel Downlink { get; }

        /// <summary>The reported packet-loss ratio, or zero when unavailable from the adapter.</summary>
        public double PacketLossRatio { get; }

        /// <summary>The reported round-trip latency, in milliseconds, or zero when unavailable.</summary>
        public int RoundTripMilliseconds { get; }

        /// <summary>The estimated bandwidth, in bits per second, or zero when unavailable.</summary>
        public int BandwidthBitsPerSecond { get; }

        /// <summary>Creates a snapshot of the supplied directional ratings and transport measurements.</summary>
        ///
        /// <remarks>Unavailable numerical measurements from the Agora adapter are zero. Ratings describe network conditions rather than the configured L1–L5 transport levels.</remarks>
        internal RealtimeNetworkQuality(
            double packetLossRatio,
            int roundTripMilliseconds,
            int bandwidthBitsPerSecond
        )
            : this(
                RealtimeNetworkQualityLevel.Unknown,
                RealtimeNetworkQualityLevel.Unknown,
                packetLossRatio,
                roundTripMilliseconds,
                bandwidthBitsPerSecond
            )
        {
        }

        /// <summary>Creates a snapshot of the supplied directional ratings and transport measurements.</summary>
        ///
        /// <remarks>Unavailable numerical measurements from the Agora adapter are zero. Ratings describe network conditions rather than the configured L1–L5 transport levels.</remarks>
        public RealtimeNetworkQuality(
            RealtimeNetworkQualityLevel uplink,
            RealtimeNetworkQualityLevel downlink,
            double packetLossRatio = 0,
            int roundTripMilliseconds = 0,
            int bandwidthBitsPerSecond = 0
        )
        {
            Uplink = uplink;
            Downlink = downlink;
            PacketLossRatio = packetLossRatio;
            RoundTripMilliseconds = roundTripMilliseconds;
            BandwidthBitsPerSecond = bandwidthBitsPerSecond;
        }
    }
}
