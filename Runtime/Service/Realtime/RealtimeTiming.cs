//
//  RealtimeTiming.cs
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
using System.Globalization;

namespace Xmax.SDK
{

    /// <summary>Records the four realtime startup stages using a monotonic clock.</summary>
    internal sealed class RealtimeTiming
    {

        /// <summary>Protects timestamps when startup callbacks arrive from different threads.</summary>
        private readonly object _gate = new object();

        /// <summary>The clock returning monotonic milliseconds.</summary>
        private readonly Func<double> _now;

        /// <summary>The current startup measurement.</summary>
        private State _state = new State();

        /// <summary>Creates a startup timer with an optional monotonic clock.</summary>
        internal RealtimeTiming(
            Func<double> now = null
        )
        {
            _now = now ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
        }

        /// <summary>Begins a new startup measurement and discards previous timestamps.</summary>
        internal void Begin()
        {
            lock (_gate)
                _state = new State { StartedAt = _now() };
        }

        /// <summary>Records the start of server session creation.</summary>
        internal void BeginSessionCreation()
        {
            lock (_gate)
                if (_state.Active)
                    _state.SessionStartedAt = _now();
        }

        /// <summary>Records the completion of server session creation.</summary>
        internal void FinishSessionCreation()
        {
            lock (_gate)
                if (_state.Active)
                    _state.SessionFinishedAt = _now();
        }

        /// <summary>Records the start of RTC room join.</summary>
        internal void BeginRoomJoin()
        {
            lock (_gate)
                if (_state.Active)
                    _state.RoomStartedAt = _now();
        }

        /// <summary>Records the completion of RTC room join.</summary>
        internal void FinishRoomJoin()
        {
            lock (_gate)
                if (_state.Active)
                    _state.RoomFinishedAt = _now();
        }

        /// <summary>Records the start of local stream publication.</summary>
        internal void BeginLocalPublication()
        {
            lock (_gate)
                if (_state.Active)
                    _state.PublicationStartedAt = _now();
        }

        /// <summary>Records when the native local publication request succeeds.</summary>
        internal void FinishLocalPublication()
        {
            lock (_gate)
                if (_state.Active)
                    _state.PublicationFinishedAt = _now();
        }

        /// <summary>Starts measuring first-frame arrival for the current generation task.</summary>
        internal void BeginSignal(
            string taskId
        )
        {
            lock (_gate)
            {
                if (!_state.Active)
                    return;
                _state.TaskId = taskId;
                _state.SignalStartedAt = _now();
                _state.FirstFrameAt = null;
            }
        }

        /// <summary>Records first-frame readiness, ignoring notifications for other tasks.</summary>
        internal void FinishFirstFrame(
            string taskId
        )
        {
            lock (_gate)
            {
                if (_state.Active && _state.TaskId == taskId)
                    _state.FirstFrameAt = _now();
            }
        }

        /// <summary>Logs successful startup once for the matching task when performance logging is enabled.</summary>
        internal void Finish(
            string taskId
        )
        {
            State snapshot;
            lock (_gate)
            {
                if (!_state.Active || _state.TaskId != taskId || !_state.FirstFrameAt.HasValue)
                    return;
                _state.ReadyAt = _now();
                snapshot = _state;
                _state = new State();
            }
            XmaxLogger.Realtime.Info(
                () => Format(snapshot),
                XmaxLoggerOption.Performance
            );
        }

        /// <summary>Logs an incomplete startup once; cancellation does not produce a failure log.</summary>
        internal void FinishFailure(
            Exception error
        )
        {
            State snapshot;
            lock (_gate)
            {
                if (!_state.Active)
                    return;
                _state.FailureAt = _now();
                snapshot = _state;
                _state = new State();
            }
            if (!(error is OperationCanceledException))
                XmaxLogger.Realtime.Info(
                    () => FormatFailure(
                        snapshot,
                        error
                    ),
                    XmaxLoggerOption.Performance
                );
        }

        /// <summary>Formats total startup duration and the four stage details.</summary>
        private static string Format(
            State state
        )
        {
            var lines = new List<string> {
                "实时生成启动耗时 (Realtime Generation Startup Timing)",
                "└─ " + XmaxLogger.Localized(
                    "总耗时：",
                    "Total Duration: "
                ) + Duration(
                    state.StartedAt,
                    state.ReadyAt
                )
            };
            lines.AddRange(StageDetails(
                    state,
                    false
                ));
            return string.Join(
                "\n",
                lines
            );
        }

        /// <summary>Formats elapsed startup time, completed or pending stages, and failure details.</summary>
        private static string FormatFailure(
            State state,
            Exception error
        )
        {
            var lines = new List<string> {
                "实时生成启动未完成耗时 (Incomplete Realtime Generation Startup Timing)",
                "├─ " + XmaxLogger.Localized(
                    "已耗时：",
                    "Elapsed Time: "
                ) + Duration(
                    state.StartedAt,
                    state.FailureAt
                )
            };
            lines.AddRange(StageDetails(
                    state,
                    true
                ));
            lines.Add("├─ " + XmaxLogger.Localized(
                    "停留阶段：",
                    "Current Stage: "
                ) + PendingStage(state));
            lines.Add("└─ " + XmaxLogger.Localized(
                    "失败原因：",
                    "Failure Reason: "
                ) + ErrorMessageFormatter.Format(error));
            return string.Join(
                "\n",
                lines
            );
        }

        /// <summary>Returns session creation, room join, publication, and signal-to-first-frame durations.</summary>
        private static IEnumerable<string> StageDetails(
            State state,
            bool failed
        )
        {
            var starts = new[] { state.SessionStartedAt, state.RoomStartedAt, state.PublicationStartedAt, state.SignalStartedAt };
            var ends = new[] { state.SessionFinishedAt, state.RoomFinishedAt, state.PublicationFinishedAt, state.FirstFrameAt };
            var labels = new[] {
                XmaxLogger.Localized(
                    "创建会话：",
                    "Session Creation: "
                ),
                XmaxLogger.Localized(
                    "RTC 进房：",
                    "RTC Room Join: "
                ),
                XmaxLogger.Localized(
                    "发布本地流：",
                    "Local Stream Publication: "
                ),
                XmaxLogger.Localized(
                    "首帧到达：",
                    "First Frame Arrival: "
                )
            };
            for (var index = 0; index < labels.Length; index++)
                yield return (failed ? "│  " : "   ") + (index == labels.Length - 1 ? "└─ " : "├─ ")
                    + labels[index] + Duration(
                        starts[index],
                        ends[index] ?? (failed ? state.FailureAt : null)
                    );
        }

        /// <summary>Returns the stage pending when startup stopped.</summary>
        private static string PendingStage(
            State state
        )
        {
            if (state.FirstFrameAt.HasValue)
                return XmaxLogger.Localized(
                    "首帧已就绪，正在完成启动",
                    "First Frame Ready, Completing Startup"
                );
            if (state.SignalStartedAt.HasValue)
                return XmaxLogger.Localized(
                    "等待首帧到达",
                    "Waiting for First Frame"
                );
            if (state.PublicationFinishedAt.HasValue)
                return XmaxLogger.Localized(
                    "准备生成",
                    "Preparing Generation"
                );
            if (state.PublicationStartedAt.HasValue)
                return XmaxLogger.Localized(
                    "发布本地流",
                    "Publishing Local Stream"
                );
            if (state.RoomFinishedAt.HasValue)
                return XmaxLogger.Localized(
                    "准备发布本地流",
                    "Preparing Local Stream Publication"
                );
            if (state.RoomStartedAt.HasValue)
                return XmaxLogger.Localized(
                    "RTC 进房",
                    "Joining RTC Room"
                );
            if (state.SessionFinishedAt.HasValue)
                return XmaxLogger.Localized(
                    "RTC 连接准备",
                    "RTC Connection Setup"
                );
            if (state.SessionStartedAt.HasValue)
                return XmaxLogger.Localized(
                    "创建会话",
                    "Creating Session"
                );
            return XmaxLogger.Localized(
                "调用与本地准备",
                "Invocation and Local Preparation"
            );
        }

        /// <summary>Formats monotonic milliseconds, using an em dash for unavailable or reversed timestamps.</summary>
        private static string Duration(
            double? start,
            double? end
        ) => start.HasValue && end.HasValue && end.Value >= start.Value
            ? (end.Value - start.Value).ToString(
                "F1",
                CultureInfo.InvariantCulture
            ) + " ms" : "—";

        /// <summary>The timestamps for one startup; fields are protected by the timer lock.</summary>
        private sealed class State
        {

            /// <summary>The start of the complete startup request.</summary>
            internal double? StartedAt;

            /// <summary>The start of server session creation.</summary>
            internal double? SessionStartedAt;

            /// <summary>The completion of server session creation.</summary>
            internal double? SessionFinishedAt;

            /// <summary>The start of RTC room join.</summary>
            internal double? RoomStartedAt;

            /// <summary>The completion of RTC room join.</summary>
            internal double? RoomFinishedAt;

            /// <summary>The start of local stream publication.</summary>
            internal double? PublicationStartedAt;

            /// <summary>The completion of the native publication request.</summary>
            internal double? PublicationFinishedAt;

            /// <summary>The start of generation signaling.</summary>
            internal double? SignalStartedAt;

            /// <summary>The first valid frame becoming ready for display.</summary>
            internal double? FirstFrameAt;

            /// <summary>The completion of generation startup.</summary>
            internal double? ReadyAt;

            /// <summary>The time startup fails.</summary>
            internal double? FailureAt;

            /// <summary>The generation task associated with the measurement.</summary>
            internal string TaskId;

            /// <summary>Whether a startup measurement exists.</summary>
            internal bool Active => StartedAt.HasValue;
        }
    }
}
