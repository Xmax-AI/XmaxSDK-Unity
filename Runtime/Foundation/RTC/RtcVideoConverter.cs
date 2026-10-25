//
//  RtcVideoConverter.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using Agora.Rtc;

namespace Xmax.SDK
{

    /// <summary>Packs stride-aware raw video into reusable native input buffers.</summary>
    internal sealed class RtcVideoConverter
    {

        /// <summary>The contiguous raw frame bytes reused by the next submission.</summary>
        private byte[] _upload;

        /// <summary>Converts one raw frame, retaining valid packed bytes until the next call.</summary>
        internal ExternalVideoFrame Convert(
            XmaxVideoFrame frame
        )
        {
            frame.Validate();
            var i420 = frame.PixelFormat == XmaxVideoPixelFormat.I420;
            var length = checked(frame.Width * frame.Height * (i420 ? 3 : 8) / 2);
            if (_upload == null || _upload.Length != length)
                _upload = new byte[length];
            var offset = 0;
            for (var plane = 0; plane < (i420 ? 3 : 1); plane++)
            {
                var width = i420 ? frame.Width / (plane == 0 ? 1 : 2) : frame.Width * 4;
                var height = frame.Height / (plane == 0 ? 1 : 2);
                for (var row = 0; row < height; row++)
                {
                    Buffer.BlockCopy(
                        frame.Planes[plane],
                        row * frame.Strides[plane],
                        _upload,
                        offset,
                        width
                    );
                    offset += width;
                }
            }
            return new ExternalVideoFrame
            {
                type = VIDEO_BUFFER_TYPE.VIDEO_BUFFER_RAW_DATA,
                format = i420 ? VIDEO_PIXEL_FORMAT.VIDEO_PIXEL_I420 : frame.PixelFormat == XmaxVideoPixelFormat.Rgba
                    ? VIDEO_PIXEL_FORMAT.VIDEO_PIXEL_RGBA : VIDEO_PIXEL_FORMAT.VIDEO_PIXEL_BGRA,
                buffer = _upload,
                stride = frame.Width,
                height = frame.Height,
                rotation = (int)frame.Rotation,
                timestamp = frame.TimestampMicroseconds / 1000
            };
        }
    }
}
