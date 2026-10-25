//
//  RealtimeVideoFormat.cs
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
    /// The preferred response to limited uplink bandwidth.
    /// </summary>
    public enum RealtimeVideoEncoderPreference
    {

        /// <summary>
        /// Lets the RTC encoder select a degradation preference.
        /// </summary>
        Auto,

        /// <summary>
        /// Prioritizes frame rate when uplink bandwidth is limited.
        /// </summary>
        MaintainFramerate,

        /// <summary>
        /// Prioritizes image quality when uplink bandwidth is limited.
        /// </summary>
        MaintainQuality
    }

    /// <summary>
    /// Video dimensions, frame rate, and optional uplink encoding settings.
    /// </summary>
    public readonly struct RealtimeVideoFormat
    {

        /// <summary>
        /// The positive video width, in pixels.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// The positive video height, in pixels.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// The positive target frame rate, in frames per second.
        /// </summary>
        public int Fps { get; }

        /// <summary>
        /// The minimum uplink bitrate, in kbps. Null uses the SDK default; zero disables the minimum.
        /// </summary>
        ///
        /// <remarks>
        /// The encoding budget is validated after combining explicit and default values. Uplink overrides are
        /// not copied into the downlink budget.
        /// </remarks>
        public int? MinimumBitrate { get; }

        /// <summary>
        /// The maximum uplink bitrate, in kbps. Null uses the SDK budget; explicit values must be positive.
        /// </summary>
        public int? MaximumBitrate { get; }

        /// <summary>
        /// The uplink encoder preference, defaulting to frame rate preservation.
        /// </summary>
        public RealtimeVideoEncoderPreference EncoderPreference { get; }

        /// <summary>
        /// Creates a video format.
        /// </summary>
        public RealtimeVideoFormat(
            int width,
            int height,
            int fps
        )
            : this(
                width,
                height,
                fps,
                null,
                null,
                RealtimeVideoEncoderPreference.MaintainFramerate
            )
        {
        }

        /// <summary>
        /// Creates a video format.
        /// </summary>
        public RealtimeVideoFormat(
            int width,
            int height,
            int fps,
            int? minimumBitrate = null,
            int? maximumBitrate = null,
            RealtimeVideoEncoderPreference encoderPreference = RealtimeVideoEncoderPreference.MaintainFramerate
        )
        {
            Width = width;
            Height = height;
            Fps = fps;

            MinimumBitrate = minimumBitrate;
            MaximumBitrate = maximumBitrate;
            EncoderPreference = encoderPreference;
        }

        /// <summary>
        /// Returns a format with replacement dimensions and optional frame rate.
        /// </summary>
        internal RealtimeVideoFormat Resized(
            int width,
            int height,
            int? fps = null
        )
        {
            return new RealtimeVideoFormat(
                width,
                height,
                fps ?? Fps,
                MinimumBitrate,
                MaximumBitrate,
                EncoderPreference
            );
        }

        /// <summary>
        /// Rejects invalid dimensions, frame rate, bitrate bounds, or encoding preferences.
        /// </summary>
        public void Validate()
        {
            if (Width <= 0 || Height <= 0 || Fps <= 0 || Width % 2 != 0 || Height % 2 != 0)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Realtime video width and height must be positive even numbers, and fps must be greater than zero."
                );
            }

            if (MinimumBitrate < 0)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Minimum bitrate must not be negative."
                );
            }

            if (MaximumBitrate <= 0)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Maximum bitrate must be greater than zero."
                );
            }

            if (MinimumBitrate > MaximumBitrate)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Minimum bitrate must not exceed maximum bitrate."
                );
            }

            if (EncoderPreference != RealtimeVideoEncoderPreference.Auto &&
                EncoderPreference != RealtimeVideoEncoderPreference.MaintainFramerate &&
                EncoderPreference != RealtimeVideoEncoderPreference.MaintainQuality)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Unknown video encoder preference."
                );
            }
        }
    }
}
