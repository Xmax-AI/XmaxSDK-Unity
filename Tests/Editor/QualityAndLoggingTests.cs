using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Xmax.SDK.Tests
{
    public sealed class QualityAndLoggingTests
    {
        private readonly List<string> _logs = new List<string>();
        [SetUp] public void SetUp()
        {
            XmaxLogger.Configure(XmaxLoggerOption.None);
            _logs.Clear();
            Application.logMessageReceived += Capture;
        }
        [TearDown] public void TearDown()
        {
            Application.logMessageReceived -= Capture;
            XmaxLogger.Configure(XmaxLoggerOption.None);
        }
        private void Capture(string text, string stack, LogType type)
        { if (text.StartsWith("[Xmax]", StringComparison.Ordinal)) _logs.Add(text); }

        [Test] public void LoggingDefaultsOffAndDoesNotEvaluateDisabledMessages()
        {
            var evaluated = false;
            XmaxLogger.Realtime.Info(() => { evaluated = true; return "test"; });
            XmaxLogger.Realtime.Failure(new Exception("private message"));
            Assert.False(evaluated);
            Assert.IsEmpty(_logs);
            Assert.AreEqual(XmaxLoggerOption.None, new XmaxConfiguration("").LoggerOptions);
        }
        [Test] public void BusinessAndPerformanceCanBeEnabledIndependently()
        {
            XmaxLogger.Configure(XmaxLoggerOption.Business);
            XmaxLogger.Realtime.Info(() => "one\ntwo");
            XmaxLogger.Rtc.Info(() => "hidden", XmaxLoggerOption.Performance);
            Assert.AreEqual(1, _logs.Count);
            Assert.AreEqual("[Xmax][Realtime] one\n[Xmax][Realtime] two", _logs[0]);
            XmaxLogger.Configure(XmaxLoggerOption.Performance);
            XmaxLogger.Realtime.Info(() => "hidden");
            XmaxLogger.Rtc.Info(() => "visible", XmaxLoggerOption.Performance);
            Assert.AreEqual(2, _logs.Count);
        }
        [Test] public void ApiAndErrorLogsRedactCredentialsAndRetainDiagnosticDetails()
        {
            XmaxLogger.Configure(XmaxLoggerOption.All, XmaxLogger.Language.English);
            ApiLogger.Response("PUT", "/session/private-session/heartbeat?token=secret-token", 200, 123, 42);
            LogAssert.Expect(LogType.Error, new Regex("^\\[Xmax\\]\\[API\\] POST <route>"));
            ApiLogger.Failure("POST", "/private-path?apiKey=secret-key", new XmaxException(XmaxErrorCode.ApiError, "Request failed token=secret-token", 4001, 403), 50);
            LogAssert.Expect(LogType.Error, new Regex("^\\[Xmax\\]\\[Realtime\\] 操作失败"));
            XmaxLogger.Realtime.Failure(new Exception("Transport failed Authorization=Bearer secret-bearer"));
            var output = string.Join("\n", _logs);
            Assert.That(output, Does.Contain("/session/{id}/heartbeat"));
            Assert.That(output, Does.Contain("Status: 200"));
            Assert.That(output, Does.Contain("42 ms"));
            Assert.That(output, Does.Contain("123 bytes"));
            Assert.That(output, Does.Contain("ApiError, API 4001, HTTP 403"));
            Assert.That(output, Does.Contain("Transport failed"));
            foreach (var secret in new[] { "private-session", "private-path", "secret-token", "secret-key", "secret-bearer" })
                Assert.That(output, Does.Not.Contain(secret));
        }
        [Test] public void ClientConfigurationAppliesLoggerOptionsWithoutValidatingApiKey()
        {
            var client = new XmaxClient(new XmaxConfiguration("", XmaxEnvironment.Global, XmaxLoggerOption.Performance));
            Assert.True(XmaxLogger.IsEnabled(XmaxLoggerOption.Performance));
            Assert.False(XmaxLogger.IsEnabled(XmaxLoggerOption.Business));
            Assert.AreEqual(XmaxConfiguration.GlobalBaseUrl, client.Configuration.BaseUrl);
        }
        [TestCase("Debug", LogType.Log)]
        [TestCase("Info", LogType.Log)]
        [TestCase("Warn", LogType.Warning)]
        [TestCase("Error", LogType.Error)]
        public void EveryLevelSupportsLazyMessagesAndOptionFiltering(string level, LogType outputType)
        {
            Action<Func<string>, XmaxLoggerOption> log;
            switch (level)
            {
                case "Debug": log = XmaxLogger.Rtc.Debug; break;
                case "Info": log = XmaxLogger.Rtc.Info; break;
                case "Warn": log = XmaxLogger.Rtc.Warn; break;
                default: log = XmaxLogger.Rtc.Error; break;
            }
            var evaluations = 0;
            Func<string> message = () => { evaluations++; return "diagnostic"; };
            XmaxLogger.Configure(XmaxLoggerOption.Business);
            log(message, XmaxLoggerOption.Performance);
            log(message, XmaxLoggerOption.All);
            log(message, XmaxLoggerOption.None);
            Assert.AreEqual(0, evaluations);
            Assert.IsEmpty(_logs);
            XmaxLogger.Configure(XmaxLoggerOption.Performance);
            LogAssert.Expect(outputType, "[Xmax][RTC] diagnostic");
            log(message, XmaxLoggerOption.Performance);
            Assert.AreEqual(1, evaluations);
            Assert.AreEqual(1, _logs.Count);
        }
        [UnityTest] public IEnumerator CreatingManagerDoesNotReapplyAnOlderClientsLoggerOptions() => AsyncTest.Run(async () =>
        {
            var oldClient = new XmaxClient(new XmaxConfiguration("", loggerOptions: XmaxLoggerOption.Business));
            _ = new XmaxClient(new XmaxConfiguration("", loggerOptions: XmaxLoggerOption.Performance));
            var manager = oldClient.CreateRealtimeManager(new RealtimeConfiguration(RealtimeModel.X2_1_1_Preview_CN, false));
            try
            {
                Assert.True(XmaxLogger.IsEnabled(XmaxLoggerOption.Performance));
                Assert.False(XmaxLogger.IsEnabled(XmaxLoggerOption.Business));
                manager.CreateLocalExternalStream(new RealtimeVideoFormat(1280, 720, 30));
                Assert.True(XmaxLogger.IsEnabled(XmaxLoggerOption.Performance));
            }
            finally { await manager.CloseAsync(); }
        });
        [UnityTest] public IEnumerator ConvenienceConstructorAppliesOptionsThroughItsClient() => AsyncTest.Run(async () =>
        {
            var manager = new XmaxRealtimeManager("", loggerOptions: XmaxLoggerOption.Performance);
            try
            {
                Assert.True(XmaxLogger.IsEnabled(XmaxLoggerOption.Performance));
                Assert.False(XmaxLogger.IsEnabled(XmaxLoggerOption.Business));
            }
            finally { await manager.CloseAsync(); }
        });
        [Test] public void QualityControllerMapsBothDirectionsAndRetainsMetrics()
        {
            var rtc = new FakeRtc();
            var controller = new QualityController(rtc);
            RealtimeNetworkQuality result = null;
            controller.NetworkQualityChanged += quality => result = quality;
            foreach (RtcQualityLevel level in Enum.GetValues(typeof(RtcQualityLevel)))
            {
                rtc.Quality(new RtcNetworkQuality(level, RtcQualityLevel.Poor, 0.02, 30, 100000));
                Assert.AreEqual(level.ToString(), result.Uplink.ToString());
                Assert.AreEqual(RealtimeNetworkQualityLevel.Poor, result.Downlink);
                Assert.AreEqual(0.02, result.PacketLossRatio);
                Assert.AreEqual(30, result.RoundTripMilliseconds);
                Assert.AreEqual(100000, result.BandwidthBitsPerSecond);
            }
            rtc.Quality(new RtcNetworkQuality((RtcQualityLevel)999, (RtcQualityLevel)(-1), 0, 0, 0));
            Assert.AreEqual(RealtimeNetworkQualityLevel.Unknown, result.Uplink);
            Assert.AreEqual(RealtimeNetworkQualityLevel.Unknown, result.Downlink);
        }
        [UnityTest] public IEnumerator QualityEventsStopAfterDisconnect() => AsyncTest.Run(async () =>
        {
            var rtc = new FakeRtc(); var stream = new StreamController(rtc); var count = 0;
            stream.NetworkQualityChanged += quality => count++;
            rtc.Quality(); Assert.AreEqual(0, count);
            await stream.ConnectAsync(FakeSessions.Session().JoinInfo, new RealtimeVideoFormat(1280, 720, 30), CancellationToken.None);
            rtc.Quality(); Assert.AreEqual(1, count);
            await stream.DisconnectAsync();
            rtc.Quality(); Assert.AreEqual(1, count);
        });
        [Test] public void StartupTimingUsesFourStagesAndRejectsStaleTasks()
        {
            var now = 0.0; var timing = new RealtimeTiming(() => now);
            XmaxLogger.Configure(XmaxLoggerOption.Performance, XmaxLogger.Language.English);
            timing.Begin(); now = 10; timing.BeginSessionCreation(); now = 30; timing.FinishSessionCreation();
            now = 35; timing.BeginRoomJoin(); now = 45; timing.FinishRoomJoin();
            now = 46; timing.BeginLocalPublication(); now = 49; timing.FinishLocalPublication();
            now = 50; timing.BeginSignal("current");
            timing.FinishFirstFrame("old"); timing.Finish("old");
            Assert.IsEmpty(_logs);
            now = 110; timing.FinishFirstFrame("current"); now = 120; timing.Finish("current");
            var success = _logs[0];
            Assert.That(success, Does.Contain("[Xmax][Realtime]"));
            Assert.That(success, Does.Contain("Total Duration: 120.0 ms"));
            Assert.That(success, Does.Contain("Session Creation: 20.0 ms"));
            Assert.That(success, Does.Contain("RTC Room Join: 10.0 ms"));
            Assert.That(success, Does.Contain("Local Stream Publication: 3.0 ms"));
            Assert.That(success, Does.Contain("First Frame Arrival: 60.0 ms"));
            Assert.That(success, Does.Not.Contain("Confirmation"));
            timing.Finish("current"); Assert.AreEqual(1, _logs.Count);
            timing.Begin(); now = 150;
            timing.FinishFailure(new XmaxException(XmaxErrorCode.Timeout, "Startup timed out."));
            Assert.That(_logs[1], Does.Contain("Elapsed Time: 30.0 ms"));
            Assert.That(_logs[1], Does.Contain("Current Stage: Invocation and Local Preparation"));
            Assert.That(_logs[1], Does.Contain("Session Creation: —"));
            timing.Begin(); timing.FinishFailure(new OperationCanceledException());
            Assert.AreEqual(2, _logs.Count);
        }
        [UnityTest] public IEnumerator CombinedStartupProducesRealStageTiming() => AsyncTest.Run(async () =>
        {
            var timing = new RealtimeTiming(); var rtc = new FakeRtc();
            var stream = new StreamController(rtc, timing: timing);
            var client = new XmaxClient(new XmaxConfiguration("test", XmaxEnvironment.Global, XmaxLoggerOption.Performance));
            var manager = new XmaxRealtimeManager(client.Configuration,
                new RealtimeConfiguration(RealtimeModel.X2_1_1_Preview_CN, false), new FakeSessions(), stream, timing);
            try
            {
                var local = manager.CreateLocalExternalStream(new RealtimeVideoFormat(1280, 720, 30));
                var start = manager.StartGenerationAsync(local, new RealtimeContext("secret prompt"));
                rtc.Publish(); rtc.Frame(); await start;
                var output = string.Join("\n", _logs);
                foreach (var stage in new[] { "Session Creation:", "RTC Room Join:", "Local Stream Publication:", "First Frame Arrival:" })
                    Assert.That(output, Does.Contain(stage));
                Assert.That(output, Does.Not.Contain("secret prompt"));
                Assert.That(output, Does.Contain("[Xmax][Realtime]"));
            }
            finally { await manager.CloseAsync(); }
        });
    }
}
