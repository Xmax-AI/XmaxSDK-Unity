//
//  RoomCommandController.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>Retries frozen room commands independently of transport completion and generation readiness.</summary>
    internal sealed class RoomCommandController
    {

        /// <summary>The generation operations acknowledged by the service.</summary>
        internal enum Kind
        {

            /// <summary>Starts a generation task.</summary>
            Start,

            /// <summary>Updates generation conditions.</summary>
            ChangeCondition,

            /// <summary>Updates returned video dimensions.</summary>
            ChangeTargetSize
        }

        /// <summary>One independently cancellable transport attempt.</summary>
        private sealed class Attempt
        {

            /// <summary>Whether this is the original send.</summary>
            internal bool Initial;

            /// <summary>Cancels the attempt's queued and unsent fragments.</summary>
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        }

        /// <summary>One immutable logical command and its caller completion.</summary>
        private sealed class Command
        {

            /// <summary>The generation operation.</summary>
            internal Kind Kind;

            /// <summary>The generation task owning the command.</summary>
            internal string TaskId;

            /// <summary>The identifier retained across all retransmissions.</summary>
            internal string Xid;

            /// <summary>The serialized payload retained across all attempts.</summary>
            internal string Message;

            /// <summary>Rejects obsolete configuration until a complete send succeeds.</summary>
            internal Action EnsureCurrent;

            /// <summary>The first complete send, initial failure, or explicit cancellation.</summary>
            internal readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Cancels scheduled retries and the receipt deadline.</summary>
            internal readonly CancellationTokenSource Timers = new CancellationTokenSource();

            /// <summary>The attempts retained until their transport completions settle.</summary>
            internal readonly Dictionary<Guid, Attempt> Transports = new Dictionary<Guid, Attempt>();

            /// <summary>The caller's cancellation registration while its send remains unresolved.</summary>
            internal CancellationTokenRegistration CallerCancellation;

            /// <summary>The number of attempts submitted to the transport.</summary>
            internal int Attempts;

            /// <summary>Whether redundant attempts may still be submitted.</summary>
            internal bool Retrying = true;

            /// <summary>Whether a complete attempt has succeeded.</summary>
            internal bool Sent;

            /// <summary>Whether lifecycle cleanup or supersession invalidated the command.</summary>
            internal bool Cancelled;
        }

        /// <summary>The lock protecting submission, cancellation, and transport completion.</summary>
        private readonly object _gate = new object();

        /// <summary>The Unity context used to marshal caller cancellation.</summary>
        private readonly SynchronizationContext _context = SynchronizationContext.Current;

        /// <summary>Submits one complete, independently cancellable message attempt.</summary>
        private readonly Func<string, CancellationToken, Task> _sendMessage;

        /// <summary>The delay provider used for offsets relative to the initial attempt.</summary>
        private readonly Func<int, CancellationToken, Task> _delay;

        /// <summary>The pending logical commands indexed by their stable identifiers.</summary>
        private readonly Dictionary<string, Command> _pending = new Dictionary<string, Command>();

        /// <summary>Orders initial commands without waiting for receipts or retry deadlines.</summary>
        private Task _sendTail = Task.CompletedTask;

        /// <summary>Whether this room scheduler permanently rejects new commands.</summary>
        private bool _closed;

        /// <summary>Creates a scheduler over the room's cancellable signaling transport.</summary>
        internal RoomCommandController(
            Func<string, CancellationToken, Task> sendMessage,
            Func<int, CancellationToken, Task> delay = null
        )
        {
            _sendMessage = sendMessage ?? throw new ArgumentNullException(nameof(sendMessage));
            _delay = delay ?? ((milliseconds, token) => Task.Delay(milliseconds, token));
        }

        /// <summary>Freezes a command, supersedes matching work, and returns its first successful send.</summary>
        internal Task Send(
            Kind kind,
            string taskId,
            Func<string, string> createMessage,
            CancellationToken cancellationToken,
            Action ensureCurrent = null
        )
        {
            lock (_gate)
            {
                if (_closed)
                    throw new OperationCanceledException("RTC command room is closed.");
                cancellationToken.ThrowIfCancellationRequested();
                ensureCurrent?.Invoke();
                var xid = Guid.NewGuid().ToString();
                var message = createMessage(xid);
                CancelMatching(pending => kind == Kind.Start || (pending.TaskId == taskId && pending.Kind == kind));
                var command = new Command
                {
                    Kind = kind,
                    TaskId = taskId,
                    Xid = xid,
                    Message = message,
                    EnsureCurrent = ensureCurrent
                };
                _pending.Add(
                    xid,
                    command
                );
                command.CallerCancellation = cancellationToken.Register(() => DispatchCancellation(command));
                if (command.Completion.Task.IsCompleted)
                    command.CallerCancellation.Dispose();
                _ = RtcMessageQueue.ObserveCompletionAsync(command.Completion.Task);
                var previous = _sendTail;
                var ordering = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _sendTail = ordering.Task;
                _ = SendOrderedAsync(
                    previous,
                    command,
                    ordering
                );
                return command.Completion.Task;
            }
        }

        /// <summary>Stops redundant work for a valid receipt without completing an unresolved original send.</summary>
        internal void Acknowledge(
            string xid,
            long count
        )
        {
            lock (_gate)
            {
                if (count < 1 || count > 9007199254740991L || string.IsNullOrEmpty(xid) ||
                    !_pending.TryGetValue(xid, out var command) || !command.Retrying || command.Attempts == 0)
                    return;
                try
                {
                    if (!command.Sent)
                        command.EnsureCurrent?.Invoke();
                    XmaxLogger.Room.Info(() => "接收房间信令 (Inbound Room Signaling)\n"
                        + "├─ " + XmaxLogger.Localized("类型：", "Type: ") + "xid_ack\n├─ "
                        + XmaxLogger.Localized("命令：", "Command: ") + Name(command.Kind)
                        + "\n├─ xid: " + command.Xid + "\n├─ count: " + count + "\n└─ "
                        + XmaxLogger.Localized("发送次数：", "Attempts: ") + command.Attempts);
                    StopRetries(command);
                }
                catch (Exception exception)
                {
                    CancelCommand(
                        command,
                        exception
                    );
                }
            }
        }

        /// <summary>Cancels one ended task or all room commands before asynchronous cleanup.</summary>
        internal void Cancel(
            string taskId = null
        )
        {
            lock (_gate)
            {
                CancelMatching(command => taskId == null || command.TaskId == taskId);
                if (taskId == null)
                    _sendTail = Task.CompletedTask;
            }
        }

        /// <summary>Permanently invalidates the scheduler before leaving its room.</summary>
        internal void Close()
        {
            lock (_gate)
            {
                _closed = true;
                Cancel();
            }
        }

        /// <summary>Preserves earlier ordering barriers even when an intermediate command is superseded.</summary>
        private async Task SendOrderedAsync(
            Task previous,
            Command command,
            TaskCompletionSource<bool> ordering
        )
        {
            try
            {
                await previous;
                lock (_gate)
                {
                    if (!command.Cancelled)
                        Begin(command);
                }
                await command.Completion.Task;
            }
            catch (Exception)
            {
            }
            finally
            {
                ordering.TrySetResult(true);
            }
        }

        /// <summary>Schedules the two send rounds and their six-second receipt deadline.</summary>
        private void Begin(
            Command command
        )
        {
            foreach (var offset in new[] { 300, 1000, 3000, 3300, 4000, 6000 })
                _ = ScheduleAsync(
                    command,
                    offset,
                    command.Timers.Token
                );
            Submit(
                command,
                true
            );
        }

        /// <summary>Submits a redundant attempt or stops retrying when its deadline expires.</summary>
        private async Task ScheduleAsync(
            Command command,
            int offset,
            CancellationToken token
        )
        {
            try
            {
                await _delay(
                    offset,
                    token
                );
                lock (_gate)
                {
                    if (token.IsCancellationRequested || command.Cancelled || !command.Retrying)
                        return;
                    if (offset == 6000)
                    {
                        XmaxLogger.Room.Warn(() => "RTC command ACK not received after 6000 ms; retries stopped: "
                            + Name(command.Kind) + ", xid: " + command.Xid);
                        StopRetries(command);
                    }
                    else
                        Submit(
                            command,
                            false
                        );
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>Starts an attempt while retaining cancellation until its transport settles.</summary>
        private void Submit(
            Command command,
            bool initial
        )
        {
            if (command.Cancelled || !command.Retrying)
                return;
            try
            {
                if (!command.Sent)
                    command.EnsureCurrent?.Invoke();
            }
            catch (Exception exception)
            {
                CancelCommand(
                    command,
                    exception
                );
                return;
            }
            var id = Guid.NewGuid();
            var attempt = new Attempt { Initial = initial };
            command.Attempts++;
            command.Transports.Add(
                id,
                attempt
            );
            Task sending;
            try
            {
                sending = _sendMessage(
                    command.Message,
                    attempt.Cancellation.Token
                );
            }
            catch (Exception exception)
            {
                Finish(
                    command,
                    id,
                    attempt,
                    exception
                );
                return;
            }
            if (!command.Retrying && (!initial || command.Sent))
                attempt.Cancellation.Cancel();
            _ = ObserveTransportAsync(
                command,
                id,
                attempt,
                sending
            );
        }

        /// <summary>Observes transport completion on the scheduler's captured context.</summary>
        private async Task ObserveTransportAsync(
            Command command,
            Guid id,
            Attempt attempt,
            Task sending
        )
        {
            Exception error = null;
            try
            {
                await sending;
            }
            catch (Exception exception)
            {
                error = exception;
            }
            lock (_gate)
                Finish(
                    command,
                    id,
                    attempt,
                    error
                );
        }

        /// <summary>Completes the caller once and preserves initial-send failures.</summary>
        private void Finish(
            Command command,
            Guid id,
            Attempt attempt,
            Exception error
        )
        {
            command.Transports.Remove(id);
            attempt.Cancellation.Dispose();
            if (command.Cancelled)
                return;
            if (error == null)
            {
                command.Sent = true;
                command.Completion.TrySetResult(true);
                command.CallerCancellation.Dispose();
                if (!command.Retrying)
                    _pending.Remove(command.Xid);
            }
            else if (attempt.Initial && !command.Sent)
            {
                if (command.Kind == Kind.Start)
                    CancelMatching(other => !ReferenceEquals(other, command) && other.TaskId == command.TaskId);
                CancelCommand(
                    command,
                    error
                );
            }
            else if (command.Retrying && !(error is OperationCanceledException))
                XmaxLogger.Room.Warn(() => "RTC command retry send failed: " + Name(command.Kind) + ", xid: " + command.Xid);
        }

        /// <summary>Releases redundant work while preserving an unresolved original transport.</summary>
        private void StopRetries(
            Command command
        )
        {
            if (!command.Retrying)
                return;
            command.Retrying = false;
            command.Timers.Cancel();
            command.Timers.Dispose();
            foreach (var attempt in new List<Attempt>(command.Transports.Values))
                if (!attempt.Initial || command.Sent)
                    attempt.Cancellation.Cancel();
            if (command.Sent)
                _pending.Remove(command.Xid);
        }

        /// <summary>Cancels every attempt and resolves an unfinished caller with its operation failure.</summary>
        private void CancelCommand(
            Command command,
            Exception error = null
        )
        {
            if (command.Cancelled)
                return;
            command.Cancelled = true;
            StopRetries(command);
            foreach (var attempt in new List<Attempt>(command.Transports.Values))
                attempt.Cancellation.Cancel();
            if (error == null || error is OperationCanceledException)
                command.Completion.TrySetCanceled();
            else
                command.Completion.TrySetException(error);
            command.CallerCancellation.Dispose();
            _pending.Remove(command.Xid);
        }

        /// <summary>Cancels a snapshot of commands matching a task or supersession boundary.</summary>
        private void CancelMatching(
            Predicate<Command> matches
        )
        {
            foreach (var command in new List<Command>(_pending.Values))
                if (matches(command))
                    CancelCommand(command);
        }

        /// <summary>Marshals caller cancellation without cancelling retries after a successful send.</summary>
        private void DispatchCancellation(
            Command command
        )
        {
            Action cancel = () =>
            {
                lock (_gate)
                    if (!command.Completion.Task.IsCompleted)
                        CancelCommand(command);
            };
            if (_context == null)
                ThreadPool.QueueUserWorkItem(_ => cancel());
            else if (ReferenceEquals(_context, SynchronizationContext.Current))
                cancel();
            else
                _context.Post(
                    _ => cancel(),
                    null
                );
        }

        /// <summary>The wire event name for an acknowledged command kind.</summary>
        private static string Name(
            Kind kind
        ) => kind == Kind.Start ? "start" : kind == Kind.ChangeCondition ? "change_condition" : "change_target_size";
    }
}
