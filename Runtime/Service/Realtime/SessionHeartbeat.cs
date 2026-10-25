//
//  SessionHeartbeat.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>Renews the server session and waits for its heartbeat loop to stop before another connection can reuse it.</summary>
    internal sealed class SessionHeartbeat
    {

        /// <summary>The service renewing the current server session.</summary>
        private readonly IRealtimeSessionService _sessions;

        /// <summary>The cancellable heartbeat interval, replaceable by the caller.</summary>
        private readonly Func<CancellationToken, Task> _delay;

        /// <summary>The cancellation source owning the current heartbeat lifetime.</summary>
        private CancellationTokenSource _cancellation;

        /// <summary>The observed heartbeat loop, completed when inactive.</summary>
        private Task _loop = Task.CompletedTask;

        /// <summary>Occurs when an uncanceled heartbeat fails or the session is no longer active.</summary>
        internal event Action<XmaxException> Failed;

        /// <summary>Creates a server heartbeat with a default ten-second interval.</summary>
        internal SessionHeartbeat(
            IRealtimeSessionService sessions,
            Func<CancellationToken, Task> delay = null
        )
        {
            _sessions = sessions;
            _delay = delay ?? (token => Task.Delay(
                10000,
                token
            ));
        }

        /// <summary>Starts renewing a session after the preceding heartbeat has stopped.</summary>
        internal void Start(
            string sessionId
        )
        {
            if (_cancellation != null)
                throw new InvalidOperationException("Stop the previous heartbeat first.");

            _cancellation = new CancellationTokenSource();
            _loop = RunAsync(
                sessionId,
                _cancellation.Token
            );
        }

        /// <summary>Cancels heartbeat and waits for the current loop before releasing its cancellation source.</summary>
        internal async Task StopAsync()
        {
            var cancellation = _cancellation;
            if (cancellation == null)
                return;

            cancellation.Cancel();
            await _loop;
            cancellation.Dispose();
            if (ReferenceEquals(
                _cancellation,
                cancellation
            ))
                _cancellation = null;
        }

        /// <summary>Renews the session and rejects inactive responses while ignoring responses received after cancellation.</summary>
        private async Task RunAsync(
            string sessionId,
            CancellationToken token
        )
        {
            await Task.Yield();

            try
            {
                while (true)
                {
                    await _delay(token);
                    token.ThrowIfCancellationRequested();
                    var session = await _sessions.HeartbeatSessionAsync(
                        sessionId,
                        token
                    );
                    token.ThrowIfCancellationRequested(); // Ignore responses belonging to a stopped heartbeat.
                    if (!string.IsNullOrEmpty(session.Status) && session.Status != "ACTIVE")
                        throw new XmaxException(
                            XmaxErrorCode.SessionError,
                            session.CloseReason ?? ("Session is no longer active: " + session.Status)
                        );
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested)
                    EventDispatch.Raise(
                        Failed,
                        XmaxErrorConverter.Wrap(
                            exception,
                            XmaxErrorSeverity.Fatal
                        )
                    );
            }
        }
    }
}
