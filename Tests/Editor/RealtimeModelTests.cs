//
//  RealtimeModelTests.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Xmax.SDK.Tests
{
    /// <summary>Covers model capabilities and independent media and generation sizing.</summary>
    public sealed class RealtimeModelTests
    {

        /// <summary>Registers the X2.1 server name with its sizing capabilities.</summary>
        [TestCase(RealtimeModel.X2_1, "x2.1")]
        public void X21ModelHasExpectedSizingRules(
            RealtimeModel model,
            string name
        )
        {
            var definition = RealtimeModelRegistry.Get(model);
            Assert.AreEqual(
                name,
                definition.Name
            );
            var capability = definition.Capabilities;
            Assert.AreSame(
                capability,
                RealtimeModelRegistry.Get(model).Capabilities
            );
            Assert.AreEqual(
                200000,
                capability.MinimumPixels
            );
            Assert.AreEqual(
                2100000,
                capability.MaximumPixels
            );
            Assert.AreEqual(
                32,
                capability.DimensionAlignment
            );
            Assert.AreEqual(
                new RealtimeVideoFormat(
                    1080,
                    1920,
                    30
                ),
                capability.DefaultCameraVideoFormat
            );
            Assert.AreEqual(
                5,
                capability.ResolutionBuckets.Count
            );
            Assert.Throws<ArgumentOutOfRangeException>(() => RealtimeModelRegistry.Get((RealtimeModel)2));
            Assert.Throws<ArgumentOutOfRangeException>(() => RealtimeModelRegistry.Get((RealtimeModel)3));
        }

        /// <summary>Resolves representative external input dimensions to the expected media baseline.</summary>
        [TestCase(3840, 2160, 1920, 1080)]
        [TestCase(6000, 8000, 1248, 1664)]
        [TestCase(256, 480, 352, 660)]
        [TestCase(200, 200, 448, 448)]
        [TestCase(1, 1, 448, 448)]
        [TestCase(1001, 500, 1000, 500)]
        public void ExternalSizeMatchesReferenceCases(
            int width,
            int height,
            int expectedWidth,
            int expectedHeight
        )
        {
            var result = new MediaService().ResolveMediaSize(
                RealtimeModel.X2_1,
                width,
                height
            );
            Assert.AreEqual(
                expectedWidth,
                result.Width
            );
            Assert.AreEqual(
                expectedHeight,
                result.Height
            );
        }

        /// <summary>Keeps unusual aspect ratios inside the model pixel and alignment limits.</summary>
        [TestCase(1, 200001)]
        [TestCase(447, 448)]
        [TestCase(999, 501)]
        [TestCase(1449, 1450)]
        [TestCase(65535, 65537)]
        public void ExtremeAspectRatiosRespectAreaAndAlignment(
            int width,
            int height
        )
        {
            var service = new MediaService();
            var result = service.ResolveMediaSize(
                RealtimeModel.X2_1,
                width,
                height
            );
            Assert.That(
                (long)result.Width * result.Height,
                Is.InRange(
                    200000L,
                    2100000L
                )
            );
            Assert.AreEqual(
                0,
                result.Width % 4
            );
            Assert.AreEqual(
                0,
                result.Height % 4
            );
        }

        /// <summary>Applies camera bucket constraints only to camera resolution decisions.</summary>
        [Test]
        public void ExternalBaselineAndGenerationCanvasAreIndependentOfCameraBuckets()
        {
            var service = new MediaService();
            var model = RealtimeModel.X2_1;
            var media = service.ResolveMediaSize(
                model,
                1920,
                1080
            );
            var canvas = service.ResolveGenerationSize(
                model,
                media.Width,
                media.Height
            );
            Assert.AreEqual(
                new RealtimeVideoSize(
                    1920,
                    1080
                ),
                media
            );
            Assert.AreEqual(
                new RealtimeVideoSize(
                    1920,
                    1080
                ),
                canvas
            );
            Assert.Throws<XmaxException>(() => service.ResolveMediaSize(
                    model,
                    1024,
                    1920,
                    RealtimeMediaSource.Camera
                ));
            Assert.AreEqual(
                new RealtimeVideoSize(
                    1080,
                    1920
                ),
                service.ResolveMediaSize(
                    model,
                    1080,
                    1920,
                    RealtimeMediaSource.Camera
                )
            );
        }

        /// <summary>Bounds return dimensions without imposing uplink area requirements.</summary>
        [Test]
        public void TargetBoundsDoNotUpscaleOrImposeTheInputMinimum()
        {
            var service = new MediaService();
            Assert.AreEqual(
                new RealtimeVideoSize(
                    224,
                    224
                ),
                service.ResolveTargetSize(
                    448,
                    448,
                    224,
                    224
                )
            );
            Assert.AreEqual(
                new RealtimeVideoSize(
                    640,
                    480
                ),
                service.ResolveTargetSize(
                    640,
                    480,
                    1920,
                    1080
                )
            );
            Assert.Throws<XmaxException>(() => service.ResolveTargetSize(
                    640,
                    480,
                    double.NaN,
                    480
                ));
            Assert.Throws<XmaxException>(() => service.ResolveTargetSize(
                    2,
                    2,
                    2,
                    2
                ));
        }

        /// <summary>Creates and resizes local preview without opening a server session.</summary>
        [UnityTest]
        public IEnumerator ExternalPreviewResolvesMediaWithoutCredentials() => AsyncTest.Run(async () =>
        {
            var manager = new XmaxRealtimeManager("");
            try
            {
                var local = manager.CreateLocalExternalStream(new RealtimeVideoFormat(
                        200,
                        200,
                        30
                    ));
                Assert.AreEqual(
                    200,
                    local.SourceVideoFormat.Value.Width
                );
                Assert.AreEqual(
                    448,
                    local.VideoFormat.Value.Width
                );
                XmaxVideoFrame preview = null;
                local.VideoTrack.FrameReceived += frame => preview = frame.Clone();
                local.PushVideoFrame(XmaxVideoFrame.CreateRgba(
                        new byte[200 * 200 * 4],
                        200,
                        200
                    ));
                Assert.AreEqual(
                    448,
                    preview.Width
                );
                Assert.AreEqual(
                    448,
                    preview.Height
                );
                await AsyncTest.Throws<XmaxException>(manager.ConnectAsync(local));
                Assert.AreEqual(
                    448,
                    local.VideoFormat.Value.Width
                );
            }
            finally
            {
                await manager.CloseAsync();
            }
        });
    }
}
