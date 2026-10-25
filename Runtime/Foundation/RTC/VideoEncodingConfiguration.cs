//
//  VideoEncodingConfiguration.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>Validated main-stream encoder settings with explicit bitrate bounds and no vendor types.</summary>
    internal readonly struct VideoEncodingConfiguration
    {

        /// <summary>The encoded width, in pixels.</summary>
        public int Width { get; }

        /// <summary>The encoded height, in pixels.</summary>
        public int Height { get; }

        /// <summary>The encoded frame rate, in frames per second.</summary>
        public int FrameRate { get; }

        /// <summary>The minimum sending bitrate, in kbps; zero disables the lower bound.</summary>
        public int MinimumBitrate { get; }

        /// <summary>The maximum sending bitrate, in kbps.</summary>
        public int MaximumBitrate { get; }

        /// <summary>The uplink encoder degradation preference.</summary>
        public RealtimeVideoEncoderPreference EncoderPreference { get; }

        /// <summary>Creates resolved encoder settings from a validated format and bitrate budget.</summary>
        internal VideoEncodingConfiguration(
            RealtimeVideoFormat format,
            int minimumBitrate,
            int maximumBitrate
        )
        {
            Width = format.Width;
            Height = format.Height;
            FrameRate = format.Fps;

            MinimumBitrate = minimumBitrate;
            MaximumBitrate = maximumBitrate;
            EncoderPreference = format.EncoderPreference;
        }
    }
}
