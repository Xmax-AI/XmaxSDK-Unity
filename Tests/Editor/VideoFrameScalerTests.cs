//
//  VideoFrameScalerTests.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using NUnit.Framework;

namespace Xmax.SDK.Tests
{
    /// <summary>
    /// Covers pixel accuracy, row padding, and reusable scaling buffer ownership.
    /// </summary>
    public sealed class VideoFrameScalerTests
    {

        /// <summary>
        /// Preserves packed channel order and excludes row padding from bilinear sampling.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PackedFramesRespectStridesAndCloneBeforeBufferReuse(
            bool bgra
        )
        {
            var pixels = new byte[24];
            var values = new byte[] { 0, 100, 200, 240 };
            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 2; x++)
                    for (var channel = 0; channel < 4; channel++)
                        pixels[y * 12 + x * 4 + channel] = (byte)(values[y * 2 + x] + channel);

            var input = bgra
                ? XmaxVideoFrame.CreateBgra(
                    pixels,
                    2,
                    2,
                    stride: 12,
                    timestampMicroseconds: 123
                )
                : XmaxVideoFrame.CreateRgba(
                    pixels,
                    2,
                    2,
                    stride: 12,
                    timestampMicroseconds: 123
                );
            var scaler = new VideoFrameScaler();
            Assert.AreSame(
                input,
                scaler.Resize(
                    input,
                    2,
                    2
                )
            );
            var output = scaler.Resize(
                input,
                4,
                4
            );
            Assert.AreEqual(
                input.PixelFormat,
                output.PixelFormat
            );
            Assert.AreEqual(
                123,
                output.TimestampMicroseconds
            );
            Assert.AreEqual(
                71,
                output.Planes[0][(1 * 4 + 1) * 4]
            );
            Assert.AreEqual(
                74,
                output.Planes[0][(1 * 4 + 1) * 4 + 3]
            );
            Assert.AreEqual(
                240,
                output.Planes[0][(3 * 4 + 3) * 4]
            );

            var retained = output.Clone();
            for (var index = 0; index < pixels.Length; index++)
                pixels[index] = 42;
            var next = scaler.Resize(
                input,
                4,
                4
            );
            Assert.AreSame(
                output.Planes[0],
                next.Planes[0]
            );
            Assert.AreEqual(
                42,
                next.Planes[0][0]
            );
            Assert.AreEqual(
                0,
                retained.Planes[0][0]
            );
        }

        /// <summary>
        /// Scales chroma at half resolution while respecting independently padded I420 planes.
        /// </summary>
        [Test]
        public void I420ScalesLumaAndChromaWithoutReadingRowPadding()
        {
            var yPlane = new byte[24];
            var uPlane = new byte[] { 90, 90, 255, 90, 90, 255 };
            var vPlane = new byte[] { 140, 140, 255, 140, 140, 255 };
            for (var row = 0; row < 4; row++)
                for (var column = 0; column < 4; column++)
                    yPlane[row * 6 + column] = (byte)((row + 1) * 16);
            var input = XmaxVideoFrame.CreateI420(
                yPlane,
                uPlane,
                vPlane,
                4,
                4,
                strideY: 6,
                strideU: 3,
                strideV: 3
            );
            var output = new VideoFrameScaler().Resize(
                input,
                2,
                2
            );
            CollectionAssert.AreEqual(
                new byte[] { 24, 24, 56, 56 },
                output.Planes[0]
            );
            CollectionAssert.AreEqual(
                new byte[] { 90 },
                output.Planes[1]
            );
            CollectionAssert.AreEqual(
                new byte[] { 140 },
                output.Planes[2]
            );
            CollectionAssert.AreEqual(
                new[] { 2, 1, 1 },
                output.Strides
            );
        }
    }
}
