//
//  XmaxVideoFrame.cs
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

    /// <summary>The memory layout of raw video pixels.</summary>
    public enum XmaxVideoPixelFormat
    {

        /// <summary>Planar 4:2:0 pixels stored as separate Y, U, and V planes.</summary>
        I420,

        /// <summary>Packed four-byte pixels in red, green, blue, alpha order.</summary>
        Rgba,

        /// <summary>Packed four-byte pixels in blue, green, red, alpha order.</summary>
        Bgra
    }

    /// <summary>Clockwise rotation metadata carried with a frame; pixel arrays are not rotated.</summary>
    public enum XmaxVideoRotation
    {

        /// <summary>No rotation.</summary>
        Rotation0 = 0,

        /// <summary>A clockwise 90-degree rotation.</summary>
        Rotation90 = 90,

        /// <summary>A clockwise 180-degree rotation.</summary>
        Rotation180 = 180,

        /// <summary>A clockwise 270-degree rotation.</summary>
        Rotation270 = 270
    }

    /// <summary>A video frame referencing pixel planes and row strides without copying input arrays.</summary>
    ///
    /// <remarks>Do not modify pixel arrays during submission. SDK-owned callback buffers may be reused; call Clone before retaining a frame after its callback.</remarks>
    public sealed class XmaxVideoFrame
    {

        /// <summary>The memory layout of the pixel planes.</summary>
        public XmaxVideoPixelFormat PixelFormat { get; }

        /// <summary>The positive even frame width, in pixels.</summary>
        public int Width { get; }

        /// <summary>The positive even frame height, in pixels.</summary>
        public int Height { get; }

        /// <summary>The referenced pixel arrays: Y, U, V for I420, or one packed array for RGBA and BGRA.</summary>
        public byte[][] Planes { get; }

        /// <summary>The byte distance between adjacent row starts in each pixel plane.</summary>
        public int[] Strides { get; }

        /// <summary>The timestamp forwarded to RTC, in microseconds; the default is zero.</summary>
        public long TimestampMicroseconds { get; }

        /// <summary>The clockwise rotation metadata accompanying the frame.</summary>
        public XmaxVideoRotation Rotation { get; }

        /// <summary>Whether the native mailbox discarded a frame before this one.</summary>
        internal bool IsDiscontinuous { get; set; }

        /// <summary>Creates a frame from validated pixel planes and metadata without copying arrays.</summary>
        ///
        /// <remarks>Do not modify pixel arrays during submission. SDK-owned callback buffers may be reused; call Clone before retaining a frame after its callback.</remarks>
        private XmaxVideoFrame(
            XmaxVideoPixelFormat pixelFormat,
            int width,
            int height,
            byte[][] planes,
            int[] strides,
            long timestampMicroseconds,
            XmaxVideoRotation rotation
        )
        {
            PixelFormat = pixelFormat;
            Width = width;
            Height = height;
            Planes = planes;
            Strides = strides;
            TimestampMicroseconds = timestampMicroseconds;
            Rotation = rotation;
        }

        /// <summary>Validates and wraps RGBA pixels, retaining the input array.</summary>
        ///
        /// <remarks>Stride is in bytes; a nonpositive stride uses width times four. Timestamps are in microseconds and zero is forwarded unchanged. Rotation is metadata and does not alter pixels.</remarks>
        public static XmaxVideoFrame CreateRgba(
            byte[] data,
            int width,
            int height,
            int stride = 0,
            long timestampMicroseconds = 0,
            XmaxVideoRotation rotation = XmaxVideoRotation.Rotation0
        )
        {
            return CreatePacked(
                XmaxVideoPixelFormat.Rgba,
                data,
                width,
                height,
                stride,
                timestampMicroseconds,
                rotation
            );
        }

        /// <summary>Validates and wraps BGRA pixels, retaining the input array.</summary>
        ///
        /// <remarks>Stride is in bytes; a nonpositive stride uses width times four. Timestamps are in microseconds and zero is forwarded unchanged. Rotation is metadata and does not alter pixels.</remarks>
        public static XmaxVideoFrame CreateBgra(
            byte[] data,
            int width,
            int height,
            int stride = 0,
            long timestampMicroseconds = 0,
            XmaxVideoRotation rotation = XmaxVideoRotation.Rotation0
        )
        {
            return CreatePacked(
                XmaxVideoPixelFormat.Bgra,
                data,
                width,
                height,
                stride,
                timestampMicroseconds,
                rotation
            );
        }

        /// <summary>Validates and wraps Y, U, and V pixels, retaining the input arrays.</summary>
        ///
        /// <remarks>Strides are in bytes; nonpositive strides use tightly packed plane rows. Timestamps are in microseconds and zero is forwarded unchanged. Rotation does not alter pixels.</remarks>
        public static XmaxVideoFrame CreateI420(
            byte[] y,
            byte[] u,
            byte[] v,
            int width,
            int height,
            int strideY = 0,
            int strideU = 0,
            int strideV = 0,
            long timestampMicroseconds = 0,
            XmaxVideoRotation rotation = XmaxVideoRotation.Rotation0
        )
        {
            ValidateDimensions(
                width,
                height
            );
            strideY = strideY <= 0 ? width : strideY;
            strideU = strideU <= 0 ? width / 2 : strideU;
            strideV = strideV <= 0 ? width / 2 : strideV;
            ValidatePlane(
                y,
                strideY,
                width,
                height,
                nameof(y)
            );
            ValidatePlane(
                u,
                strideU,
                width / 2,
                height / 2,
                nameof(u)
            );
            ValidatePlane(
                v,
                strideV,
                width / 2,
                height / 2,
                nameof(v)
            );
            ValidateRotation(rotation);

            return new XmaxVideoFrame(
                XmaxVideoPixelFormat.I420,
                width,
                height,
                new[] { y, u, v },
                new[] { strideY, strideU, strideV },
                timestampMicroseconds,
                rotation
            );
        }

        /// <summary>Validates dimensions, rotation, plane counts, row strides, and buffer lengths.</summary>
        internal void Validate()
        {
            ValidateDimensions(
                Width,
                Height
            );
            ValidateRotation(Rotation);
            if (PixelFormat == XmaxVideoPixelFormat.I420)
            {
                if (Planes == null || Strides == null || Planes.Length < 3 || Strides.Length < 3)
                {
                    throw new XmaxException(
                        XmaxErrorCode.InvalidConfiguration,
                        "An I420 frame requires Y, U and V planes."
                    );
                }

                ValidatePlane(
                    Planes[0],
                    Strides[0],
                    Width,
                    Height,
                    "Y"
                );
                ValidatePlane(
                    Planes[1],
                    Strides[1],
                    Width / 2,
                    Height / 2,
                    "U"
                );
                ValidatePlane(
                    Planes[2],
                    Strides[2],
                    Width / 2,
                    Height / 2,
                    "V"
                );
            }
            else
            {
                if (Planes == null || Strides == null || Planes.Length < 1 || Strides.Length < 1)
                {
                    throw new XmaxException(
                        XmaxErrorCode.InvalidConfiguration,
                        "A packed frame requires one plane."
                    );
                }

                ValidatePlane(
                    Planes[0],
                    Strides[0],
                    (long)Width * 4,
                    Height,
                    "RGBA/BGRA"
                );
            }
        }

        /// <summary>Validates and wraps four-byte packed pixels without copying the input array.</summary>
        private static XmaxVideoFrame CreatePacked(
            XmaxVideoPixelFormat format,
            byte[] data,
            int width,
            int height,
            int stride,
            long timestampMicroseconds,
            XmaxVideoRotation rotation
        )
        {
            ValidateDimensions(
                width,
                height
            );
            if ((long)width * 4 > int.MaxValue)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Video row is too large."
                );

            stride = stride <= 0 ? width * 4 : stride;
            ValidatePlane(
                data,
                stride,
                (long)width * 4,
                height,
                nameof(data)
            );
            ValidateRotation(rotation);

            return new XmaxVideoFrame(
                format,
                width,
                height,
                new[] { data },
                new[] { stride },
                timestampMicroseconds,
                rotation
            );
        }

        /// <summary>Requires positive even video dimensions.</summary>
        private static void ValidateDimensions(
            int width,
            int height
        )
        {
            if (width <= 0 || height <= 0 || width % 2 != 0 || height % 2 != 0)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Video frame width and height must be positive even numbers."
                );
            }
        }

        /// <summary>Creates a deep copy of the frame planes and strides for retention beyond a callback.</summary>
        public XmaxVideoFrame Clone()
        {
            Validate();
            var planes = new byte[Planes.Length][];

            for (var i = 0; i < planes.Length; i++)
                planes[i] = (byte[])Planes[i].Clone();

            return new XmaxVideoFrame(
                PixelFormat,
                Width,
                Height,
                planes,
                (int[])Strides.Clone(),
                TimestampMicroseconds,
                Rotation
            )
            {
                IsDiscontinuous = IsDiscontinuous
            };
        }

        /// <summary>Requires a row stride and byte length sufficient for the complete plane layout.</summary>
        private static void ValidatePlane(
            byte[] data,
            int stride,
            long rowBytes,
            int height,
            string name
        )
        {
            var minimumLength = (long)stride * height;
            if (stride < rowBytes || minimumLength > int.MaxValue || data == null || data.Length < minimumLength)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    $"Video plane {name} is missing or smaller than {minimumLength} bytes."
                );
            }
        }

        /// <summary>Requires one of the supported clockwise rotation values.</summary>
        private static void ValidateRotation(
            XmaxVideoRotation rotation
        )
        {
            if (rotation != XmaxVideoRotation.Rotation0 &&
                rotation != XmaxVideoRotation.Rotation90 &&
                rotation != XmaxVideoRotation.Rotation180 &&
                rotation != XmaxVideoRotation.Rotation270)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    $"Unsupported video rotation: {rotation}."
                );
            }
        }
    }
}
