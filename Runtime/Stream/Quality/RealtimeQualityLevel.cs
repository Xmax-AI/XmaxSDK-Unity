//
//  RealtimeQualityLevel.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>
    /// A transport quality level, independent of network quality ratings.
    /// </summary>
    public enum RealtimeQualityLevel
    {

        /// <summary>
        /// Retains the media baseline dimensions.
        /// </summary>
        L1 = 1,

        /// <summary>
        /// Targets 90 percent of baseline dimensions.
        /// </summary>
        L2 = 2,

        /// <summary>
        /// Targets 75 percent of baseline dimensions.
        /// </summary>
        L3 = 3,

        /// <summary>
        /// Targets 60 percent of baseline dimensions.
        /// </summary>
        L4 = 4,

        /// <summary>
        /// Targets 50 percent of baseline dimensions.
        /// </summary>
        L5 = 5
    }

    /// <summary>
    /// The successfully submitted transport configuration for the current connection.
    /// </summary>
    public sealed class RealtimeQualityConfiguration
    {

        /// <summary>
        /// The media baseline, unchanged by adaptation.
        /// </summary>
        public RealtimeVideoFormat Baseline { get; internal set; }

        /// <summary>
        /// The successfully applied uplink level.
        /// </summary>
        public RealtimeQualityLevel UplinkLevel { get; internal set; }

        /// <summary>
        /// The successfully submitted downlink level.
        /// </summary>
        public RealtimeQualityLevel DownlinkLevel { get; internal set; }

        /// <summary>
        /// The configured uplink format, independent of actual encoder statistics.
        /// </summary>
        public RealtimeVideoFormat Uplink { get; internal set; }

        /// <summary>
        /// The requested return format, independent of received frame dimensions.
        /// </summary>
        public RealtimeVideoFormat Downlink { get; internal set; }
    }
}
