//
//  IQualityController.cs
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
    /// Owns directional transport policies and model-specific quality history for one connection.
    /// </summary>
    internal interface IQualityController
    {

        /// <summary>The applied transport configuration, or null before preparation and after reset.</summary>
        RealtimeQualityConfiguration Configuration { get; }

        /// <summary>Occurs for available uplink metrics, or null when availability is reset.</summary>
        event System.Action<UplinkVideoStatistics> UplinkVideoStatisticsChanged;

        /// <summary>Occurs for the active result user's metrics, or null when its binding is cleared.</summary>
        event System.Action<DownlinkVideoStatistics> DownlinkVideoStatisticsChanged;

        /// <summary>Occurs for a device encoding limitation or recovery alarm.</summary>
        event System.Action<RealtimePerformanceAlarm> PerformanceAlarm;

        /// <summary>Changes local publication availability and clears the previous snapshot.</summary>
        void SetUplinkStatisticsEnabled(
            bool enabled
        );

        /// <summary>Changes the result account observed by downlink statistics and clears its snapshot.</summary>
        void SetDownlinkStatisticsUser(
            string userId
        );

        /// <summary>Occurs when the transport reports a public network quality update.</summary>
        event Action<RealtimeNetworkQuality> NetworkQualityChanged;

        /// <summary>Occurs before public quality delivery with the sample's monotonic time in seconds.</summary>
        event Action<RealtimeNetworkQuality, double> NetworkQualitySampled;

        /// <summary>Prepares initial levels from the unchanged media baseline and optional model history.</summary>
        RealtimeQualityConfiguration Prepare(
            RealtimeVideoFormat baseline,
            RealtimeConfiguration options
        );

        /// <summary>Resolves one candidate's transport dimensions without committing it.</summary>
        RealtimeVideoFormat ResolveFormat(
            RealtimeQualityLevel level,
            bool downlink
        );

        /// <summary>Proposes independent levels from a monotonic sample without applying them.</summary>
        QualityChange Propose(
            RealtimeNetworkQuality quality,
            double sampledAt
        );

        /// <summary>Records an adjustment and persists only successfully applied levels.</summary>
        void Complete(
            RealtimeQualityLevel level,
            bool downlink,
            bool success
        );

        /// <summary>Discards accumulated samples while retaining levels and cooldowns.</summary>
        void ResetSamples();

        /// <summary>Releases all connection-specific policies and history.</summary>
        void Reset();
    }

    /// <summary>The independently proposed directional adjustments for one network sample.</summary>
    internal readonly struct QualityChange
    {

        /// <summary>The proposed uplink level, or null when no adjustment is eligible.</summary>
        internal RealtimeQualityLevel? Uplink { get; }

        /// <summary>The proposed downlink level, or null when no adjustment is eligible.</summary>
        internal RealtimeQualityLevel? Downlink { get; }

        /// <summary>Creates a pair of optional directional proposals.</summary>
        internal QualityChange(
            RealtimeQualityLevel? uplink,
            RealtimeQualityLevel? downlink
        )
        {
            Uplink = uplink;
            Downlink = downlink;
        }
    }
}
