using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Xmax.SDK.Tests
{
    public sealed class RealtimeLifecycleTests
    {
        private FakeSessions _sessions;
        private FakeStream _stream;
        private XmaxRealtimeManager _manager;
        private static RealtimeVideoFormat Format => new RealtimeVideoFormat(1280, 720, 30);
        [SetUp] public void SetUp()
        {
            XmaxLogger.Configure(XmaxLoggerOption.None);
            _sessions = new FakeSessions(); _stream = new FakeStream();
            _manager = new XmaxRealtimeManager(new XmaxConfiguration("test"), new RealtimeConfiguration(RealtimeModel.X2_1_1_Preview_CN, false), _sessions, _stream);
        }
        [UnityTearDown] public IEnumerator TearDown() => AsyncTest.Run(() => _manager.CloseAsync());

        [UnityTest] public IEnumerator LocalCameraPreviewDoesNotRequireApiKey() => AsyncTest.Run(async () =>
        {
            var manager = new XmaxClient(new XmaxConfiguration("")).CreateRealtimeManager(
                new RealtimeConfiguration(RealtimeModel.X2_1_1_Preview_CN, false));
            try
            {
                var local = manager.CreateLocalExternalStream(Format);
                var frames = 0;
                local.VideoTrack.FrameReceived += frame => frames++;
                local.PushVideoFrame(AsyncTest.Frame());
                Assert.AreEqual(1, frames);
                await AsyncTest.Throws<XmaxException>(manager.ConnectAsync(local));
                Assert.AreSame(local, manager.LocalStream);
            }
            finally { await manager.CloseAsync(); }
        });
        [UnityTest] public IEnumerator CombinedStartValidatesKeyBeforeCreatingSession() => AsyncTest.Run(async () =>
        {
            var manager = new XmaxRealtimeManager(new XmaxConfiguration(""), _manager.Options, _sessions, _stream);
            try
            {
                var local = manager.CreateLocalExternalStream(Format);
                await AsyncTest.Throws<XmaxException>(manager.StartGenerationAsync(local, new RealtimeContext("test")));
                Assert.AreEqual(0, _sessions.Created);
                Assert.AreEqual(0, _stream.Joins);
                Assert.AreSame(local, manager.LocalStream);
            }
            finally { await manager.CloseAsync(); }
        });
        [UnityTest] public IEnumerator CombinedStartConnectsOnceAndReturnsSameRemoteStream() => AsyncTest.Run(async () =>
        {
            var local = _manager.CreateLocalExternalStream(Format);
            var remote = await _manager.StartGenerationAsync(local, new RealtimeContext("first"));
            var taskId = _manager.CurrentState.TaskId;
            Assert.AreEqual(RealtimeStreamId.Remote, remote.Id);
            Assert.AreEqual(RealtimeConnectionState.Generating, _manager.CurrentState.ConnectionState);
            Assert.AreSame(remote, await _manager.StartGenerationAsync(local, null));
            Assert.AreSame(remote, await _manager.StartGenerationAsync(local, new RealtimeContext("update")));
            Assert.AreEqual(taskId, _manager.CurrentState.TaskId);
            Assert.AreEqual(1, _sessions.Created);
            Assert.AreEqual(1, _stream.Starts);
            Assert.AreEqual(2, _stream.Updates);
        });
        [UnityTest] public IEnumerator CombinedStartRejectsForeignAndReplacedSources() => AsyncTest.Run(async () =>
        {
            var old = _manager.CreateLocalExternalStream(Format);
            var current = _manager.CreateLocalExternalStream(Format);
            await AsyncTest.Throws<XmaxException>(_manager.StartGenerationAsync(old, new RealtimeContext("test")));
            var other = new XmaxRealtimeManager("test");
            try
            {
                var foreign = other.CreateLocalExternalStream(Format);
                await AsyncTest.Throws<XmaxException>(_manager.StartGenerationAsync(foreign, new RealtimeContext("test")));
                Assert.AreEqual(0, _sessions.Created);
                await _manager.StartGenerationAsync(current, new RealtimeContext("test"));
                await AsyncTest.Throws<XmaxException>(_manager.StartGenerationAsync(foreign, null));
                Assert.AreEqual(RealtimeConnectionState.Generating, _manager.CurrentState.ConnectionState);
            }
            finally { await other.CloseAsync(); }
        });
        [UnityTest] public IEnumerator NewGenerationReplacesPendingRequestAfterCleanup() => AsyncTest.Run(async () =>
        {
            var local = _manager.CreateLocalExternalStream(Format);
            _stream.Generate = async token => { await Task.Delay(Timeout.Infinite, token); return "unused"; };
            var first = _manager.StartGenerationAsync(local, new RealtimeContext("first"));
            _stream.Generate = null;
            var second = _manager.StartGenerationAsync(local, new RealtimeContext("second"));
            await AsyncTest.Throws<OperationCanceledException>(first);
            await second;
            Assert.AreEqual(RealtimeConnectionState.Generating, _manager.CurrentState.ConnectionState);
            Assert.AreEqual(1, _sessions.Created);
        });
        [UnityTest] public IEnumerator CombinedStartGenerationFailureReleasesConnectionBeforeRetry() => AsyncTest.Run(async () =>
        {
            var local = _manager.CreateLocalExternalStream(Format);
            _stream.Generate = token => Task.FromException<string>(new XmaxException(XmaxErrorCode.Timeout, "test timeout"));
            await AsyncTest.Throws<XmaxException>(_manager.StartGenerationAsync(local, new RealtimeContext("first")));
            Assert.AreEqual(RealtimeConnectionState.Ready, _manager.CurrentState.ConnectionState);
            Assert.AreEqual(1, _sessions.Closed.Count);
            _stream.Generate = null;
            await _manager.StartGenerationAsync(local, new RealtimeContext("retry"));
            Assert.AreEqual(2, _sessions.Created);
        });
        [UnityTest] public IEnumerator CombinedStartCanBeDisconnectedWhileSessionCreationIsPending() => AsyncTest.Run(async () =>
        {
            var local = _manager.CreateLocalExternalStream(Format);
            var pending = AsyncTest.Pending<XmaxSession>();
            _sessions.Create = token => pending.Task;
            var start = _manager.StartGenerationAsync(local, new RealtimeContext("test"));
            var disconnect = _manager.DisconnectAsync();
            pending.SetResult(FakeSessions.Session("late"));
            await AsyncTest.Throws<OperationCanceledException>(start);
            await disconnect;
            Assert.AreEqual(RealtimeConnectionState.Ready, _manager.CurrentState.ConnectionState);
            Assert.AreEqual(0, _stream.Starts);
            CollectionAssert.AreEqual(new[] { "late" }, _sessions.Closed);
            Assert.AreSame(local, _manager.LocalStream);
        });
        [UnityTest] public IEnumerator CombinedStartDisconnectAtConnectedNeverStartsGeneration() => AsyncTest.Run(async () =>
        {
            var local = _manager.CreateLocalExternalStream(Format);
            Task disconnect = null;
            _manager.StateChanged += state =>
            {
                if (state.ConnectionState == RealtimeConnectionState.Connected) disconnect = _manager.DisconnectAsync();
            };
            await AsyncTest.Throws<OperationCanceledException>(_manager.StartGenerationAsync(local, new RealtimeContext("test")));
            await disconnect;
            Assert.AreEqual(0, _stream.Starts);
            Assert.AreEqual(1, _sessions.Closed.Count);
        });

        [UnityTest] public IEnumerator LateSessionIsClosedWithoutJoining() => AsyncTest.Run(async () =>
        {
            var pending = AsyncTest.Pending<XmaxSession>();
            _sessions.Create = token => pending.Task; // Deliberately ignores cancellation.
            var connect = _manager.ConnectAsync(Format);
            var disconnect = _manager.DisconnectAsync();
            Assert.False(disconnect.IsCompleted);
            pending.SetResult(FakeSessions.Session("late"));
            await AsyncTest.Throws<OperationCanceledException>(connect);
            await disconnect;
            Assert.AreEqual(0, _stream.Joins);
            CollectionAssert.AreEqual(new[] { "late" }, _sessions.Closed);
            Assert.AreEqual(RealtimeConnectionState.Ready, _manager.CurrentState.ConnectionState);
        });
        [UnityTest] public IEnumerator CleanupBlocksReconnectAndCoalescesDisconnects() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            var pending = AsyncTest.Pending<bool>();
            _sessions.Close = () => pending.Task;
            var first = _manager.DisconnectAsync();
            var second = _manager.DisconnectAsync();
            Assert.AreSame(first, second);
            Assert.AreEqual(RealtimeConnectionState.Disconnecting, _manager.CurrentState.ConnectionState);
            await AsyncTest.Throws<XmaxException>(_manager.ConnectAsync(Format));
            pending.SetResult(true);
            await first;
            _sessions.Close = null;
            await _manager.ConnectAsync(Format);
            Assert.AreEqual(RealtimeConnectionState.Connected, _manager.CurrentState.ConnectionState);
            Assert.AreEqual(1, _sessions.Closed.Count);
        });
        [UnityTest] public IEnumerator DisconnectCancelsPendingGenerationAndAllowsReconnect() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            _stream.Generate = async token => { await Task.Delay(Timeout.Infinite, token); return "never"; };
            var start = _manager.StartGenerationAsync("first");
            var disconnect = _manager.DisconnectAsync();
            await AsyncTest.Throws<OperationCanceledException>(start);
            await disconnect;
            Assert.AreEqual(RealtimeConnectionState.Ready, _manager.CurrentState.ConnectionState);
            Assert.IsNull(_manager.CurrentState.TaskId);
            Assert.AreEqual(1, _sessions.Closed.Count);
            _stream.Generate = null;
            await _manager.ConnectAsync(Format);
            await _manager.StartGenerationAsync("second");
            Assert.AreEqual(RealtimeConnectionState.Generating, _manager.CurrentState.ConnectionState);
        });
        [UnityTest] public IEnumerator GenerationNeedsAnActualRemoteFrame() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            _stream.Generate = token => Task.FromResult("confirmed");
            var start = _manager.StartGenerationAsync("first");
            await Task.Yield();
            Assert.False(start.IsCompleted);
            Assert.AreEqual(RealtimeConnectionState.Connected, _manager.CurrentState.ConnectionState);
            _stream.Frame();
            await start;
            Assert.AreEqual(_stream.LastTaskId, _manager.CurrentState.TaskId);
        });
        [UnityTest] public IEnumerator NilContextReusesTaskAndExplicitContextUpdatesIt() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            await _manager.StartGenerationAsync("first");
            var taskId = _manager.CurrentState.TaskId;
            await _manager.StartGenerationAsync();
            await _manager.StartGenerationAsync("second");
            Assert.AreEqual(taskId, _manager.CurrentState.TaskId);
            Assert.AreEqual(1, _stream.Starts);
            Assert.AreEqual(2, _stream.Updates);
            Assert.AreEqual(0, _stream.Stops);
        });
        [UnityTest] public IEnumerator FailedContextUpdateKeepsGenerationRunning() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            await _manager.StartGenerationAsync("first");
            var id = _manager.CurrentState.TaskId;
            _stream.RejectUpdate = true;
            await AsyncTest.Throws<XmaxException>(_manager.StartGenerationAsync("second"));
            Assert.AreEqual(id, _manager.CurrentState.TaskId);
            Assert.AreEqual(RealtimeConnectionState.Generating, _manager.CurrentState.ConnectionState);
        });
        [UnityTest] public IEnumerator ReentrantDisconnectFromConnectingDoesNotCreateSession() => AsyncTest.Run(async () =>
        {
            Task disconnect = null;
            _manager.StateChanged += state => { if (state.ConnectionState == RealtimeConnectionState.Connecting) disconnect = _manager.DisconnectAsync(); };
            await AsyncTest.Throws<OperationCanceledException>(_manager.ConnectAsync(Format));
            await disconnect;
            Assert.AreEqual(0, _sessions.Created);
        });
        [UnityTest] public IEnumerator ConnectedNotificationRunsAfterOperationRelease() => AsyncTest.Run(async () =>
        {
            Task disconnect = null;
            _manager.StateChanged += state => { if (state.ConnectionState == RealtimeConnectionState.Connected) disconnect = _manager.DisconnectAsync(); };
            await _manager.ConnectAsync(Format);
            await disconnect;
            Assert.AreEqual(1, _sessions.Closed.Count);
        });
        [UnityTest] public IEnumerator ListenerExceptionsDoNotBreakConnection() => AsyncTest.Run(async () =>
        {
            Action<RealtimeState> listener = state => { throw new InvalidOperationException("listener failure"); };
            _manager.StateChanged += listener;
            await _manager.ConnectAsync(Format);
            _manager.StateChanged -= listener;
            Assert.AreEqual(RealtimeConnectionState.Connected, _manager.CurrentState.ConnectionState);
        });
        [UnityTest] public IEnumerator TerminalListenerCanUpgradeDisconnectToClose() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            var source = _manager.LocalStream;
            Task close = null;
            _manager.StateChanged += state =>
            {
                if (state.ConnectionState == RealtimeConnectionState.Ready) close = _manager.CloseAsync();
            };
            await _manager.DisconnectAsync();
            await close;
            Assert.IsNull(_manager.LocalStream);
            Assert.AreEqual(RealtimeConnectionState.Idle, _manager.CurrentState.ConnectionState);
            Assert.Throws<XmaxException>(() => source.PushVideoFrame(AsyncTest.Frame()));
        });
        [UnityTest] public IEnumerator ReentrantStateChangeDoesNotDeliverObsoleteStateToNextListener() => AsyncTest.Run(async () =>
        {
            Task disconnect = null;
            var observed = new List<RealtimeConnectionState>();
            _manager.StateChanged += state =>
            {
                if (state.ConnectionState == RealtimeConnectionState.Connecting) disconnect = _manager.DisconnectAsync();
            };
            _manager.StateChanged += state => observed.Add(state.ConnectionState);
            await AsyncTest.Throws<OperationCanceledException>(_manager.ConnectAsync(Format));
            await disconnect;
            CollectionAssert.DoesNotContain(observed, RealtimeConnectionState.Connecting);
            Assert.AreEqual(RealtimeConnectionState.Ready, observed[observed.Count - 1]);
        });
        [UnityTest] public IEnumerator GeneratingNotificationRunsAfterStartupCompletion() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            using (var cancellation = new CancellationTokenSource())
            {
                _manager.StateChanged += state => { if (state.ConnectionState == RealtimeConnectionState.Generating) cancellation.Cancel(); };
                await _manager.StartGenerationAsync(new RealtimeContext("test"), cancellation.Token);
                Assert.AreEqual(RealtimeConnectionState.Generating, _manager.CurrentState.ConnectionState);
                Assert.NotNull(_manager.CurrentState.TaskId);
            }
        });
        [UnityTest] public IEnumerator FatalErrorIsDeliveredAfterCleanup() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            var pending = AsyncTest.Pending<bool>();
            _sessions.Close = () => pending.Task;
            XmaxException failure = null;
            _manager.ErrorOccurred += error => failure = error;
            _stream.Fail();
            Assert.IsNull(failure);
            var cleanup = _manager.DisconnectAsync();
            pending.SetResult(true);
            await cleanup;
            Assert.AreEqual(XmaxErrorSeverity.Fatal, failure.Severity);
            Assert.AreEqual(RealtimeConnectionState.Ready, _manager.CurrentState.ConnectionState);
        });
        [UnityTest] public IEnumerator FatalErrorDuringJoinCancelsAndClosesSession() => AsyncTest.Run(async () =>
        {
            _stream.Join = token => Task.Delay(Timeout.Infinite, token);
            var connect = _manager.ConnectAsync(Format);
            _stream.Fail();
            await AsyncTest.Throws<XmaxException>(connect);
            await _manager.DisconnectAsync();
            Assert.AreEqual(1, _sessions.Closed.Count);
        });
        [UnityTest] public IEnumerator CloseUpgradesPendingDisconnectAndInvalidatesLocalSource() => AsyncTest.Run(async () =>
        {
            var source = _manager.CreateLocalExternalStream(Format);
            await _manager.ConnectAsync(source);
            var pending = AsyncTest.Pending<bool>();
            _sessions.Close = () => pending.Task;
            var disconnect = _manager.DisconnectAsync();
            var close = _manager.CloseAsync();
            Assert.AreSame(disconnect, close);
            pending.SetResult(true);
            await close;
            Assert.IsNull(_manager.LocalStream);
            Assert.Throws<XmaxException>(() => source.PushVideoFrame(AsyncTest.Frame()));
            Assert.AreEqual(1, _sessions.Closed.Count);
        });
        [UnityTest] public IEnumerator DisconnectKeepsLocalPreviewAndCloseReleasesIt() => AsyncTest.Run(async () =>
        {
            var source = _manager.CreateLocalExternalStream(Format);
            var previewFrames = 0;
            source.VideoTrack.FrameReceived += frame => previewFrames++;
            source.PushVideoFrame(AsyncTest.Frame());
            Assert.AreEqual(0, _stream.Pushes);
            await _manager.ConnectAsync(source);
            source.PushVideoFrame(AsyncTest.Frame());
            await _manager.DisconnectAsync();
            source.PushVideoFrame(AsyncTest.Frame());
            Assert.AreEqual(3, previewFrames);
            Assert.AreEqual(1, _stream.Pushes);
            Assert.AreSame(source, _manager.LocalStream);
        });
        [UnityTest] public IEnumerator UnsupportedPlatformDoesNotCreateSession() => AsyncTest.Run(async () =>
        {
            _stream.RejectPlatform = true;
            await AsyncTest.Throws<XmaxException>(_manager.ConnectAsync(Format));
            Assert.AreEqual(0, _sessions.Created);
        });
        [UnityTest] public IEnumerator CleanupFailureIsRecoverableAndDoesNotBlockReconnect() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            _sessions.Close = () => Task.FromException(new XmaxException(XmaxErrorCode.NetworkError, "offline"));
            XmaxException warning = null;
            _manager.CleanupWarning += error => warning = error;
            await _manager.DisconnectAsync();
            Assert.AreEqual(XmaxErrorSeverity.Recoverable, warning.Severity);
            Assert.AreEqual(RealtimeConnectionState.Ready, _manager.CurrentState.ConnectionState);
            await _manager.ConnectAsync(Format);
        });
        [UnityTest] public IEnumerator TracksRequireGenerationAndNormalizedBounds() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            Assert.Throws<XmaxException>(() => _manager.SendTracks(new[] { new Vector2(1, 1) }));
            await _manager.StartGenerationAsync("first");
            Assert.Throws<XmaxException>(() => _manager.SendTracks(new[] { new Vector2(1.01f, 0) }));
            _manager.SendTracks(new[] { new Vector2(1, 1) });
            Assert.AreEqual(1, _stream.Tracks);
        });
        [UnityTest] public IEnumerator FrameCallbackCanDisconnectWithoutForwardingStaleManagerFrame() => AsyncTest.Run(async () =>
        {
            var remote = await _manager.ConnectAsync(Format);
            await _manager.StartGenerationAsync("first");
            var count = 0;
            Task disconnect = null;
            _manager.RemoteFrameReceived += frame => count++;
            remote.VideoTrack.FrameReceived += frame => disconnect = _manager.DisconnectAsync();
            _stream.Frame();
            await disconnect;
            Assert.AreEqual(0, count);
        });
        [UnityTest] public IEnumerator DisconnectReportsNormalReasonAndReconnectClearsIt() => AsyncTest.Run(async () =>
        {
            Assert.IsNull(_manager.CurrentState.Reason);
            await _manager.ConnectAsync(Format);
            Assert.AreEqual(RealtimeConnectionState.Connected, _manager.CurrentState.ConnectionState);
            Assert.IsNull(_manager.CurrentState.Reason);
            await _manager.DisconnectAsync();
            Assert.AreEqual(RealtimeConnectionState.Ready, _manager.CurrentState.ConnectionState);
            Assert.AreEqual(RealtimeReasonKind.Normal, _manager.CurrentState.Reason.Kind);
            Assert.IsNull(_manager.CurrentState.Reason.Error);
            await _manager.ConnectAsync(Format);
            Assert.AreEqual(RealtimeConnectionState.Connected, _manager.CurrentState.ConnectionState);
            Assert.IsNull(_manager.CurrentState.Reason);
        });
        [UnityTest] public IEnumerator CancelledConnectReportsNormalReason() => AsyncTest.Run(async () =>
        {
            Task disconnect = null;
            _manager.StateChanged += state => { if (state.ConnectionState == RealtimeConnectionState.Connecting) disconnect = _manager.DisconnectAsync(); };
            await AsyncTest.Throws<OperationCanceledException>(_manager.ConnectAsync(Format));
            await disconnect;
            Assert.AreEqual(RealtimeConnectionState.Ready, _manager.CurrentState.ConnectionState);
            Assert.AreEqual(RealtimeReasonKind.Normal, _manager.CurrentState.Reason.Kind);
        });
        [UnityTest] public IEnumerator ConnectFailureReportsFailureReasonWithThrownError() => AsyncTest.Run(async () =>
        {
            _sessions.Create = token => Task.FromException<XmaxSession>(new XmaxException(XmaxErrorCode.NetworkError, "Session creation failed."));
            XmaxException thrown = null;
            try { await _manager.ConnectAsync(Format); }
            catch (XmaxException exception) { thrown = exception; }
            Assert.NotNull(thrown);
            Assert.AreEqual(RealtimeConnectionState.Ready, _manager.CurrentState.ConnectionState);
            Assert.AreEqual(RealtimeReasonKind.Failure, _manager.CurrentState.Reason.Kind);
            Assert.AreSame(thrown, _manager.CurrentState.Reason.Error);
        });
        [UnityTest] public IEnumerator FatalErrorTerminationReportsFailureReason() => AsyncTest.Run(async () =>
        {
            await _manager.ConnectAsync(Format);
            XmaxException reported = null;
            _manager.ErrorOccurred += error => reported = error;
            _stream.Fail();
            await _manager.DisconnectAsync();
            Assert.AreEqual(RealtimeConnectionState.Ready, _manager.CurrentState.ConnectionState);
            Assert.AreEqual(RealtimeReasonKind.Failure, _manager.CurrentState.Reason.Kind);
            Assert.AreEqual(XmaxErrorSeverity.Fatal, _manager.CurrentState.Reason.Error.Severity);
            Assert.AreSame(reported, _manager.CurrentState.Reason.Error);
        });
        [UnityTest] public IEnumerator FailureReasonRejectsNullError() => AsyncTest.Run(() =>
        {
            Assert.Throws<ArgumentNullException>(() => RealtimeReason.Failure(null));
            Assert.AreSame(RealtimeReason.Normal, RealtimeReason.Normal);
            Assert.AreEqual(RealtimeReason.Normal, RealtimeReason.Normal);
            return Task.CompletedTask;
        });
    }
}
