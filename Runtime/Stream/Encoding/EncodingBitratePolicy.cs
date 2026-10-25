//
//  EncodingBitratePolicy.cs
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
    /// Resolves the uplink bitrate budget and validates four-aligned encoder dimensions.
    /// </summary>
    internal static class EncodingBitratePolicy
    {

        /// <summary>
        /// The 15 fps quality references, ordered by pixel area, in kbps.
        /// </summary>
        private static readonly (double Value, double Bitrate)[] ReferenceBitratesAt15Fps =
        {
            (120 * 120, 50),
            (160 * 120, 65),
            (180 * 180, 100),
            (240 * 180, 120),
            (320 * 180, 140),
            (320 * 240, 200),
            (424 * 240, 220),
            (360 * 360, 260),
            (480 * 360, 320),
            (640 * 360, 400),
            (640 * 480, 500),
            (848 * 480, 610),
            (960 * 720, 910),
            (1280 * 720, 1130),
            (1920 * 1080, 2080)
        };

        /// <summary>
        /// The 30 fps quality references, ordered by pixel area, in kbps.
        /// </summary>
        private static readonly (double Value, double Bitrate)[] ReferenceBitratesAt30Fps =
        {
            (360 * 360, 400),
            (480 * 360, 490),
            (640 * 360, 600),
            (640 * 480, 750),
            (848 * 480, 930),
            (960 * 720, 1380),
            (1280 * 720, 1710),
            (1920 * 1080, 3150)
        };

        /// <summary>
        /// Resolves validated uplink settings. The default minimum is 500 kbps, capped by the maximum.
        /// </summary>
        internal static VideoEncodingConfiguration Resolve(
            RealtimeVideoFormat format
        )
        {
            format.Validate();
            if (format.Width % 4 != 0 || format.Height % 4 != 0)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Uplink dimensions must be multiples of four."
                );

            int minimum;
            int maximum;
            if (format.MinimumBitrate.HasValue && format.MaximumBitrate.HasValue)
            {
                minimum = format.MinimumBitrate.Value;
                maximum = format.MaximumBitrate.Value;
            }
            else
            {
                var bitrates = ResolveBitrates(
                    (double)format.Width * format.Height,
                    format.Fps
                );
                var roundedMaximum = Math.Round(bitrates.Maximum * 0.6, MidpointRounding.ToEven);
                if (double.IsNaN(roundedMaximum) || double.IsInfinity(roundedMaximum) || roundedMaximum > int.MaxValue)
                {
                    throw new XmaxException(
                        XmaxErrorCode.InvalidConfiguration,
                        "Realtime video format exceeds the supported bitrate range."
                    );
                }

                var defaultMaximum = Math.Max(100, (int)roundedMaximum);
                minimum = format.MinimumBitrate ?? 100;
                maximum = format.MaximumBitrate ?? defaultMaximum;
            }

            if (minimum > maximum)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Minimum bitrate must not exceed maximum bitrate after applying SDK defaults."
                );
            }

            return new VideoEncodingConfiguration(
                format,
                format.MinimumBitrate ?? Math.Min(500, maximum),
                maximum
            );
        }

        /// <summary>
        /// Interpolates quality references by pixel area and frame rate before the budget multiplier.
        /// </summary>
        private static (double Minimum, double Maximum) ResolveBitrates(
            double pixels,
            double fps
        )
        {
            var bitrate15 = Interpolate(
                pixels,
                ReferenceBitratesAt15Fps
            );
            var first30 = ReferenceBitratesAt30Fps[0];
            // Extend the 15 fps curve proportionally below the first 30 fps reference point.
            var bitrate30 = pixels < first30.Value
                ? bitrate15 * (first30.Bitrate / Interpolate(
                    first30.Value,
                    ReferenceBitratesAt15Fps
                ))
                : Interpolate(
                    pixels,
                    ReferenceBitratesAt30Fps
                );

            // Anchor 10 fps at 400 kbps for 640 by 480; derive the 60 fps quality ceiling from 1080p.
            var bitrate10 = bitrate15 * (400.0 / 500);
            var minimum60 = bitrate30 * (4780.0 / 3150);
            var maximum60 = bitrate30 * (6500.0 / 3150);

            return (
                Interpolate(
                    fps,
                    new[]
                    {
                        (10.0, bitrate10),
                        (15.0, bitrate15),
                        (30.0, bitrate30),
                        (60.0, minimum60)
                    }
                ),
                Interpolate(
                    fps,
                    new[]
                    {
                        (10.0, bitrate10 * 2),
                        (15.0, bitrate15 * 2),
                        (30.0, bitrate30 * 2),
                        (60.0, maximum60)
                    }
                ));
        }

        /// <summary>
        /// Interpolates adjacent references and extrapolates proportionally beyond their endpoints.
        /// </summary>
        private static double Interpolate(
            double value,
            (double Value, double Bitrate)[] points
        )
        {
            var first = points[0];
            if (value <= first.Value)
            {
                return first.Bitrate * (value / first.Value);
            }

            for (var index = 1; index < points.Length; index++)
            {
                var upper = points[index];
                if (value <= upper.Value)
                {
                    var lower = points[index - 1];
                    var ratio = (value - lower.Value) / (upper.Value - lower.Value);

                    return lower.Bitrate + (upper.Bitrate - lower.Bitrate) * ratio;
                }
            }

            var last = points[points.Length - 1];

            return last.Bitrate * (value / last.Value);
        }
    }
}
