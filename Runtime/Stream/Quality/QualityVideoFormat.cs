//
//  QualityVideoFormat.cs
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
    /// Derives directional formats from an unchanged media baseline.
    /// </summary>
    internal static class QualityVideoFormat
    {

        /// <summary>
        /// The dimension scale at each quality level.
        /// </summary>
        private static readonly double[] Scales = { 1, 0.9, 0.75, 0.6, 0.5 };

        /// <summary>
        /// The uplink frame-rate ceiling at each quality level.
        /// </summary>
        private static readonly int[] FrameRates = { 30, 24, 24, 20, 16 };

        /// <summary>
        /// Resolves a four-aligned uplink or final target format without upscaling.
        /// </summary>
        internal static RealtimeVideoFormat Resolve(
            RealtimeVideoFormat baseline,
            RealtimeQualityLevel level,
            bool downlink,
            int minimumPixels = 0
        )
        {
            ValidateLevel(level);
            baseline.Validate();
            var index = (int)level - 1;
            var shortSide = Math.Min(baseline.Width, baseline.Height);
            var scale = Math.Max(Scales[index], (double)Math.Min(shortSide, 240) / shortSide);
            RealtimeVideoSize size;
            if (downlink)
            {
                size = MediaService.Align(
                    baseline.Width,
                    baseline.Height,
                    scale,
                    2
                );
            }
            else
            {
                var divisor = MediaService.Gcd(
                    baseline.Width,
                    baseline.Height
                );
                var minimumScale = Math.Sqrt(Math.Min(Math.Max(0, minimumPixels),
                        (double)baseline.Width * baseline.Height) / ((double)baseline.Width * baseline.Height));
                var minimumUnits = (int)Math.Ceiling(divisor * minimumScale / 4) * 4;
                var units = (int)Math.Floor(divisor * scale + 0.0000001) / 4 * 4;
                var resolved = Math.Max(units, minimumUnits);
                if (units >= 4 && resolved <= divisor)
                    size = new RealtimeVideoSize(
                        baseline.Width / divisor * resolved,
                        baseline.Height / divisor * resolved
                    );
                else
                    size = new RealtimeVideoSize(
                        Math.Max(4, Math.Max((int)Math.Floor(baseline.Width * scale / 4) * 4,
                                (int)Math.Ceiling(baseline.Width * minimumScale / 4) * 4)),
                        Math.Max(4, Math.Max((int)Math.Floor(baseline.Height * scale / 4) * 4,
                                (int)Math.Ceiling(baseline.Height * minimumScale / 4) * 4))
                    );
            }
            return new RealtimeVideoFormat(
                size.Width,
                size.Height,
                downlink ? 24 : Math.Min(baseline.Fps, FrameRates[index]),
                downlink ? null : baseline.MinimumBitrate,
                downlink ? null : baseline.MaximumBitrate,
                baseline.EncoderPreference
            );
        }

        /// <summary>
        /// Resolves the final four-aligned return format from fractional downlink bounds.
        /// </summary>
        internal static RealtimeVideoFormat ResolveTarget(
            RealtimeVideoFormat baseline,
            RealtimeQualityLevel level,
            IMediaService mediaService
        )
        {
            ValidateLevel(level);
            var shortSide = Math.Min(baseline.Width, baseline.Height);
            var scale = Math.Max(Scales[(int)level - 1], (double)Math.Min(shortSide, 240) / shortSide);
            var size = mediaService.ResolveTargetSize(
                baseline.Width,
                baseline.Height,
                baseline.Width * scale,
                baseline.Height * scale
            );
            return new RealtimeVideoFormat(
                size.Width,
                size.Height,
                24,
                encoderPreference: baseline.EncoderPreference
            );
        }

        /// <summary>
        /// Rejects values outside L1 through L5.
        /// </summary>
        internal static void ValidateLevel(
            RealtimeQualityLevel level
        )
        {
            if (level < RealtimeQualityLevel.L1 || level > RealtimeQualityLevel.L5)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Quality level must be L1 through L5."
                );
        }
    }
}
