//
//  XmaxRealtimeLifecycleManager.cs
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

    /// <summary>The kinds of coordinated local media and realtime operations.</summary>
    internal enum RealtimeOperation
    {

        /// <summary>Creates or replaces a local media stream.</summary>
        Media,

        /// <summary>Establishes a server session and RTC connection.</summary>
        Connection,

        /// <summary>Starts generation, optionally establishing its connection.</summary>
        Generation,

        /// <summary>Changes conditions or transport settings for an active generation.</summary>
        Configuration
    }

    /// <summary>The resources affected by an internal termination request.</summary>
    internal enum TerminationScope
    {

        /// <summary>Releases a pending generation while retaining its connection and media.</summary>
        Generation = -1,

        /// <summary>Releases generation and connection while retaining local media.</summary>
        Connection,

        /// <summary>Releases connection, generation, and local media.</summary>
        All
    }

    /// <summary>Owns operation admission, lifecycle state, generation replacement, and scoped cleanup.</summary>
    ///
    /// <remarks>All members run on the creating Unity main thread. Await operations without blocking that thread.</remarks>
    internal sealed class XmaxRealtimeLifecycleManager
    {

        /// <summary>The creating Unity thread identifier.</summary>
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;

        /// <summary>The currently admitted foreground or background operation.</summary>
        private Operation _active;

        /// <summary>The latest generation request, including a request waiting for its predecessor.</summary>
        private GenerationRequest _generationRequest;

        /// <summary>The number of foreground callers preventing background adaptation from overtaking them.</summary>
        private int _foregroundRequests;

        /// <summary>The merged cleanup currently blocking admission.</summary>
        private TaskCompletionSource<bool> _termination;

        /// <summary>The greatest scope requested by concurrent cleanup callers.</summary>
        private TerminationScope _scope;

        /// <summary>The first failure retained for the current cleanup.</summary>
        private XmaxException _failure;

        /// <summary>The reason delivered after cleanup.</summary>
        private RealtimeReason _reason;

        /// <summary>The scoped resource cleanup supplied by the component factory.</summary>
        private Func<TerminationScope, Task> _cleanup;

        /// <summary>The local media availability query used to choose the final state.</summary>
        private Func<bool> _hasLocalMedia;

        /// <summary>The active server session query.</summary>
        private Func<string> _sessionId;

        /// <summary>The last committed public lifecycle state.</summary>
        internal RealtimeState CurrentState { get; private set; } = new RealtimeState(RealtimeConnectionState.Idle);

        /// <summary>Occurs after state changes; subscriber exceptions do not interrupt the lifecycle.</summary>
        internal event Action<RealtimeState> StateChanged;

        /// <summary>Occurs after failure cleanup completes.</summary>
        internal event Action<XmaxException> Failed;

        /// <summary>Whether resource cleanup is blocking new operations.</summary>
        internal bool IsTerminating => _termination != null;

        /// <summary>Whether an admitted operation or queued generation request owns the lifecycle.</summary>
        internal bool HasOperation => _active != null || _generationRequest != null;

        /// <summary>Binds resource cleanup after the component graph has been assembled.</summary>
        internal void Configure(
            Func<TerminationScope, Task> cleanup,
            Func<bool> hasLocalMedia,
            Func<string> sessionId
        )
        {
            _cleanup = cleanup;
            _hasLocalMedia = hasLocalMedia;
            _sessionId = sessionId;
        }

        /// <summary>Requires the creating Unity main thread.</summary>
        internal void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Use realtime APIs on the Unity main thread where the manager was created."
                );
        }

        /// <summary>Requires the creating thread and rejects calls during explicit resource cleanup.</summary>
        internal void RequireAvailable()
        {
            RequireThread();
            if (IsTerminating)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Realtime cleanup is in progress. Await it before starting new work."
                );
        }

        /// <summary>Runs a synchronous media operation and publishes readiness after releasing admission.</summary>
        internal T RunMedia<T>(
            Func<T> body
        )
        {
            RequireAvailable();
            if (HasOperation)
                throw Busy();
            var operation = _active = new Operation(
                this,
                RealtimeOperation.Media,
                CancellationToken.None,
                null,
                null,
                false
            );
            try
            {
                Commit(
                    new RealtimeState(RealtimeConnectionState.Preparing),
                    operation
                );
                var value = body();
                operation.EnsureCurrent();
                Release(operation);
                Publish(new RealtimeState(RealtimeConnectionState.Ready));
                return value;
            }
            catch
            {
                Release(operation);
                if (!IsTerminating)
                    Publish(new RealtimeState(_hasLocalMedia() ? RealtimeConnectionState.Ready : RealtimeConnectionState.Idle));
                throw;
            }
        }

        /// <summary>Runs foreground work after any active background configuration has completed.</summary>
        internal async Task<T> RunAsync<T>(
            RealtimeOperation kind,
            Func<Operation, Task<T>> body,
            CancellationToken token,
            TerminationScope? failureScope = null
        )
        {
            RequireThread();
            _foregroundRequests++;
            try
            {
                return await ExecuteAsync(
                    kind,
                    body,
                    token,
                    failureScope,
                    null,
                    false
                );
            }
            finally
            {
                _foregroundRequests--;
            }
        }

        /// <summary>Runs foreground work without a result value.</summary>
        internal Task RunAsync(
            RealtimeOperation kind,
            Func<Operation, Task> body,
            CancellationToken token,
            TerminationScope? failureScope = null
        )
            => RunAsync<object>(
                kind,
                async operation =>
                {
                    await body(operation);
                    return null;
                },
                token,
                failureScope
            );

        /// <summary>Replaces an unfinished generation request and waits for its work and cleanup before starting.</summary>
        internal async Task<T> RunGenerationAsync<T>(
            Func<Operation, Task<T>> body,
            CancellationToken token
        )
        {
            RequireThread();
            token.ThrowIfCancellationRequested();
            if ((IsTerminating && _generationRequest == null) ||
                (_active != null && !_active.Background && _active.Request == null))
                throw Busy();
            var previous = _generationRequest;
            var request = new GenerationRequest(token);
            _generationRequest = request;
            _foregroundRequests++;
            previous?.Cancel();
            try
            {
                if (previous != null)
                    await previous.Done.Task;
                request.Token.ThrowIfCancellationRequested();
                return await ExecuteAsync(
                    RealtimeOperation.Generation,
                    body,
                    request.Token,
                    null,
                    request,
                    false
                );
            }
            finally
            {
                FinishRequest(request);
                _foregroundRequests--;
            }
        }

        /// <summary>Replaces generation work without a result value.</summary>
        internal Task RunGenerationAsync(
            Func<Operation, Task> body,
            CancellationToken token
        )
            => RunGenerationAsync<object>(
                async operation =>
                {
                    await body(operation);
                    return null;
                },
                token
            );

        /// <summary>Runs low-priority adaptation only while no foreground work is pending.</summary>
        internal async Task<bool> RunAdaptiveAsync(
            Func<Operation, Task> body
        )
        {
            RequireThread();
            if (IsTerminating || HasOperation || _foregroundRequests > 0 || CurrentState.ConnectionState != RealtimeConnectionState.Generating)
                return false;
            await ExecuteAsync<object>(
                RealtimeOperation.Configuration,
                async operation =>
                {
                    await body(operation);
                    return null;
                },
                CancellationToken.None,
                null,
                null,
                true
            );
            return true;
        }

        /// <summary>Executes an admitted operation, applies failure cleanup, and publishes staged completion after release.</summary>
        private async Task<T> ExecuteAsync<T>(
            RealtimeOperation kind,
            Func<Operation, Task<T>> body,
            CancellationToken token,
            TerminationScope? failureScope,
            GenerationRequest request,
            bool background
        )
        {
            if (_active != null && _active.Background)
                await _active.Done.Task;
            token.ThrowIfCancellationRequested();
            RequireAvailable();
            if (_active != null || (request == null && _generationRequest != null) ||
                (request != null && !ReferenceEquals(
                    request,
                    _generationRequest
                )))
                throw Busy();
            if (kind == RealtimeOperation.Generation && CurrentState.ConnectionState == RealtimeConnectionState.Generating)
                kind = RealtimeOperation.Configuration;
            var operation = _active = new Operation(
                this,
                kind,
                token,
                failureScope,
                request,
                background
            );
            try
            {
                var result = await body(operation);
                operation.EnsureCurrent();
                var completion = operation.CompletionState;
                Release(operation);
                if (request != null)
                    FinishRequest(request);
                if (completion != null)
                    Publish(completion);
                return result;
            }
            catch (Exception exception)
            {
                var cancelled = exception is OperationCanceledException || operation.Token.IsCancellationRequested;
                var resolved = cancelled ? null : XmaxErrorConverter.Wrap(exception);
                var termination = _termination?.Task;
                var replaced = request != null && _generationRequest != null && !ReferenceEquals(
                    request,
                    _generationRequest
                );
                Release(operation);
                if (termination != null)
                {
                    if (request != null)
                        FinishRequest(request);
                    await termination;
                    if (operation.Failure != null)
                        throw operation.Failure;
                    throw new OperationCanceledException(operation.Token);
                }
                if (kind != RealtimeOperation.Configuration && operation.FailureScope.HasValue)
                {
                    var scope = cancelled && replaced && CurrentState.ConnectionState == RealtimeConnectionState.Connected
                        ? TerminationScope.Generation : operation.FailureScope.Value;
                    await TerminateAsync(
                        scope,
                        resolved,
                        cancelled ? RealtimeReason.Normal : RealtimeReason.Failure(resolved),
                        true
                    );
                }
                else if (!cancelled)
                    XmaxRealtimeErrorManager.Report(exception);
                if (kind == RealtimeOperation.Media && !IsTerminating && CurrentState.ConnectionState == RealtimeConnectionState.Preparing)
                    Publish(new RealtimeState(_hasLocalMedia() ? RealtimeConnectionState.Ready : RealtimeConnectionState.Idle));
                if (cancelled)
                    throw new OperationCanceledException(operation.Token);
                throw resolved;
            }
            finally
            {
                Release(operation);
            }
        }

        /// <summary>Commits an intermediate state while the operation remains current.</summary>
        internal void Commit(
            RealtimeState state,
            Operation operation
        )
        {
            operation.EnsureCurrent();
            Publish(state);
            operation.EnsureCurrent();
        }

        /// <summary>Stages the final state for notification after operation admission has been released.</summary>
        internal void Complete(
            RealtimeState state,
            Operation operation
        )
        {
            operation.EnsureCurrent();
            operation.CompletionState = state;
        }

        /// <summary>Releases the active operation and signals its completion once.</summary>
        private void Release(
            Operation operation
        )
        {
            if (!ReferenceEquals(
                _active,
                operation
            ))
                return;
            _active = null;
            operation.DisposeCancellation();
            operation.Done.TrySetResult(true);
        }

        /// <summary>Completes a generation request without overwriting a newer request.</summary>
        private void FinishRequest(
            GenerationRequest request
        )
        {
            if (ReferenceEquals(
                _generationRequest,
                request
            ))
                _generationRequest = null;
            request.Complete();
        }

        /// <summary>Cancels affected work and merges cleanup requests; internal rollback preserves successor requests.</summary>
        internal Task TerminateAsync(
            TerminationScope scope,
            XmaxException error = null,
            RealtimeReason reason = null,
            bool internalRollback = false
        )
        {
            RequireThread();
            if (!internalRollback && error != null && _termination != null &&
                _scope != TerminationScope.Generation && scope != TerminationScope.All)
                return Task.CompletedTask;

            var hasConnectionOperation = _active != null && Affects(
                TerminationScope.Connection,
                _active.Kind
            );
            if (!internalRollback && scope == TerminationScope.Connection && !hasConnectionOperation &&
                _generationRequest == null && !IsTerminating &&
                (CurrentState.ConnectionState == RealtimeConnectionState.Idle ||
                 CurrentState.ConnectionState == RealtimeConnectionState.Preparing ||
                 CurrentState.ConnectionState == RealtimeConnectionState.Ready))
                return Task.CompletedTask;
            GenerationRequest waiting = null;
            if (!internalRollback)
            {
                waiting = _generationRequest;
                _generationRequest = null;
                waiting?.Cancel();
            }
            if (_termination != null)
            {
                var pending = _termination;
                if (scope > _scope)
                    _scope = scope;
                if (_failure == null && error != null)
                {
                    _failure = error;
                    _reason = RealtimeReason.Failure(error);
                }
                if (_active != null && Affects(
                    _scope,
                    _active.Kind
                ))
                {
                    if (_active.Failure == null)
                        _active.Failure = _failure;
                    _active.Cancel();
                }
                if (_scope != TerminationScope.Generation)
                    Publish(new RealtimeState(
                            RealtimeConnectionState.Disconnecting,
                            _sessionId() ?? CurrentState.SessionId
                        ));
                return pending.Task;
            }
            var session = _sessionId() ?? CurrentState.SessionId;
            _scope = scope;
            _failure = error;
            _reason = reason ?? (error == null ? RealtimeReason.Normal : RealtimeReason.Failure(error));
            var completion = _termination = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var active = _active != null && Affects(
                scope,
                _active.Kind
            ) ? _active : null;
            if (active != null)
            {
                active.Failure = error;
                active.Cancel();
            }
            if (scope != TerminationScope.Generation)
                Publish(new RealtimeState(
                        RealtimeConnectionState.Disconnecting,
                        session
                    ));
            _ = FinishTerminationAsync(
                active,
                waiting,
                session,
                completion
            );
            return completion.Task;
        }

        /// <summary>Awaits affected work, completes expanded cleanup scopes, and releases admission before notifications.</summary>
        private async Task FinishTerminationAsync(
            Operation active,
            GenerationRequest waiting,
            string session,
            TaskCompletionSource<bool> completion
        )
        {
            if (active != null)
                await active.Done.Task;
            if (waiting != null)
                await waiting.Done.Task;

            XmaxException cleanupFailure = null;
            TerminationScope cleaned;
            do
            {
                cleaned = _scope;
                try
                {
                    await _cleanup(cleaned);
                }
                catch (Exception exception)
                {
                    if (cleanupFailure == null)
                        cleanupFailure = XmaxErrorConverter.Wrap(exception);
                    XmaxRealtimeErrorManager.Report(exception);
                }
            } while (_scope > cleaned);

            var scope = _scope;
            var failure = _failure ?? cleanupFailure;
            var reason = failure == null ? _reason : RealtimeReason.Failure(failure);
            _termination = null;
            _failure = null;
            _reason = null;
            var state = scope == TerminationScope.Generation ? RealtimeConnectionState.Connected
                : scope == TerminationScope.All ? RealtimeConnectionState.Idle
                : _hasLocalMedia() ? RealtimeConnectionState.Ready : RealtimeConnectionState.Idle;
            var finalState = new RealtimeState(
                state,
                session,
                reason: scope == TerminationScope.Generation ? null : reason
            );
            Publish(finalState);
            if (failure != null)
                EventDispatch.Raise(
                    Failed,
                    failure,
                    () => ReferenceEquals(
                        CurrentState,
                        finalState
                    )
                );
            completion.TrySetResult(true);
        }

        /// <summary>Determines whether resource cleanup cancels the specified operation.</summary>
        private static bool Affects(
            TerminationScope scope,
            RealtimeOperation kind
        )
        {
            switch (scope)
            {
                case TerminationScope.Generation:
                    return kind == RealtimeOperation.Generation || kind == RealtimeOperation.Configuration;
                case TerminationScope.Connection:
                    return kind != RealtimeOperation.Media;
                case TerminationScope.All:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Publishes changed state and stops stale delivery after a reentrant transition.</summary>
        private void Publish(
            RealtimeState state
        )
        {
            if (CurrentState.ConnectionState == state.ConnectionState && CurrentState.SessionId == state.SessionId &&
                CurrentState.TaskId == state.TaskId && Equals(
                    CurrentState.Reason,
                    state.Reason
                ))
                return;
            CurrentState = state;
            XmaxLogger.Realtime.Info(() => "State=" + state.ConnectionState);
            EventDispatch.Raise(
                StateChanged,
                state,
                () => ReferenceEquals(
                    CurrentState,
                    state
                )
            );
        }

        /// <summary>Creates the operation-conflict error.</summary>
        private static XmaxException Busy() => new XmaxException(
            XmaxErrorCode.InvalidConfiguration,
            "Another realtime operation is in progress."
        );

        /// <summary>The cancellable lease protecting one admitted operation.</summary>
        internal sealed class Operation
        {

            /// <summary>The operation's linked cancellation source.</summary>
            private readonly CancellationTokenSource _cancellation;

            /// <summary>The coordinator validating ownership of this lease.</summary>
            private readonly XmaxRealtimeLifecycleManager _owner;

            /// <summary>The operation kind governing failure cleanup.</summary>
            internal RealtimeOperation Kind { get; }

            /// <summary>The token retained independently of cancellation-source disposal.</summary>
            internal CancellationToken Token { get; }

            /// <summary>The resources to release when execution fails or is cancelled.</summary>
            internal TerminationScope? FailureScope { get; set; }

            /// <summary>The owning generation request, if any.</summary>
            internal GenerationRequest Request { get; }

            /// <summary>Whether this operation yields admission to waiting foreground requests.</summary>
            internal bool Background { get; }

            /// <summary>The final state staged for publication after release.</summary>
            internal RealtimeState CompletionState { get; set; }

            /// <summary>The runtime failure responsible for external termination.</summary>
            internal XmaxException Failure { get; set; }

            /// <summary>Completes after operation execution releases resource ownership.</summary>
            internal readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Creates an admitted operation with caller cancellation.</summary>
            internal Operation(
                XmaxRealtimeLifecycleManager owner,
                RealtimeOperation kind,
                CancellationToken token,
                TerminationScope? failureScope,
                GenerationRequest request,
                bool background
            )
            {
                _owner = owner;
                Kind = kind;
                FailureScope = failureScope;
                Request = request;
                Background = background;
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                Token = _cancellation.Token;
            }

            /// <summary>Requires the current operation lease and a noncancelled token.</summary>
            internal void EnsureCurrent()
            {
                Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(
                    _owner._active,
                    this
                ))
                    throw new OperationCanceledException(Token);
            }

            /// <summary>Cancels execution without releasing ownership.</summary>
            internal void Cancel() => _cancellation.Cancel();

            /// <summary>Disposes linked cancellation after execution has released ownership.</summary>
            internal void DisposeCancellation() => _cancellation.Dispose();
        }

        /// <summary>A generation request retained until its work and required cleanup have completed.</summary>
        internal sealed class GenerationRequest
        {

            /// <summary>The cancellation source linking replacement and caller cancellation.</summary>
            private readonly CancellationTokenSource _cancellation;

            /// <summary>Whether completion and cancellation-source disposal have occurred.</summary>
            private bool _completed;

            /// <summary>The linked request token.</summary>
            internal CancellationToken Token { get; }

            /// <summary>The completion barrier awaited by successor requests and explicit termination.</summary>
            internal readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Creates a generation request linked to caller cancellation.</summary>
            internal GenerationRequest(
                CancellationToken token
            )
            {
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                Token = _cancellation.Token;
            }

            /// <summary>Cancels an unfinished request.</summary>
            internal void Cancel()
            {
                if (!_completed)
                    _cancellation.Cancel();
            }

            /// <summary>Releases successor requests once work and cleanup finish.</summary>
            internal void Complete()
            {
                if (_completed)
                    return;
                _completed = true;
                _cancellation.Dispose();
                Done.TrySetResult(true);
            }
        }
    }
}
