//
//  InteractionCoordinateMapper.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using UnityEngine;

namespace Xmax.SDK
{

    /// <summary>
    /// The aspect-preserving layout of media within an interaction viewport.
    /// </summary>
    public enum RealtimeContentMode
    {

        /// <summary>
        /// Displays the complete media, allowing margins around it.
        /// </summary>
        Fit,

        /// <summary>
        /// Covers the viewport, allowing centered media cropping.
        /// </summary>
        Fill
    }

    /// <summary>
    /// Maps top-left-origin viewport positions into normalized full-media coordinates.
    /// </summary>
    public static class InteractionCoordinateMapper
    {

        /// <summary>
        /// Resolves zero-to-one media coordinates, rejecting invalid geometry and aspect-fit margins.
        /// </summary>
        ///
        /// <remarks>Fill clamps finite positions to the full media. Fit excludes its right and bottom edges.</remarks>
        ///
        /// <param name="x">The viewport coordinate measured rightward from the left edge.</param>
        /// <param name="y">The viewport coordinate measured downward from the top edge.</param>
        /// <param name="viewportWidth">The positive viewport width, using the same units as the position.</param>
        /// <param name="viewportHeight">The positive viewport height, using the same units as the position.</param>
        /// <param name="video">The stable media baseline defining the displayed aspect ratio.</param>
        /// <param name="mode">The aspect-preserving Fit or Fill layout.</param>
        /// <param name="point">The normalized result, or the default value when mapping fails.</param>
        ///
        /// <returns>Whether geometry and the position allow a normalized media point.</returns>
        public static bool TryMap(
            double x,
            double y,
            double viewportWidth,
            double viewportHeight,
            RealtimeVideoFormat video,
            RealtimeContentMode mode,
            out Vector2 point
        )
        {
            point = default;
            if (!Finite(x) || !Finite(y) || !Finite(viewportWidth) || !Finite(viewportHeight) ||
                viewportWidth <= 0 || viewportHeight <= 0 || video.Width <= 0 || video.Height <= 0)
                return false;

            if (mode != RealtimeContentMode.Fit && mode != RealtimeContentMode.Fill)
                return false;

            var scale = mode == RealtimeContentMode.Fill
                ? Math.Max(viewportWidth / video.Width, viewportHeight / video.Height)
                : Math.Min(viewportWidth / video.Width, viewportHeight / video.Height);
            var width = video.Width * scale;
            var height = video.Height * scale;
            if (!Finite(scale) || scale <= 0 || !Finite(width) || !Finite(height))
                return false;
            var left = (viewportWidth - width) / 2;
            var top = (viewportHeight - height) / 2;
            if (mode == RealtimeContentMode.Fit && (x < left || y < top || x >= left + width || y >= top + height))
                return false;

            var normalized = new RealtimePoint(
                Math.Min(1, Math.Max(0, (x - left) / width)),
                Math.Min(1, Math.Max(0, (y - top) / height))
            );
            point = new Vector2(
                (float)normalized.X,
                (float)normalized.Y
            );

            return true;
        }

        /// <summary>
        /// Returns whether a value is neither NaN nor infinity.
        /// </summary>
        private static bool Finite(
            double value
        ) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
