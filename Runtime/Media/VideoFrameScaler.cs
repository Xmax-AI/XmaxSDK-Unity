//
//  VideoFrameScaler.cs
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
    /// Normalizes raw planes to the stable media dimensions using reusable bilinear buffers.
    /// </summary>
    ///
    /// <remarks>
    /// Returned pixels remain valid until the next resize. Retained callback frames must be cloned.
    /// </remarks>
    internal sealed class VideoFrameScaler
    {

        /// <summary>
        /// The reusable output planes.
        /// </summary>
        private byte[][] _planes;

        /// <summary>
        /// The dimensions and layout of the cached output planes.
        /// </summary>
        private (int Width, int Height, XmaxVideoPixelFormat Format) _layout;

        /// <summary>
        /// Returns the input unchanged at matching dimensions, otherwise scales each pixel plane.
        /// </summary>
        internal XmaxVideoFrame Resize(
            XmaxVideoFrame frame,
            int width,
            int height
        )
        {
            frame.Validate();
            if (frame.Width == width && frame.Height == height)
                return frame;
            var i420 = frame.PixelFormat == XmaxVideoPixelFormat.I420;
            var channels = i420 ? 1 : 4;
            if (_planes == null || _layout != (width, height, frame.PixelFormat))
            {
                _layout = (width, height, frame.PixelFormat);
                _planes = i420 ? new[] { new byte[checked(width * height)], new byte[checked(width * height / 4)],
                    new byte[checked(width * height / 4)] } : new[] { new byte[checked(width * height * 4)] };
            }
            for (var plane = 0; plane < _planes.Length; plane++)
            {
                var divisor = plane == 0 ? 1 : 2;
                Scale(
                    frame.Planes[plane],
                    frame.Strides[plane],
                    frame.Width / divisor,
                    frame.Height / divisor,
                    _planes[plane],
                    width / divisor,
                    height / divisor,
                    channels
                );
            }
            if (i420)
                return XmaxVideoFrame.CreateI420(
                    _planes[0],
                    _planes[1],
                    _planes[2],
                    width,
                    height,
                    timestampMicroseconds: frame.TimestampMicroseconds,
                    rotation: frame.Rotation
                );
            return frame.PixelFormat == XmaxVideoPixelFormat.Rgba
                ? XmaxVideoFrame.CreateRgba(
                    _planes[0],
                    width,
                    height,
                    timestampMicroseconds: frame.TimestampMicroseconds,
                    rotation: frame.Rotation
                )
                : XmaxVideoFrame.CreateBgra(
                    _planes[0],
                    width,
                    height,
                    timestampMicroseconds: frame.TimestampMicroseconds,
                    rotation: frame.Rotation
                );
        }

        /// <summary>
        /// Bilinearly scales one plane while respecting its source stride.
        /// </summary>
        private static void Scale(
            byte[] source,
            int stride,
            int sourceWidth,
            int sourceHeight,
            byte[] target,
            int width,
            int height,
            int channels
        )
        {
            for (var y = 0; y < height; y++)
            {
                var sy = Math.Max(0, (y + 0.5) * sourceHeight / height - 0.5);
                var y0 = Math.Min(sourceHeight - 1, (int)sy);
                var y1 = Math.Min(sourceHeight - 1, y0 + 1);
                var fy = sy - y0;
                for (var x = 0; x < width; x++)
                {
                    var sx = Math.Max(0, (x + 0.5) * sourceWidth / width - 0.5);
                    var x0 = Math.Min(sourceWidth - 1, (int)sx);
                    var x1 = Math.Min(sourceWidth - 1, x0 + 1);
                    var fx = sx - x0;
                    for (var c = 0; c < channels; c++)
                    {
                        var top = source[y0 * stride + x0 * channels + c] * (1 - fx) + source[y0 * stride + x1 * channels + c] * fx;
                        var bottom = source[y1 * stride + x0 * channels + c] * (1 - fx) + source[y1 * stride + x1 * channels + c] * fx;
                        target[(y * width + x) * channels + c] = (byte)Math.Round(top * (1 - fy) + bottom * fy);
                    }
                }
            }
        }
    }
}
