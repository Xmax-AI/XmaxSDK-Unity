//
//  AdaptiveQualityTests.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Xmax.SDK.Tests
{
    /// <summary>Covers directional sizing, adaptation timing, history, and configuration lifecycle.</summary>
    public sealed class AdaptiveQualityTests
    {

        /// <summary>The portrait media baseline shared by the policy cases.</summary>
        private static RealtimeVideoFormat Baseline => new RealtimeVideoFormat(
            1024,
            1920,
            30
        );

        /// <summary>Keeps transport dimensions tied to the stable baseline at every quality level.</summary>
        [TestCase(RealtimeQualityLevel.L1, 1024, 1920, 30)]
        [TestCase(RealtimeQualityLevel.L2, 896, 1680, 24)]
        [TestCase(RealtimeQualityLevel.L3, 768, 1440, 24)]
        [TestCase(RealtimeQualityLevel.L4, 608, 1140, 20)]
        [TestCase(RealtimeQualityLevel.L5, 512, 960, 16)]
        public void UplinkAndFinalTargetAlwaysDeriveFromBaseline(
            RealtimeQualityLevel level,
            int width,
            int height,
            int fps
        )
        {
            var up = QualityVideoFormat.Resolve(
                Baseline,
                level,
                false,
                200000
            );
            Assert.AreEqual(
                new RealtimeVideoFormat(
                    width,
                    height,
                    fps
                ),
                up
            );
            var target = QualityVideoFormat.ResolveTarget(
                Baseline,
                level, new MediaService()
            );
            Assert.AreEqual(
                width,
                target.Width
            );
            Assert.AreEqual(
                height,
                target.Height
            );
            Assert.AreEqual(
                24,
                target.Fps
            );
        }

        /// <summary>Distinguishes scaled two-aligned bounds from the final four-aligned target.</summary>
        [Test]
        public void RawDownlinkBoundsAndFinalAlignmentAreIndependent()
        {
            var raw = QualityVideoFormat.Resolve(
                Baseline,
                RealtimeQualityLevel.L2,
                true
            );
            Assert.AreEqual(
                912,
                raw.Width
            );
            Assert.AreEqual(
                1710,
                raw.Height
            );
            var target = QualityVideoFormat.ResolveTarget(
                Baseline,
                RealtimeQualityLevel.L2, new MediaService()
            );
            Assert.AreEqual(
                896,
                target.Width
            );
            Assert.AreEqual(
                1680,
                target.Height
            );
        }

        /// <summary>Applies the input area floor only to uplink and retains source frame-rate limits.</summary>
        [Test]
        public void UplinkMinimumIsPreservedWithoutChangingDownlinkOrExceedingSourceFrameRate()
        {
            var small = new RealtimeVideoFormat(
                448,
                448,
                12,
                maximumBitrate: 3000
            );
            var up = QualityVideoFormat.Resolve(
                small,
                RealtimeQualityLevel.L5,
                false,
                200000
            );
            Assert.GreaterOrEqual(
                up.Width * up.Height,
                200000
            );
            Assert.AreEqual(
                12,
                up.Fps
            );
            Assert.AreEqual(
                3000,
                up.MaximumBitrate
            );
            var down = QualityVideoFormat.ResolveTarget(
                small,
                RealtimeQualityLevel.L5, new MediaService()
            );
            Assert.Less(
                down.Width * down.Height,
                200000
            );
            Assert.IsNull(down.MaximumBitrate);
        }

        /// <summary>Keeps the applied level unchanged until success and stabilizes after failure.</summary>
        [Test]
        public void ThreeBadSamplesProposeButDoNotCommitAndFailuresStabilize()
        {
            var direction = new QualityDirection(
                Baseline,
                RealtimeQualityLevel.L2,
                false,
                200000
            );
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    1,
                    1
                ));
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.VeryBad,
                    2,
                    2
                ));
            Assert.AreEqual(
                RealtimeQualityLevel.L3,
                direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    3,
                    3
                )
            );
            Assert.AreEqual(
                RealtimeQualityLevel.L2,
                direction.Level
            );
            direction.Complete(
                RealtimeQualityLevel.L3,
                false,
                3
            );
            Assert.AreEqual(
                RealtimeQualityLevel.L2,
                direction.Level
            );
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    4,
                    4
                ));
        }

        /// <summary>Blocks upgrades after a downgrade during the observation period.</summary>
        [Test]
        public void FailedUpgradeProbeHoldsUpgradesForSixtySeconds()
        {
            var direction = new QualityDirection(
                Baseline,
                RealtimeQualityLevel.L3,
                false,
                200000
            );
            for (var time = 1; time < 5; time++)
                Assert.IsNull(direction.Observe(
                        RealtimeNetworkQualityLevel.Good,
                        time,
                        time
                    ));
            Assert.AreEqual(
                RealtimeQualityLevel.L2,
                direction.Observe(
                    RealtimeNetworkQualityLevel.Good,
                    5,
                    5
                )
            );
            direction.Complete(
                RealtimeQualityLevel.L2,
                true,
                5
            );
            for (var time = 9; time < 11; time++)
                Assert.IsNull(direction.Observe(
                        RealtimeNetworkQualityLevel.Bad,
                        time,
                        time
                    ));
            Assert.AreEqual(
                RealtimeQualityLevel.L3,
                direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    11,
                    11
                )
            );
            direction.Complete(
                RealtimeQualityLevel.L3,
                true,
                11
            );
            for (var time = 15; time < 71; time++)
                Assert.IsNull(direction.Observe(
                        RealtimeNetworkQualityLevel.Good,
                        time,
                        time
                    ));
            Assert.AreEqual(
                RealtimeQualityLevel.L2,
                direction.Observe(
                    RealtimeNetworkQualityLevel.Good,
                    71,
                    71
                )
            );
        }

        /// <summary>Rejects invalid samples and resets accumulation across interruptions.</summary>
        [Test]
        public void StaleDuplicateFutureUnknownAndGappedSamplesCannotTriggerFalseDowngrades()
        {
            var direction = new QualityDirection(
                Baseline,
                RealtimeQualityLevel.L2,
                true,
                0
            );
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    1,
                    10
                ));
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    11,
                    11
                ));
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    11,
                    11
                ));
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    20,
                    12
                ));
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.Unknown,
                    12,
                    12
                ));
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    13,
                    13
                ));
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    20,
                    20
                ));
            Assert.IsNull(direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    21,
                    21
                ));
            Assert.AreEqual(
                RealtimeQualityLevel.L3,
                direction.Observe(
                    RealtimeNetworkQualityLevel.Bad,
                    22,
                    22
                )
            );
        }

        /// <summary>Expires history independently for each model and transport direction.</summary>
        [Test]
        public void HistoryExpiresAndSeparatesModelsAndDirections()
        {
            var store = new Dictionary<string, string>();
            Func<string, string> read = key => store.TryGetValue(
                key,
                out var value
            ) ? value : "";
            Action<string, string> write = (
                key,
                value
            ) => store[key] = value;
            var history = new QualityHistory(
                "a",
                read,
                write
            );
            Assert.AreEqual(
                RealtimeQualityLevel.L2,
                history.Read(
                    true,
                    100
                )
            );
            history.Write(
                true,
                RealtimeQualityLevel.L4,
                100
            );
            Assert.AreEqual(
                RealtimeQualityLevel.L4,
                history.Read(
                    true,
                    200
                )
            );
            Assert.AreEqual(
                RealtimeQualityLevel.L2,
                history.Read(
                    false,
                    200
                )
            );
            Assert.AreEqual(
                RealtimeQualityLevel.L2,
                history.Read(
                    true,
                    3700
                )
            );
            Assert.AreEqual(
                RealtimeQualityLevel.L2,
                history.Read(
                    true,
                    99
                )
            );
            Assert.AreEqual(
                RealtimeQualityLevel.L2,
                new QualityHistory(
                    "b",
                    read,
                    write
                ).Read(
                    true,
                    200
                )
            );
        }

        /// <summary>Keeps media geometry stable and preserves working settings after configuration failure.</summary>
        [UnityTest]
        public IEnumerator ManualChangesPreserveMediaAndCanvasAndCommitOnlySuccess() => AsyncTest.Run(async () =>
        {
            var stream = new FakeStream();
            var manager = new XmaxRealtimeManager(
                new XmaxConfiguration("test"),
                new RealtimeConfiguration(
                    RealtimeModel.X2_1,
                    false
                ),
                new FakeSessions(),
                stream
            );
            try
            {
                var local = manager.CreateLocalExternalStream(new RealtimeVideoFormat(
                        1920,
                        1080,
                        30
                    ));
                await manager.StartGenerationAsync(
                    local,
                    new RealtimeContext("test")
                );
                Assert.AreEqual(
                    RealtimeQualityLevel.L1,
                    manager.UplinkQualityLevel
                );
                Assert.AreEqual(
                    new RealtimeVideoFormat(
                        1920,
                        1080,
                        30
                    ),
                    manager.GenerationVideoFormat.Value
                );
                await manager.SetUplinkQualityLevelAsync(RealtimeQualityLevel.L5);
                await manager.SetDownlinkQualityLevelAsync(RealtimeQualityLevel.L3);
                Assert.AreEqual(
                    1920,
                    local.VideoFormat.Value.Width
                );
                Assert.AreEqual(
                    1080,
                    local.VideoFormat.Value.Height
                );
                Assert.AreEqual(
                    1920,
                    manager.GenerationVideoFormat.Value.Width
                );
                Assert.AreEqual(
                    1080,
                    manager.GenerationVideoFormat.Value.Height
                );
                Assert.AreEqual(
                    RealtimeQualityLevel.L5,
                    manager.UplinkQualityLevel
                );
                var previous = manager.QualityConfiguration;
                stream.RejectEncoding = true;
                await AsyncTest.Throws<XmaxException>(manager.SetUplinkQualityLevelAsync(RealtimeQualityLevel.L2));
                Assert.AreEqual(
                    previous.Uplink,
                    manager.QualityConfiguration.Uplink
                );
                stream.ChangeTarget = token => Task.FromException(new XmaxException(
                        XmaxErrorCode.RtcError,
                        "Rejected"
                    ));
                await AsyncTest.Throws<XmaxException>(manager.SetDownlinkQualityLevelAsync(RealtimeQualityLevel.L5));
                Assert.AreEqual(
                    previous.Downlink,
                    manager.QualityConfiguration.Downlink
                );
                Assert.AreEqual(
                    RealtimeConnectionState.Generating,
                    manager.CurrentState.ConnectionState
                );
            }
            finally
            {
                await manager.CloseAsync();
            }
        });

        /// <summary>Cancels an in-flight return-size change before clearing connection state.</summary>
        [UnityTest]
        public IEnumerator DisconnectCancelsPendingTargetChangeBeforePublishingNewState() => AsyncTest.Run(async () =>
        {
            var stream = new FakeStream();
            var manager = new XmaxRealtimeManager(
                new XmaxConfiguration("test"),
                new RealtimeConfiguration(
                    RealtimeModel.X2_1,
                    false
                ),
                new FakeSessions(),
                stream
            );
            try
            {
                var local = manager.CreateLocalExternalStream(Baseline);
                await manager.StartGenerationAsync(
                    local,
                    new RealtimeContext("test")
                );
                stream.ChangeTarget = token => Task.Delay(
                    10000,
                    token
                );
                var change = manager.SetDownlinkQualityLevelAsync(RealtimeQualityLevel.L5);
                var close = manager.CloseAsync();
                await AsyncTest.Throws<OperationCanceledException>(change);
                await close;
                Assert.IsNull(manager.DownlinkQualityLevel);
                Assert.IsNull(manager.QualityConfiguration);
                Assert.AreEqual(
                    RealtimeConnectionState.Idle,
                    manager.CurrentState.ConnectionState
                );
            }
            finally
            {
                await manager.CloseAsync();
            }
        });
    }
}
