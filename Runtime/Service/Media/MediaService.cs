//
//  MediaService.cs
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
    /// The kind of media whose dimensions are being resolved.
    /// </summary>
    public enum RealtimeMediaSource
    {

        /// <summary>
        /// Camera dimensions that must match the model's camera buckets.
        /// </summary>
        Camera,

        /// <summary>
        /// Video frames supplied by your app.
        /// </summary>
        External,

        /// <summary>
        /// Decoded video file dimensions.
        /// </summary>
        Video,

        /// <summary>
        /// Still image dimensions.
        /// </summary>
        Image
    }

    /// <summary>
    /// Resolves local media, generation canvases, and returned dimensions without network access.
    /// </summary>
    public sealed class MediaService : IMediaService
    {

        /// <summary>
        /// Returns the built-in model's sizing capabilities.
        /// </summary>
        public RealtimeModelCapabilities GetCapabilities(
            RealtimeModel model
        )
        {
            return RealtimeModelRegistry.Get(model).Capabilities;
        }

        /// <summary>
        /// Resolves a four-aligned media baseline within the model's pixel area limits.
        /// </summary>
        ///
        /// <remarks>
        /// Camera sizes must match a bucket. Other sources retain their aspect ratio where alignment permits.
        /// </remarks>
        public RealtimeVideoSize ResolveMediaSize(
            RealtimeModel model,
            int width,
            int height,
            RealtimeMediaSource source = RealtimeMediaSource.External
        )
        {
            RequireDimensions(
                width,
                height
            );
            if (!Enum.IsDefined(
                typeof(RealtimeMediaSource),
                source
            ))
                throw Invalid("Unknown media source.");
            var capability = GetCapabilities(model);
            if (source == RealtimeMediaSource.Camera)
                capability.ValidateResolution(
                    width,
                    height
                );
            var pixels = (double)width * height;
            var target = Math.Max(capability.MinimumPixels, Math.Min(capability.MaximumPixels, pixels));
            return Align(
                width,
                height,
                Math.Sqrt(target / pixels),
                4,
                capability.MinimumPixels,
                capability.MaximumPixels
            );
        }

        /// <summary>
        /// Selects the generation canvas closest to the media aspect ratio.
        /// </summary>
        public RealtimeVideoSize ResolveGenerationSize(
            RealtimeModel model,
            int width,
            int height
        )
        {
            RequireDimensions(
                width,
                height
            );
            var capability = GetCapabilities(model);
            if (capability.ResolutionBuckets.Count == 0)
                return Align(
                    width,
                    height,
                    Math.Sqrt(Math.Max(capability.MinimumPixels,
                            Math.Min(capability.MaximumPixels, (double)width * height)) / ((double)width * height)),
                    capability.DimensionAlignment,
                    capability.MinimumPixels,
                    capability.MaximumPixels
                );
            var best = capability.ResolutionBuckets[0];
            var distance = double.PositiveInfinity;
            foreach (var size in capability.ResolutionBuckets)
            {
                var next = Math.Abs(Math.Log(((double)width / height) / ((double)size.Width / size.Height)));
                if (next < distance)
                {
                    distance = next;
                    best = size;
                }
            }
            return best;
        }

        /// <summary>
        /// Resolves four-aligned return dimensions within bounds without upscaling.
        /// </summary>
        public RealtimeVideoSize ResolveTargetSize(
            int width,
            int height,
            double maximumWidth,
            double maximumHeight
        )
        {
            RequireDimensions(
                width,
                height
            );
            if (double.IsNaN(maximumWidth) || double.IsInfinity(maximumWidth) || maximumWidth <= 0 ||
                double.IsNaN(maximumHeight) || double.IsInfinity(maximumHeight) || maximumHeight <= 0)
                throw Invalid("Target bounds must be positive and finite.");
            return Align(
                width,
                height,
                Math.Min(1, Math.Min(maximumWidth / width, maximumHeight / height)),
                4
            );
        }

        /// <summary>
        /// Resolves an external media baseline, preserving explicit encoding options.
        /// </summary>
        public RealtimeVideoFormat RecommendVideoFormat(
            RealtimeModel model,
            int sourceWidth,
            int sourceHeight,
            int fps = 0
        )
        {
            if (fps < 0)
                throw Invalid("Frame rate cannot be negative.");
            var size = ResolveMediaSize(
                model,
                sourceWidth,
                sourceHeight
            );
            return new RealtimeVideoFormat(
                size.Width,
                size.Height,
                fps == 0 ? GetCapabilities(model).DefaultFps : fps
            );
        }

        /// <summary>
        /// Resolves aligned dimensions, preferring exact aspect ratio before bounded rounding.
        /// </summary>
        internal static RealtimeVideoSize Align(
            int width,
            int height,
            double scale,
            int alignment,
            int minimumPixels = 0,
            int maximumPixels = int.MaxValue
        )
        {
            var divisor = Gcd(
                width,
                height
            );
            var ratioWidth = width / divisor;
            var ratioHeight = height / divisor;
            var units = Math.Floor(divisor * scale / alignment) * alignment;
            if (minimumPixels > 0)
                units = Math.Max(units, Math.Ceiling(Math.Sqrt(minimumPixels / ((double)ratioWidth * ratioHeight)) / alignment) * alignment);
            var exactWidth = ratioWidth * units;
            var exactHeight = ratioHeight * units;
            if (units >= alignment && exactWidth * exactHeight <= maximumPixels &&
                exactWidth <= int.MaxValue && exactHeight <= int.MaxValue)
                return new RealtimeVideoSize(
                    (int)exactWidth,
                    (int)exactHeight
                );
            var w = Math.Floor(width * scale / alignment) * alignment;
            var h = Math.Floor(height * scale / alignment) * alignment;
            if (minimumPixels > 0 && (w * h < minimumPixels || w * h > maximumPixels))
            {
                var minUnits = (minimumPixels + alignment * alignment - 1) / (alignment * alignment);
                var maxUnits = maximumPixels / (alignment * alignment);
                var best = double.PositiveInfinity;
                var result = new RealtimeVideoSize();
                for (var x = 1; x <= maxUnits; x++)
                {
                    var minY = (minUnits + x - 1) / x;
                    var maxY = maxUnits / x;
                    if (minY > maxY)
                        continue;
                    var y = Math.Max(minY, Math.Min(maxY, Math.Round(height * scale / alignment, MidpointRounding.AwayFromZero)));
                    var dx = (x * alignment - width * scale) / (width * scale);
                    var dy = (y * alignment - height * scale) / (height * scale);
                    if (dx * dx + dy * dy >= best)
                        continue;
                    best = dx * dx + dy * dy;
                    result = new RealtimeVideoSize(
                        x * alignment,
                        (int)y * alignment
                    );
                }
                if (result.Width == 0)
                    throw Invalid("No aligned size satisfies the pixel limits.");
                return result;
            }
            if (w < alignment || h < alignment || w > int.MaxValue || h > int.MaxValue)
                throw Invalid("No aligned size fits the bounds.");
            return new RealtimeVideoSize(
                (int)w,
                (int)h
            );
        }

        /// <summary>
        /// Returns the greatest common divisor of positive dimensions.
        /// </summary>
        internal static int Gcd(
            int a,
            int b
        )
        {
            while (b != 0)
            {
                var remainder = a % b;
                a = b;
                b = remainder;
            }
            return a;
        }

        /// <summary>
        /// Rejects nonpositive source dimensions.
        /// </summary>
        private static void RequireDimensions(
            int width,
            int height
        )
        {
            if (width <= 0 || height <= 0)
                throw Invalid("Media dimensions must be positive integers.");
        }

        /// <summary>
        /// Creates a sizing configuration error.
        /// </summary>
        private static XmaxException Invalid(
            string message
        ) => new XmaxException(
            XmaxErrorCode.InvalidConfiguration,
            message
        );
    }
}
