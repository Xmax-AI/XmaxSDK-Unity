//
//  EncodingControllerTests.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System.Collections;
using System.Threading;
using Agora.Rtc;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Xmax.SDK.Tests
{
    /// <summary>Covers bitrate defaults, explicit bounds, Agora mapping, and validation before connection.</summary>
    public sealed class EncodingControllerTests
    {

        /// <summary>Calculates default bitrate bounds for representative dimensions and frame rates.</summary>
        [TestCase(1920, 1080, 30, 500, 3780)]
        [TestCase(1024, 1920, 30, 500, 3619)]
        [TestCase(1024, 1920, 24, 500, 3127)]
        [TestCase(832, 1472, 24, 500, 2166)]
        [TestCase(640, 480, 30, 500, 900)]
        [TestCase(160, 120, 15, 100, 100)]
        [TestCase(4, 4, 1, 100, 100)]
        public void DefaultBudgetsMatchReferenceCases(
            int width,
            int height,
            int fps,
            int minimum,
            int maximum
        )
        {
            var actual = EncodingBitratePolicy.Resolve(new RealtimeVideoFormat(
                    width,
                    height,
                    fps
                ));
            Assert.AreEqual(
                minimum,
                actual.MinimumBitrate
            );
            Assert.AreEqual(
                maximum,
                actual.MaximumBitrate
            );
            Assert.AreEqual(
                RealtimeVideoEncoderPreference.MaintainFramerate,
                actual.EncoderPreference
            );
        }

        /// <summary>Preserves explicit encoding limits, including a disabled minimum.</summary>
        [TestCase(null, 4000, 500, 4000)]
        [TestCase(1500, null, 1500, 2166)]
        [TestCase(0, 50, 0, 50)]
        [TestCase(null, 100, 100, 100)]
        public void ExplicitBoundsArePreserved(
            int? minimum,
            int? maximum,
            int expectedMinimum,
            int expectedMaximum
        )
        {
            var actual = EncodingBitratePolicy.Resolve(new RealtimeVideoFormat(
                    832,
                    1472,
                    24,
                    minimum,
                    maximum
                ));
            Assert.AreEqual(
                expectedMinimum,
                actual.MinimumBitrate
            );
            Assert.AreEqual(
                expectedMaximum,
                actual.MaximumBitrate
            );
        }

        /// <summary>Rejects incompatible explicit and default bitrate limits.</summary>
        [TestCase(-1, null)]
        [TestCase(null, 0)]
        [TestCase(4000, null)]
        [TestCase(null, 99)]
        public void InvalidMergedBoundsAreRejected(
            int? minimum,
            int? maximum
        )
        {
            Assert.Throws<XmaxException>(() => EncodingBitratePolicy.Resolve(new RealtimeVideoFormat(
                        832,
                        1472,
                        24,
                        minimum,
                        maximum
                    )));
        }

        /// <summary>Requires four-pixel alignment for the uplink encoder.</summary>
        [Test]
        public void UplinkRejectsEvenDimensionsWithoutFourAlignment()
        {
            Assert.Throws<XmaxException>(() => EncodingBitratePolicy.Resolve(new RealtimeVideoFormat(
                        1026,
                        1920,
                        30
                    )));
        }

        /// <summary>Maps neutral encoding values and degradation preferences to Agora.</summary>
        [TestCase(RealtimeVideoEncoderPreference.Auto, DEGRADATION_PREFERENCE.MAINTAIN_AUTO)]
        [TestCase(RealtimeVideoEncoderPreference.MaintainFramerate, DEGRADATION_PREFERENCE.MAINTAIN_FRAMERATE)]
        [TestCase(RealtimeVideoEncoderPreference.MaintainQuality, DEGRADATION_PREFERENCE.MAINTAIN_QUALITY)]
        public void AgoraMappingPreservesEncodingValues(
            RealtimeVideoEncoderPreference preference,
            DEGRADATION_PREFERENCE expected
        )
        {
            var native = AgoraRtcPlatform.ToVideoEncoderConfig(EncodingBitratePolicy.Resolve(new RealtimeVideoFormat(
                        1920,
                        1024,
                        30,
                        0,
                        4500,
                        preference
                    )));
            Assert.AreEqual(
                1920,
                native.dimensions.width
            );
            Assert.AreEqual(
                1024,
                native.dimensions.height
            );
            Assert.AreEqual(
                30,
                native.frameRate
            );
            Assert.AreEqual(
                0,
                native.minBitrate
            );
            Assert.AreEqual(
                4500,
                native.bitrate
            );
            Assert.AreEqual(
                expected,
                native.degradationPreference
            );
        }

        /// <summary>Rejects invalid encoding before allocating a server session or replacing local preview.</summary>
        [UnityTest]
        public IEnumerator InvalidEncodingDoesNotCreateSessionsOrReplacePreview() => AsyncTest.Run(async () =>
        {
            var sessions = new FakeSessions();
            var stream = new FakeStream();
            var manager = new XmaxRealtimeManager(
                new XmaxConfiguration("test"),
                new RealtimeConfiguration(RealtimeModel.X2_1),
                sessions,
                stream
            );
            try
            {
                var local = manager.CreateLocalExternalStream();
                var invalid = new RealtimeVideoFormat(
                    1920,
                    1024,
                    30,
                    maximumBitrate: 99
                );
                Assert.Throws<XmaxException>(() => manager.CreateLocalExternalStream(invalid));
                await AsyncTest.Throws<XmaxException>(manager.ConnectAsync(invalid));
                Assert.AreSame(
                    local,
                    manager.LocalStream
                );
                Assert.AreEqual(
                    0,
                    sessions.Created
                );
            }
            finally
            {
                await manager.CloseAsync();
            }
        });
    }
}
