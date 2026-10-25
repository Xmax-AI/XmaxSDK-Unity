//
//  RtcPerformanceAlarm.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>The native encoder limitation state and suggested fallback measurements.</summary>
    internal readonly struct RtcPerformanceAlarm
    {

        /// <summary>Whether encoding is limited rather than recovered.</summary>
        internal bool Limited { get; }

        /// <summary>The suggested encoded width, in pixels.</summary>
        internal int Width { get; }

        /// <summary>The suggested encoded height, in pixels.</summary>
        internal int Height { get; }

        /// <summary>The suggested encoding frame rate, in frames per second.</summary>
        internal int FrameRate { get; }

        /// <summary>Copies a native encoder performance indication.</summary>
        internal RtcPerformanceAlarm(
            bool limited,
            int width,
            int height,
            int frameRate
        )
        {
            Limited = limited;
            Width = width;
            Height = height;
            FrameRate = frameRate;
        }
    }
}
