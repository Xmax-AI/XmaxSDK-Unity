using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Xmax.SDK.Tests
{
    public sealed class StreamAndMediaTests
    {
        private static RealtimeVideoFormat Format => new RealtimeVideoFormat(1280, 720, 30);
        [UnityTest] public IEnumerator StreamFiltersResultUserAndRebindsAfterRepublish() => AsyncTest.Run(async () =>
        {
            var rtc = new FakeRtc();
            var stream = new StreamController(rtc, 500);
            await stream.ConnectAsync(FakeSessions.Session().JoinInfo, Format, CancellationToken.None);
            try
            {
                var frames = 0;
                stream.FrameReceived += frame => frames++;
                var start = stream.StartGenerationAsync("test-task", new RealtimeContext("prompt"), Format, CancellationToken.None);
                rtc.Frame();
                rtc.Frame(new RemoteStream("room", "other"));
                Assert.False(start.IsCompleted);
                Assert.AreEqual(0, frames);
                rtc.Publish();
                await start;
                rtc.Frame();
                rtc.Unpublish(); rtc.Frame();
                rtc.Publish(); rtc.Publish(); rtc.Frame();
                Assert.AreEqual(2, frames);
                Assert.AreEqual(2, rtc.Subscriptions);
                await stream.StopGenerationAsync();
                rtc.Publish(); rtc.Frame();
                Assert.AreEqual(2, frames);
                Assert.That(rtc.Messages[rtc.Messages.Count - 1], Does.Contain("test-task"));
            }
            finally { await stream.DisconnectAsync(); }
        });
        [UnityTest] public IEnumerator GenerationTimeoutSendsStopAndAllowsRetry() => AsyncTest.Run(async () =>
        {
            var rtc = new FakeRtc();
            var stream = new StreamController(rtc, 30);
            await stream.ConnectAsync(FakeSessions.Session().JoinInfo, Format, CancellationToken.None);
            try
            {
                await AsyncTest.Throws<XmaxException>(stream.StartGenerationAsync("test-task", new RealtimeContext("prompt"), Format, CancellationToken.None));
                Assert.That(rtc.Messages[rtc.Messages.Count - 1], Does.Contain("stop"));
                var retry = stream.StartGenerationAsync("test-task", new RealtimeContext("retry"), Format, CancellationToken.None);
                rtc.Publish();
                await retry;
                Assert.That(rtc.Messages[0], Does.Contain("test-task"));
            }
            finally { await stream.DisconnectAsync(); }
        });
        [UnityTest] public IEnumerator RtcFailureAfterConfirmationStillRaisesFatalError() => AsyncTest.Run(async () =>
        {
            var rtc = new FakeRtc();
            var stream = new StreamController(rtc, 500);
            var failure = AsyncTest.Pending<XmaxException>();
            stream.FatalError += error => failure.TrySetResult(error);
            await stream.ConnectAsync(FakeSessions.Session().JoinInfo, Format, CancellationToken.None);
            try
            {
                var start = stream.StartGenerationAsync("test-task", new RealtimeContext("prompt"), Format, CancellationToken.None);
                rtc.Publish(); await start;
                rtc.Fail();
                await AsyncDeadline.WaitAsync(failure.Task, 1000, CancellationToken.None);
                Assert.AreEqual(XmaxErrorCode.RtcError, (await failure.Task).Code);
            }
            finally { await stream.DisconnectAsync(); }
        });
        [UnityTest] public IEnumerator EngineLeaseCancellationDoesNotReleaseCurrentOwner() => AsyncTest.Run(async () =>
        {
            using (var first = await RtcEngineManager.AcquireAsync(CancellationToken.None))
            using (var cancelled = new CancellationTokenSource())
            {
                var waiting = RtcEngineManager.AcquireAsync(cancelled.Token);
                Assert.False(waiting.IsCompleted);
                cancelled.Cancel();
                await AsyncTest.Throws<OperationCanceledException>(waiting);
                var next = RtcEngineManager.AcquireAsync(CancellationToken.None);
                Assert.False(next.IsCompleted);
                first.Dispose();
                using (await next) { first.Dispose(); }
            }
            using (await RtcEngineManager.AcquireAsync(CancellationToken.None)) { }
        });
        [UnityTest] public IEnumerator LateHeartbeatResponseIsIgnoredAfterStop() => AsyncTest.Run(async () =>
        {
            var response = AsyncTest.Pending<XmaxSession>();
            var requested = AsyncTest.Pending<bool>();
            var sessions = new FakeSessions { Heartbeat = token => { requested.TrySetResult(true); return response.Task; } };
            var heartbeat = new SessionHeartbeat(sessions, token => Task.CompletedTask);
            var failures = 0;
            heartbeat.Failed += error => failures++;
            heartbeat.Start("old");
            await requested.Task;
            var stop = heartbeat.StopAsync();
            response.SetResult(new XmaxSession { SessionUid = "old", Status = "CLOSED" });
            await stop;
            Assert.AreEqual(0, failures);
        });
        [Test] public void I420RejectsShortRowsAndOverflow()
        {
            Assert.Throws<XmaxException>(() => XmaxVideoFrame.CreateI420(new byte[16], new byte[4], new byte[4], 4, 4, 2));
            Assert.Throws<XmaxException>(() => XmaxVideoFrame.CreateI420(new byte[4], new byte[1], new byte[1], 2, 2, int.MaxValue));
            Assert.Throws<XmaxException>(() => XmaxVideoFrame.CreateRgba(new byte[4], int.MaxValue - 1, 2));
            var mutable = AsyncTest.Frame(); mutable.Strides[0] = -1;
            Assert.Throws<XmaxException>(() => mutable.Validate());
        }
        [Test] public void CloneOwnsPlanesAndStrides()
        {
            var original = AsyncTest.Frame(); var copy = original.Clone();
            original.Planes[0][0] = 42; original.Strides[0] = 20;
            Assert.AreEqual(0, copy.Planes[0][0]);
            Assert.AreEqual(2, copy.Strides[0]); copy.Validate();
        }
        [Test] public void FitRejectsLetterboxAndFillAccountsForCrop()
        {
            var video = new RealtimeVideoFormat(200, 100, 30);
            Assert.False(InteractionCoordinateMapper.TryMap(50, 10, 100, 100, video, RealtimeContentMode.Fit, out _));
            Assert.True(InteractionCoordinateMapper.TryMap(50, 50, 100, 100, video, RealtimeContentMode.Fit, out var center));
            Assert.AreEqual(0.5f, center.x); Assert.AreEqual(0.5f, center.y);
            Assert.True(InteractionCoordinateMapper.TryMap(0, 0, 100, 100, video, RealtimeContentMode.Fill, out var crop));
            Assert.AreEqual(0.25f, crop.x); Assert.AreEqual(0, crop.y);
            Assert.False(InteractionCoordinateMapper.TryMap(double.NaN, 0, 100, 100, video, RealtimeContentMode.Fill, out _));
        }
        [TestCase(1920, 1080)] [TestCase(100, 100)] [TestCase(10000, 100)] [TestCase(1, 10000)] [TestCase(832, 1472)]
        public void RecommendedFormatSatisfiesModelLimits(int width, int height)
        {
            var format = new MediaService().RecommendVideoFormat(RealtimeModel.X2_1, width, height);
            Assert.AreEqual(0, format.Width % 4); Assert.AreEqual(0, format.Height % 4);
            Assert.That(format.Width * format.Height, Is.InRange(200000, 2100000));
            Assert.AreEqual(30, format.Fps);
        }
        [Test] public void ApiEnvelopeHandlesEmptyDeleteAndMalformedResponses()
        {
            Assert.IsNull(ApiService.ParseEnvelope("", 204, false, null, true));
            Assert.Throws<XmaxException>(() => ApiService.ParseEnvelope("null", 200, false, null));
            Assert.Throws<XmaxException>(() => ApiService.ParseEnvelope("[]", 200, false, null));
            Assert.Throws<XmaxException>(() => ApiService.ParseEnvelope("not-json", 200, false, null));
            var error = Assert.Throws<XmaxException>(() => ApiService.ParseEnvelope("{\"success\":false,\"code\":42,\"message\":\"bad\"}", 400, false, null));
            Assert.AreEqual(42, error.ApiCode); Assert.AreEqual(400, error.HttpStatus);
        }
        [Test] public void RecommendationAppliesRoundingBeforeBoundarySearch()
        {
            var model = RealtimeModel.X2_1;
            var service = new MediaService();
            var small = service.RecommendVideoFormat(model, 100, 100);
            Assert.AreEqual(448, small.Width); Assert.AreEqual(448, small.Height);
            var large = service.RecommendVideoFormat(model, 1920, 1080);
            Assert.AreEqual(1920, large.Width); Assert.AreEqual(1080, large.Height);
        }
        [TestCase(false)] [TestCase(true)] public void SessionParsesObjectAndStringModelExtra(bool encoded)
        {
            var extra = "{\"rtc_app_id\":\"" + RtcEngineManager.DefaultAppId + "\",\"room_id\":\"room\",\"room_token\":\"token\",\"rtc_user_id\":\"user\",\"rtc_bot_id\":\"bot\"}";
            var data = JsonCodec.Decode("{\"sessionUid\":\"session\",\"userUid\":\"user\",\"modelExtra\":" +
                (encoded ? "\"" + extra.Replace("\"", "\\\"") + "\"" : extra) + "}");
            var info = RealtimeSessionService.RequireJoinInfo(RealtimeSessionService.ParseSession(data));
            Assert.AreEqual("user", info.UserId); Assert.AreEqual(RtcEngineManager.DefaultAppId, info.AppId);
        }
        [Test] public void TextureUploaderHandlesStrideAndResolutionChange()
        {
            using (var textures = new XmaxVideoTexture())
            {
                var frame = XmaxVideoFrame.CreateI420(new byte[] { 1, 2, 99, 3, 4, 99 }, new byte[] { 5 }, new byte[] { 6 }, 2, 2, 3);
                textures.Upload(frame);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, textures.Y.GetRawTextureData());
                textures.Upload(XmaxVideoFrame.CreateI420(new byte[16], new byte[4], new byte[4], 4, 4));
                Assert.AreEqual(4, textures.Y.width); Assert.AreEqual(2, textures.U.width);
            }
        }
        [Test] public void ResetRenderIgnoresFramesFromStoppedGeneration()
        {
            IRenderController render = new RenderController(); var track = new RealtimeVideoTrack("test"); var count = 0;
            track.FrameReceived += frame => count++;
            render.Bind(track); render.BeginGeneration(); render.SetRemoteStream(FakeRtc.Bot); render.Receive(AsyncTest.Frame());
            render.ResetGeneration(); render.Receive(AsyncTest.Frame());
            Assert.AreEqual(1, count); Assert.IsNull(render.LatestFrame);
        }
    }
}
