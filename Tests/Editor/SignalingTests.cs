//
//  SignalingTests.cs
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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Xmax.SDK.Tests
{
    /// <summary>Covers room packet encoding, queue ordering, cancellation, and generation confirmation.</summary>
    public sealed class SignalingTests
    {

        /// <summary>
        /// Completes an admitted condition update without waiting for its native send.
        /// </summary>
        [UnityTest]
        public IEnumerator AdmittedConditionUpdateCompletesBeforeSendingAndKeepsGenerationUsable() => AsyncTest.Run(async () =>
        {
            var sending = new TaskCompletionSource<bool>();
            var rtc = new FakeRtc
            {
                Send = (
                    message,
                    token
                ) => message.Contains("\"change_condition\"")
                    ? sending.Task : Task.CompletedTask
            };
            var stream = new StreamController(
                rtc,
                500
            );
            var baseline = new RealtimeVideoFormat(
                1024,
                1920,
                30
            );
            stream.ConfigureMedia(
                baseline,
                baseline,
                baseline
            );
            await stream.ConnectAsync(
                FakeSessions.Session().JoinInfo,
                baseline,
                CancellationToken.None
            );
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    rtc.Publish();
                    var task = "task";
                    await stream.StartGenerationAsync("task",
                        new RealtimeContext("test"),
                        baseline,
                        CancellationToken.None
                    );
                    var change = stream.ChangeGenerationConditionAsync(
                        new RealtimeContext("updated"),
                        baseline,
                        task,
                        cancellation.Token
                    );
                    await change;
                    cancellation.Cancel();
                    Assert.False(sending.Task.IsCompleted);
                    sending.TrySetResult(true);
                    var received = 0;
                    stream.FrameReceived += frame => received++;
                    rtc.Frame();
                    Assert.AreEqual(
                        1,
                        received
                    );
                }
                finally
                {
                    sending.TrySetResult(true);
                    await stream.DisconnectAsync();
                }
            }
        });

        /// <summary>Reassembles escaped Unicode fragments without splitting a scalar or exceeding the packet limit.</summary>
        [Test]
        public void UnicodeFragmentsRoundTripWithinPacketBudget()
        {
            var message = string.Concat(Enumerable.Repeat(
                    "中文🙂\\\"\n/",
                    250
                ));
            var packets = RtcMessageCodec.Encode(message);
            Assert.Greater(
                packets.Count,
                1
            );
            var restored = new StringBuilder();
            string identifier = null;
            for (var index = 0; index < packets.Count; index++)
            {
                Assert.LessOrEqual(
                    packets[index].Length,
                    800
                );
                var fragment = JsonCodec.Decode(Encoding.UTF8.GetString(packets[index]));
                Assert.AreEqual(
                    "__agora_chunk__",
                    (string)fragment["event"]
                );
                Assert.AreEqual(
                    index,
                    (int)fragment["index"]
                );
                Assert.AreEqual(
                    packets.Count,
                    (int)fragment["count"]
                );
                if (identifier == null)
                    identifier = (string)fragment["eventId"];
                Assert.AreEqual(
                    identifier,
                    (string)fragment["eventId"]
                );
                restored.Append((string)fragment["data"]);
            }
            Assert.AreEqual(
                message,
                restored.ToString()
            );
            Assert.Throws<XmaxException>(() => RtcMessageCodec.Encode(""));
            Assert.Throws<XmaxException>(() => RtcMessageCodec.Encode(new string(
                        'x',
                        262145
                    )));
        }

        /// <summary>Replaces pending trajectories while preserving control-message ordering.</summary>
        [UnityTest]
        public IEnumerator QueueCoalescesTrajectoriesWithoutDroppingOrReorderingControls() => AsyncTest.Run(async () =>
        {
            var sent = new List<string>();
            var queue = new RtcMessageQueue(
                packet => sent.Add(Encoding.UTF8.GetString(packet)),
                error => Assert.Fail(error.Message)
            );
            try
            {
                var start = queue.Enqueue("start");
                Task tracks = null;
                for (var i = 0; i < 200; i++)
                    tracks = queue.Enqueue(
                        "position-" + i,
                        key: "tracks:task"
                    );
                var condition = queue.Enqueue("condition");
                var stop = queue.Enqueue("stop");
                await Task.WhenAll(
                    start,
                    tracks,
                    condition,
                    stop
                );
                CollectionAssert.AreEqual(
                    new[] { "start", "position-199", "condition", "stop" },
                    sent
                );
            }
            finally
            {
                await queue.CloseAsync();
            }
        });

        /// <summary>Keeps the queue usable after a recoverable native-send failure.</summary>
        [UnityTest]
        public IEnumerator RecoverableSendFailureDoesNotPoisonFollowingMessages() => AsyncTest.Run(async () =>
        {
            var sent = new List<string>();
            var queue = new RtcMessageQueue(
                packet =>
                {
                    var text = Encoding.UTF8.GetString(packet);
                    if (text == "resize")
                        throw new XmaxException(
                            XmaxErrorCode.RtcError,
                            "Rejected"
                        );
                    sent.Add(text);
                },
                error => Assert.Fail("Recoverable failure must not terminate the room.")
            );
            try
            {
                var failed = queue.Enqueue(
                    "resize",
                    fatal: false
                );
                var next = queue.Enqueue("condition");
                await AsyncTest.Throws<XmaxException>(failed);
                await next;
                CollectionAssert.AreEqual(
                    new[] { "condition" },
                    sent
                );
            }
            finally
            {
                await queue.CloseAsync();
            }
        });

        /// <summary>Cancels the caller wait while preserving admitted control packets and ordering.</summary>
        [UnityTest]
        public IEnumerator CancelingQueuedControlWaitPreservesItsAdmittedPackets() => AsyncTest.Run(async () =>
        {
            var sent = new List<string>();
            var queue = new RtcMessageQueue(
                packet => sent.Add(Encoding.UTF8.GetString(packet)),
                error => Assert.Fail(error.Message)
            );
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    var start = queue.Enqueue("start");
                    var canceled = queue.Enqueue(
                        "canceled",
                        cancellation.Token
                    );
                    var next = queue.Enqueue("next");
                    cancellation.Cancel();
                    await AsyncTest.Throws<OperationCanceledException>(canceled);
                    await Task.WhenAll(
                        start,
                        next
                    );
                    CollectionAssert.AreEqual(
                        new[] { "start", "canceled", "next" },
                        sent
                    );
                }
                finally
                {
                    await queue.CloseAsync();
                }
            }
        });

        /// <summary>Enforces the byte quota and cancels outstanding work when the queue closes.</summary>
        [UnityTest]
        public IEnumerator ByteBudgetDelaysPacketsAndCloseCancelsPendingWork() => AsyncTest.Run(async () =>
        {
            var sent = 0;
            var queue = new RtcMessageQueue(
                packet => sent++,
                error => Assert.Fail(error.Message)
            );
            try
            {
                var watch = Stopwatch.StartNew();
                var tasks = new List<Task>();
                for (var i = 0; i < 37; i++)
                    tasks.Add(queue.Enqueue(new string(
                                'a',
                                800
                            )));
                await Task.WhenAll(tasks);
                Assert.GreaterOrEqual(
                    watch.ElapsedMilliseconds,
                    950
                );
                Assert.AreEqual(
                    37,
                    sent
                );
                Task last = null;
                for (var i = 0; i < 60; i++)
                    last = queue.Enqueue("queued");
                await queue.CloseAsync();
                await AsyncTest.Throws<OperationCanceledException>(last);
            }
            finally
            {
                await queue.CloseAsync();
            }
        });

        /// <summary>Requires the expected bot and complete start submission before forwarding frames.</summary>
        [UnityTest]
        public IEnumerator PublishedBotDoesNotConfirmBeforeStartMessageFinishes() => AsyncTest.Run(async () =>
        {
            var sent = AsyncTest.Pending<bool>();
            var rtc = new FakeRtc
            {
                Send = (
                    message,
                    token
                ) => message.Contains("\"start\"") ? sent.Task : Task.CompletedTask
            };
            var stream = new StreamController(
                rtc,
                500
            );
            var baseline = new RealtimeVideoFormat(
                1920,
                1080,
                30
            );
            stream.ConfigureMedia(
                baseline,
                new RealtimeVideoFormat(
                    1920,
                    1024,
                    30
                ),
                baseline
            );
            await stream.ConnectAsync(
                FakeSessions.Session().JoinInfo,
                baseline,
                CancellationToken.None
            );
            try
            {
                var frames = 0;
                stream.FrameReceived += frame => frames++;
                rtc.Publish(new RemoteStream(
                        "room",
                        "other"
                    ));
                rtc.Publish();
                var start = stream.StartGenerationAsync("task", 
                    new RealtimeContext("test"),
                    baseline,
                    CancellationToken.None
                );
                rtc.Frame();
                Assert.False(start.IsCompleted);
                Assert.AreEqual(
                    0,
                    frames
                );
                sent.SetResult(true);
                await start;
                Assert.That(rtc.Messages[0], Does.Contain("task"));
                rtc.Frame();
                Assert.AreEqual(
                    1,
                    frames
                );
            }
            finally
            {
                sent.TrySetResult(true);
                await stream.DisconnectAsync();
            }
        });

        /// <summary>Keeps generation, return dimensions, and baseline interaction coordinates distinct on the wire.</summary>
        [Test]
        public void WireMessagesSeparateCanvasTargetAndNormalizedTracks()
        {
            var baseline = new RealtimeVideoFormat(
                1920,
                1080,
                30
            );
            var generation = new RealtimeVideoFormat(
                1920,
                1024,
                30
            );
            var start = JsonCodec.Decode(RtcRoomEvent.Start(
                    "user",
                    "task",
                    generation,
                    new RealtimeVideoFormat(
                        960,
                        540,
                        24
                    ),
                    new RealtimeContext("prompt"),
                    true
                ));
            Assert.AreEqual(
                "contain",
                (string)start["params"]["padding"]
            );
            Assert.AreEqual(
                "task",
                (string)start["uid"]
            );
            Assert.AreEqual(
                "task",
                (string)start["session_uid"]
            );
            var tracks = RtcRoomEvent.Tracks(
                "user",
                "task",
                new[] { new RealtimePoint(
                    0.5,
                    0.5
                ) }
            );
            Assert.That(
                tracks,
                Does.Contain("0.5")
            );
            Assert.That(
                tracks,
                Does.Not.Contain("960")
            );
            var changed = JsonCodec.Decode(RtcRoomEvent.ChangeTargetSize(
                    "user",
                    "task",
                    new RealtimeVideoFormat(
                        960,
                        540,
                        24
                    )
                ));
            Assert.AreEqual(
                "change_target_size",
                (string)changed["event"]
            );
            Assert.AreEqual(
                500,
                (int)changed["params"]["min_bitrate"]
            );
        }
    }
}
