//
//  RtcMessageQueue.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>
    /// Encodes UTF-8 room signaling using the server's Agora fragmentation protocol.
    /// </summary>
    internal static class RtcMessageCodec
    {

        /// <summary>
        /// Returns ordered packets, each no larger than 800 bytes.
        /// </summary>
        internal static IReadOnlyList<byte[]> Encode(
            string message
        )
        {
            var bytes = Encoding.UTF8.GetBytes(message ?? "");
            if (bytes.Length == 0 || bytes.Length > 262144)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "RTC messages must contain 1 to 262144 UTF-8 bytes."
                );
            if (bytes.Length <= 800)
                return new[] { bytes };
            var chunks = new List<string>();
            var chunk = new StringBuilder();
            var length = 0;
            for (var i = 0; i < message.Length; i++)
            {
                var scalar = char.IsHighSurrogate(message[i]) && i + 1 < message.Length && char.IsLowSurrogate(message[i + 1])
                    ? message.Substring(
                        i++,
                        2
                    ) : message[i].ToString();
                var encoded = Encoding.UTF8.GetByteCount(JsonCodec.Encode(new[] { scalar })) - 4;
                if (length + encoded > 600)
                {
                    chunks.Add(chunk.ToString());
                    chunk.Clear();
                    length = 0;
                }
                chunk.Append(scalar);
                length += encoded;
            }
            if (chunk.Length > 0)
                chunks.Add(chunk.ToString());
            if (chunks.Count > 512)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "RTC message contains too many fragments."
                );
            var id = Guid.NewGuid().ToString("N");
            var packets = new List<byte[]>(chunks.Count);
            for (var i = 0; i < chunks.Count; i++)
            {
                var packet = Encoding.UTF8.GetBytes(JsonCodec.Encode(new Dictionary<string, object>
                {
                    ["event"] = "__agora_chunk__",
                    ["eventId"] = id,
                    ["index"] = i,
                    ["count"] = chunks.Count,
                    ["data"] = chunks[i]
                }));
                if (packet.Length > 800)
                    throw new XmaxException(
                        XmaxErrorCode.InvalidConfiguration,
                        "RTC fragment exceeds its packet budget."
                    );
                packets.Add(packet);
            }
            return packets;
        }
    }

    /// <summary>
    /// Serializes whole messages, coalesces unsent trajectories, and limits signaling traffic.
    /// </summary>
    ///
    /// <remarks>
    /// All queue operations run on the creating Unity thread. Closing cancels pending sends.
    /// </remarks>
    internal sealed class RtcMessageQueue
    {

        /// <summary>
        /// A pending message whose replacement never interleaves active fragments.
        /// </summary>
        private sealed class Entry
        {

            /// <summary>
            /// The encoded message packets.
            /// </summary>
            internal IReadOnlyList<byte[]> Packets;

            /// <summary>
            /// The coalescing key, or null for a control message.
            /// </summary>
            internal string Key;

            /// <summary>
            /// Whether send failure terminates the connection.
            /// </summary>
            internal bool Fatal;

            /// <summary>
            /// The send completion, shared by callers of replaced trajectories.
            /// </summary>
            internal readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// The native packet sender.
        /// </summary>
        private readonly Action<byte[]> _send;

        /// <summary>
        /// The handler for fatal send failures.
        /// </summary>
        private readonly Action<XmaxException> _failed;

        /// <summary>
        /// The unsent message queue.
        /// </summary>
        private readonly List<Entry> _pending = new List<Entry>();

        /// <summary>
        /// The sliding packet and byte accounting window.
        /// </summary>
        private readonly Queue<(double Time, int Bytes)> _sent = new Queue<(double, int)>();

        /// <summary>
        /// The monotonic rate-limit clock.
        /// </summary>
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>
        /// The lifetime cancellation source.
        /// </summary>
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        /// <summary>
        /// The currently observed drain task.
        /// </summary>
        private Task _drain = Task.CompletedTask;

        /// <summary>
        /// Whether a drain loop owns the queue.
        /// </summary>
        private bool _running;

        /// <summary>
        /// Whether further messages are rejected.
        /// </summary>
        private bool _closed;

        /// <summary>
        /// The active message, canceled together with the queue.
        /// </summary>
        private Entry _active;

        /// <summary>
        /// Creates a queue for one joined room.
        /// </summary>
        internal RtcMessageQueue(
            Action<byte[]> send,
            Action<XmaxException> failed
        )
        {
            _send = send;
            _failed = failed;
        }

        /// <summary>
        /// Enqueues a whole message and returns completion of all its native sends.
        /// </summary>
        ///
        /// <remarks>Cancellation stops the caller's wait; admitted packets continue until queue shutdown.</remarks>
        internal Task Enqueue(
            string message,
            CancellationToken token = default,
            string key = null,
            bool fatal = true
        )
        {
            if (_closed)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "RTC signaling is closed."
                );
            token.ThrowIfCancellationRequested();
            var packets = RtcMessageCodec.Encode(message);
            for (var i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].Completion.Task.IsCanceled)
                {
                    _pending.RemoveAt(i);
                }
            if (key != null)
            {
                var previous = _pending.Find(entry => entry.Key == key);
                if (previous != null)
                {
                    previous.Packets = packets;
                    return WaitForCompletionAsync(previous.Completion.Task, token);
                }
            }
            if (_pending.Count + (_active == null ? 0 : 1) >= 64)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "RTC signaling queue is full."
                );
            var next = new Entry { Packets = packets, Key = key, Fatal = fatal };
            _ = ObserveCompletionAsync(next.Completion.Task);
            _pending.Add(next);
            if (!_running)
            {
                _running = true;
                _drain = DrainAsync();
            }
            return WaitForCompletionAsync(next.Completion.Task, token);
        }

        /// <summary>Waits for admitted signaling without cancelling its queued or active packets.</summary>
        internal static async Task WaitForCompletionAsync(
            Task pending,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            if (!token.CanBeCanceled)
            {
                await pending;
                return;
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelled.TrySetResult(true)))
            {
                await Task.WhenAny(pending, cancelled.Task);
                token.ThrowIfCancellationRequested();
                await pending;
            }
        }

        /// <summary>Observes sends whose callers stop waiting; the queue reports native failures separately.</summary>
        internal static async Task ObserveCompletionAsync(
            Task pending
        )
        {
            try
            {
                await pending;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Discards only trajectories that have not begun sending.
        /// </summary>
        internal void DiscardPendingTracks()
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].Key != null)
                {
                    _pending[i].Completion.TrySetCanceled();
                    _pending.RemoveAt(i);
                }
        }

        /// <summary>
        /// Closes the queue and waits until its native send loop has stopped.
        /// </summary>
        internal async Task CloseAsync()
        {
            if (!_closed)
            {
                _closed = true;
                _lifetime.Cancel();
                _active?.Completion.TrySetCanceled();
                foreach (var entry in _pending)
                {
                    entry.Completion.TrySetCanceled();
                }
                _pending.Clear();
            }
            await _drain;
        }

        /// <summary>
        /// Submits each message atomically with respect to other message fragments.
        /// </summary>
        private async Task DrainAsync()
        {
            try
            {
                // Yield so all queue state is installed before callbacks can close it.
                await Task.Yield();
                while (!_closed && _pending.Count > 0)
                {
                    if (_pending[0].Completion.Task.IsCanceled)
                    {
                        _pending.RemoveAt(0);
                        continue;
                    }
                    // Wait before dequeueing so a stale trajectory remains replaceable while rate limited.
                    await WaitQuotaAsync(
                        _pending[0].Packets[0].Length,
                        _lifetime.Token
                    );
                    if (_closed || _pending.Count == 0)
                        continue;
                    var entry = _pending[0];
                    _pending.RemoveAt(0);
                    _active = entry;
                    try
                    {
                        foreach (var packet in entry.Packets)
                        {
                            await WaitQuotaAsync(
                                packet.Length,
                                _lifetime.Token
                            );
                            _lifetime.Token.ThrowIfCancellationRequested();
                            _send(packet);
                            _sent.Enqueue((_clock.Elapsed.TotalSeconds, packet.Length));
                        }
                        entry.Completion.TrySetResult(true);
                    }
                    catch (OperationCanceledException)
                    {
                        entry.Completion.TrySetCanceled();
                    }
                    catch (Exception exception)
                    {
                        var error = XmaxErrorConverter.Wrap(exception);
                        entry.Completion.TrySetException(error);
                        if (entry.Fatal)
                        {
                            _failed?.Invoke(error);
                            break;
                        }
                        XmaxLogger.Rtc.Warn(() => "发送配置失败 (Failed to Send Configuration)\n└─ " + ErrorMessageFormatter.Format(error));
                    }
                    finally
                    {
                        _active = null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _running = false;
            }
        }

        /// <summary>
        /// Waits for headroom below 60 packets and 30 KB per second.
        /// </summary>
        private async Task WaitQuotaAsync(
            int bytes,
            CancellationToken token
        )
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var now = _clock.Elapsed.TotalSeconds;
                while (_sent.Count > 0 && now - _sent.Peek().Time >= 1)
                    _sent.Dequeue();
                var total = 0;
                foreach (var sample in _sent)
                    total += sample.Bytes;
                if (_sent.Count < 58 && total + bytes <= 29000)
                    return;
                await Task.Delay(
                    Math.Max(1, (int)Math.Ceiling((1 - (now - _sent.Peek().Time)) * 1000)),
                    token
                );
            }
        }
    }
}
