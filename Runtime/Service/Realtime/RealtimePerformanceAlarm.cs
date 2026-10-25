//
//  RealtimePerformanceAlarm.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>The current device encoding performance state.</summary>
    public enum RealtimePerformanceStatus
    {

        /// <summary>Device encoding performance is limited.</summary>
        Limited,

        /// <summary>Device encoding performance has recovered.</summary>
        Recovered
    }

    /// <summary>A device performance alarm with an optional suggested RTC fallback format.</summary>
    public sealed class RealtimePerformanceAlarm
    {

        /// <summary>The current limitation or recovery state.</summary>
        public RealtimePerformanceStatus Status { get; }

        /// <summary>The suggested dimensions and frame rate, or null if the native values are invalid.</summary>
        public RealtimeVideoFormat? SuggestedVideoFormat { get; }

        /// <summary>Creates a device performance alarm.</summary>
        public RealtimePerformanceAlarm(
            RealtimePerformanceStatus status,
            RealtimeVideoFormat? suggestedVideoFormat
        )
        {
            Status = status;
            SuggestedVideoFormat = suggestedVideoFormat;
        }
    }
}
