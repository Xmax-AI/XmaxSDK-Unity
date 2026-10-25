using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Xmax.SDK.Tests
{
    internal static class AsyncTest
    {
        internal static IEnumerator Run(Func<Task> body)
        {
            var task = body();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (watch.Elapsed.TotalSeconds > 15) Assert.Fail("Async SDK test timed out.");
                yield return null;
            }
            task.GetAwaiter().GetResult();
        }
        internal static async Task Throws<T>(Task task) where T : Exception
        {
            try { await task; }
            catch (T) { return; }
            Assert.Fail("Expected " + typeof(T).Name);
        }
        internal static XmaxVideoFrame Frame() => XmaxVideoFrame.CreateI420(new byte[4], new byte[1], new byte[1], 2, 2);
        internal static TaskCompletionSource<T> Pending<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    internal sealed class FakeSessions : IRealtimeSessionService
    {
        internal Func<CancellationToken, Task<XmaxSession>> Create;
        internal Func<CancellationToken, Task<XmaxSession>> Heartbeat;
        internal Func<Task> Close;
        internal int Created;
        internal string LastModel;
        internal readonly List<string> Closed = new List<string>();
        internal static XmaxSession Session(string id = "session") => new XmaxSession
        {
            SessionUid = id, Status = "ACTIVE", JoinInfo = new RoomJoinConfiguration { AppId = RtcEngineManager.DefaultAppId, RoomId = "room", UserId = "user", Token = "token", BotName = "bot" }
        };
        public Task<XmaxSession> CreateSessionAsync(string model, CancellationToken token)
        {
            Created++;
            LastModel = model;

            return Create?.Invoke(token) ?? Task.FromResult(Session());
        }
        public Task<XmaxSession> HeartbeatSessionAsync(string id, CancellationToken token)
            => Heartbeat?.Invoke(token) ?? Task.FromResult(Session(id));
        public Task CloseSessionAsync(string id, CancellationToken token)
        { Closed.Add(id); return Close?.Invoke() ?? Task.CompletedTask; }
    }
    internal sealed class FakeStream : IStreamController
    {
        public IQualityController QualityController { get; } = new QualityController();
        public event Action<XmaxException> FatalError;
        public event Action<RealtimeNetworkQuality> NetworkQualityChanged;
        public event Action<RealtimeNetworkQuality, double> NetworkQualitySampled;
        public event Action<XmaxVideoFrame> FrameReceived;
        public event Action<RemoteStream?> RemoteStreamChanged;
        internal Func<CancellationToken, Task> Join;
        internal Func<CancellationToken, Task<string>> Generate;
        internal bool RejectPlatform;
        internal int Joins, Leaves, Starts, Stops, Updates, Pushes, Tracks;
        internal string LastTaskId;
        internal bool RejectUpdate;
        internal RealtimeVideoFormat ConnectedFormat;
        internal RealtimeVideoFormat Baseline, GenerationFormat, TargetFormat;
        internal Func<CancellationToken, Task> ChangeTarget;
        internal bool RejectEncoding;
        public void ValidatePlatform() { if (RejectPlatform) throw new XmaxException(XmaxErrorCode.NotSupported, "Test unsupported platform."); }
        public Task ConnectAsync(RoomJoinConfiguration info, RealtimeVideoFormat format, CancellationToken token)
        {
            Joins++;
            ConnectedFormat = format;

            return Join?.Invoke(token) ?? Task.CompletedTask;
        }
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public Task ReleaseAsync() => Task.CompletedTask;
        public Task DisconnectAsync() { Leaves++; return Task.CompletedTask; }
        public Task StartGenerationAsync(string taskId, RealtimeContext context, RealtimeVideoFormat format, CancellationToken token, bool faceRestoration = false)
        {
            LastTaskId = taskId;
            Starts++;
            RemoteStreamChanged?.Invoke(FakeRtc.Bot);
            if (Generate != null) return Generate(token);
            FrameReceived?.Invoke(AsyncTest.Frame());
            return Task.CompletedTask;
        }
        public Task ChangeGenerationConditionAsync(RealtimeContext context, RealtimeVideoFormat format, string taskId, CancellationToken token, bool faceRestoration = false)
        { if (RejectUpdate) throw new XmaxException(XmaxErrorCode.RtcError, "Update failed."); Updates++; return Task.CompletedTask; }
        public void ConfigureMedia(RealtimeVideoFormat baseline, RealtimeVideoFormat generation, RealtimeVideoFormat target) { Baseline = baseline; GenerationFormat = generation; TargetFormat = target; }
        public void ConfigureVideoEncoding(RealtimeVideoFormat format) { if (RejectEncoding) throw new XmaxException(XmaxErrorCode.RtcError, "Encoder rejected."); ConnectedFormat = format; }
        public Task ChangeTargetSizeAsync(string taskId, RealtimeVideoFormat format, CancellationToken token)
        { token.ThrowIfCancellationRequested(); TargetFormat = format; return ChangeTarget?.Invoke(token) ?? Task.CompletedTask; }
        public Task StopGenerationAsync() { Stops++; RemoteStreamChanged?.Invoke(null); return Task.CompletedTask; }
        public void PushFrame(XmaxVideoFrame frame) { Pushes++; }
        public void SendTracks(IReadOnlyList<RealtimePoint> tracks, string taskId) { Tracks++; }
        internal void Fail() => FatalError?.Invoke(new XmaxException(XmaxErrorCode.RtcError, "Test RTC failure."));
        internal void Frame() => FrameReceived?.Invoke(AsyncTest.Frame());
        internal void Quality()
        {
            var sample = new RealtimeNetworkQuality(0.01, 20, 100000);
            NetworkQualitySampled?.Invoke(sample, global::Xmax.SDK.QualityController.MonotonicTime());
            NetworkQualityChanged?.Invoke(sample);
        }
    }
    internal sealed class FakeRtc : IRtcManager
    {
        public event Action<XmaxException> FatalError;
        public event Action<RtcNetworkQuality> NetworkQualityChanged;
        public event Action<UplinkVideoStatistics> UplinkVideoStatisticsChanged;
        public event Action<DownlinkVideoStatistics> DownlinkVideoStatisticsChanged;
        public event Action<RtcPerformanceAlarm> PerformanceAlarm;
        public event Action<RemoteStream> VideoPublished;
        public event Action<RemoteStream> VideoUnpublished;

        public event Action<RemoteStream, XmaxVideoFrame> FrameReceived;
        internal readonly List<string> Messages = new List<string>();
        internal bool RejectMessages;
        internal Func<string, CancellationToken, Task> Send;
        internal int Subscriptions;
        internal VideoEncodingConfiguration? EncodingConfiguration;
        internal static RemoteStream Bot => new RemoteStream("room", "bot");
        public void ValidatePlatform() { }

        public Task JoinAsync(RoomJoinConfiguration info, VideoEncodingConfiguration configuration, CancellationToken token)
        {
            EncodingConfiguration = configuration;

            return Task.CompletedTask;
        }

        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public Task DestroyAsync() => Task.CompletedTask;
        public Task LeaveAsync() => Task.CompletedTask;
        public void PublishLocalVideo() { }
        public void SubscribeVideo(RemoteStream key) { Subscriptions++; }
        public void PushFrame(XmaxVideoFrame frame) { }
        public Task SendRoomMessageAsync(string message, CancellationToken cancellationToken = default,
            string replacingPendingMessage = null, bool reportFailure = true)
        { cancellationToken.ThrowIfCancellationRequested(); if (RejectMessages) throw new XmaxException(XmaxErrorCode.RtcError, "Send failed."); Messages.Add(message); return Send?.Invoke(message, cancellationToken) ?? Task.CompletedTask; }
        public void DiscardPendingTracks() { }
        public void ConfigureVideoEncoding(VideoEncodingConfiguration configuration) { EncodingConfiguration = configuration; }
        internal void Frame(RemoteStream? key = null) => FrameReceived?.Invoke(key ?? Bot, AsyncTest.Frame());
        internal void Publish(RemoteStream? key = null) => VideoPublished?.Invoke(key ?? Bot);
        internal void Unpublish() => VideoUnpublished?.Invoke(Bot);
        internal void Fail() => FatalError?.Invoke(new XmaxException(XmaxErrorCode.RtcError, "RTC failed."));
        internal void Quality(RtcNetworkQuality? quality = null) => NetworkQualityChanged?.Invoke(quality ??
            new RtcNetworkQuality(RtcQualityLevel.Good, RtcQualityLevel.Poor, 0.02, 30, 100000));
    }
}
