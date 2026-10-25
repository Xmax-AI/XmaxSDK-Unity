//
//  RealtimeModelCapabilities.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;

namespace Xmax.SDK
{

    /// <summary>The dimensions of a video frame, in pixels.</summary>
    public readonly struct RealtimeVideoSize
    {

        /// <summary>The video width, in pixels.</summary>
        public int Width { get; }

        /// <summary>The video height, in pixels.</summary>
        public int Height { get; }

        /// <summary>Creates a video size.</summary>
        public RealtimeVideoSize(
            int width,
            int height
        )
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>The immutable media limits, generation buckets, and default camera format for a model.</summary>
    public sealed class RealtimeModelCapabilities
    {

        /// <summary>The fixed camera and generation canvas dimensions. External media need not match a bucket.</summary>
        public IReadOnlyList<RealtimeVideoSize> ResolutionBuckets { get; }

        /// <summary>The minimum local media and uplink area, in pixels.</summary>
        public int MinimumPixels { get; }

        /// <summary>The maximum local media area, in pixels.</summary>
        public int MaximumPixels { get; }

        /// <summary>The dimension alignment used for variable generation canvases, in pixels.</summary>
        public int DimensionAlignment { get; }

        /// <summary>The default frame rate, in frames per second.</summary>
        public int DefaultFps => DefaultCameraVideoFormat.Fps;

        /// <summary>The default external-stream format when no source dimensions are supplied.</summary>
        public RealtimeVideoFormat DefaultCameraVideoFormat { get; }

        /// <summary>Creates model capabilities and copies the supported size list.</summary>
        internal RealtimeModelCapabilities(
            int minimumPixels,
            int maximumPixels,
            int dimensionAlignment,
            RealtimeVideoFormat defaultCameraVideoFormat,
            params RealtimeVideoSize[] resolutionBuckets
        )
        {
            MinimumPixels = minimumPixels;
            MaximumPixels = maximumPixels;
            DimensionAlignment = dimensionAlignment;
            DefaultCameraVideoFormat = defaultCameraVideoFormat;
            ResolutionBuckets = Array.AsReadOnly((RealtimeVideoSize[])resolutionBuckets.Clone());
        }

        /// <summary>Requires an exact camera bucket match when the model declares fixed sizes.</summary>
        internal void ValidateResolution(
            int width,
            int height
        )
        {
            if (ResolutionBuckets.Count == 0)
            {
                return;
            }

            foreach (var size in ResolutionBuckets)
            {
                if (size.Width == width && size.Height == height)
                {
                    return;
                }
            }

            var supported = new List<string>();
            foreach (var size in ResolutionBuckets)
            {
                supported.Add($"{size.Width}x{size.Height}");
            }

            throw new XmaxException(
                XmaxErrorCode.InvalidConfiguration,
                $"Unsupported model input resolution {width}x{height}. Supported resolutions: " +
                string.Join(
                    ", ",
                    supported
                )
            );
        }
    }
}
