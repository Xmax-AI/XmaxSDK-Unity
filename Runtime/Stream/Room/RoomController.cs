//
//  RoomController.cs
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

    /// <summary>Owns room connection, wire messages, and heartbeat separately from result confirmation.</summary>
    internal sealed class RoomController : IRoomController
    {

        /// <summary>The vendor-independent RTC connection and signaling transport.</summary>
        private readonly IRtcManager _rtc;

        /// <summary>The heartbeat bound to this room's lifetime.</summary>
        private readonly RoomHeartbeat _heartbeat;

        /// <summary>The current room credentials, available while joining and connected.</summary>
        private RoomJoinConfiguration _info;

        /// <summary>The stable media dimensions used to resolve input containment within the generation canvas.</summary>
        private RealtimeVideoFormat _baseline;

        /// <summary>The independent model generation canvas.</summary>
        private RealtimeVideoFormat _generation;

        /// <summary>The most recently submitted return dimensions.</summary>
        private RealtimeVideoFormat _target;

        /// <inheritdoc/>
        public bool IsConnected { get; private set; }

        /// <summary>Creates a room controller with ordered signaling and independent heartbeat.</summary>
        internal RoomController(
            IRtcManager rtc
        )
        {
            _rtc = rtc;
            _heartbeat = new RoomHeartbeat(rtc);
        }

        /// <inheritdoc/>
        public void ConfigureMedia(
            RealtimeVideoFormat baseline,
            RealtimeVideoFormat generation,
            RealtimeVideoFormat target
        )
        {
            _baseline = baseline;
            _generation = generation;
            _target = target;
        }

        /// <inheritdoc/>
        public async Task ConnectAsync(
            RoomJoinConfiguration info,
            RealtimeVideoFormat format,
            CancellationToken token
        )
        {
            _info = info;
            await _rtc.JoinAsync(
                info,
                EncodingBitratePolicy.Resolve(format),
                token
            );
            token.ThrowIfCancellationRequested();
            IsConnected = true;
            _heartbeat.Start(info.UserId);
        }

        /// <inheritdoc/>
        public async Task DisconnectAsync()
        {
            IsConnected = false;
            _info = null;
            try
            {
                await _heartbeat.StopAsync();
            }
            finally
            {
                await _rtc.LeaveAsync();
            }
        }

        /// <inheritdoc/>
        public Task StopHeartbeatAsync() => _heartbeat.StopAsync();

        /// <inheritdoc/>
        public bool Accept(
            RemoteStream stream
        ) => _info != null && stream.RoomId == _info.RoomId && stream.UserId == _info.BotName;

        /// <inheritdoc/>
        public Task StartGenerationAsync(
            string taskId,
            RealtimeContext context,
            RealtimeVideoFormat format,
            CancellationToken token,
            bool faceRestoration = false
        )
        {
            RequireConnection();
            _rtc.DiscardPendingTracks();
            var canvas = _generation.Width > 0 ? _generation : format;
            return Send(
                RtcRoomEvent.Start(
                    _info.UserId,
                    taskId,
                    canvas,
                    _target.Width > 0 ? _target : format,
                    context,
                    NeedsPadding(canvas),
                    faceRestoration
                ),
                token,
                reportFailure: false
            );
        }

        /// <inheritdoc/>
        public Task ChangeConditionAsync(
            string taskId,
            RealtimeContext context,
            RealtimeVideoFormat format,
            CancellationToken token,
            bool faceRestoration = false
        )
        {
            token.ThrowIfCancellationRequested();
            RequireConnection();
            _rtc.DiscardPendingTracks();
            var canvas = _generation.Width > 0 ? _generation : format;
            var sending = Send(
                RtcRoomEvent.ChangeCondition(
                    _info.UserId,
                    taskId,
                    canvas,
                    context,
                    NeedsPadding(canvas),
                    faceRestoration
                ),
                CancellationToken.None
            );
            _ = RtcMessageQueue.ObserveCompletionAsync(sending);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task ChangeTargetSizeAsync(
            string taskId,
            RealtimeVideoFormat format,
            CancellationToken token
        )
        {
            RequireConnection();
            await Send(
                RtcRoomEvent.ChangeTargetSize(
                    _info.UserId,
                    taskId,
                    format
                ),
                token,
                reportFailure: false
            );
            token.ThrowIfCancellationRequested();
        }

        /// <inheritdoc/>
        public void CommitTargetSize(
            RealtimeVideoFormat format
        ) => _target = format;

        /// <inheritdoc/>
        public async Task StopGenerationAsync(
            string taskId
        )
        {
            _rtc.DiscardPendingTracks();
            if (taskId == null || _info == null)
                return;
            try
            {
                await Send(
                    RtcRoomEvent.Stop(
                        _info.UserId,
                        taskId
                    ),
                    reportFailure: false
                );
            }
            catch (Exception exception)
            {
                XmaxLogger.Room.Error(() => "发送房间信令失败 (Room Signaling Failed)\n└─ "
                    + XmaxLogger.Localized(
                        "原因：",
                        "Reason: "
                    ) + ErrorMessageFormatter.Format(exception));
            }
        }

        /// <inheritdoc/>
        public void SendTracks(
            string taskId,
            IReadOnlyList<RealtimePoint> tracks
        )
        {
            RequireConnection();
            var sending = Send(
                RtcRoomEvent.Tracks(
                    _info.UserId,
                    taskId,
                    tracks
                ),
                replacingPendingMessage: "tracks:" + taskId
            );
            _ = ObserveTracksAsync(sending);
        }

        /// <summary>Submits one logical signal and logs its event and formatted contents.</summary>
        private Task Send(
            string message,
            CancellationToken cancellationToken = default,
            string replacingPendingMessage = null,
            bool reportFailure = true
        )
        {
            var sending = _rtc.SendRoomMessageAsync(
                message,
                cancellationToken,
                replacingPendingMessage,
                reportFailure
            );
            XmaxLogger.Room.Debug(() => FormatSignalLog(message));
            return sending;
        }

        /// <summary>Formats a complete outbound signal with localized labels.</summary>
        private static string FormatSignalLog(
            string message
        )
        {
            var eventType = "unknown";
            try
            {
                var value = JsonCodec.Decode(message);
                if (value != null && value.ContainsKey("event") && value["event"] != null && value["event"].IsString)
                    eventType = (string)value["event"];
            }
            catch
            {
            }
            return "发送房间信令 (Outbound Room Signaling)\n"
                + "├─ " + XmaxLogger.Localized(
                    "类型：",
                    "Type: "
                ) + eventType + "\n"
                + "└─ " + XmaxLogger.Localized(
                    "内容：",
                    "Content: "
                ) + "\n   " + JsonCodec.FormatForLogging(message).Replace(
                    "\n",
                    "\n   "
                );
        }

        /// <summary>Observes coalesced trajectory completion without an async-void path.</summary>
        private static async Task ObserveTracksAsync(
            Task sending
        )
        {
            try
            {
                await sending;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                XmaxLogger.Interaction.Warn(() => "发送交互轨迹失败，已丢弃当前采样帧 (Failed to Send Interaction Trajectory; Current Sample Dropped)\n└─ "
                    + XmaxLogger.Localized(
                        "原因：",
                        "Reason: "
                    ) + ErrorMessageFormatter.Format(exception));
            }
        }

        /// <summary>Checks whether containment is needed between media and canvas aspect ratios.</summary>
        private bool NeedsPadding(
            RealtimeVideoFormat canvas
        ) => _baseline.Width > 0 &&
            (long)_baseline.Width * canvas.Height != (long)_baseline.Height * canvas.Width;

        /// <summary>Rejects signaling outside an established room connection.</summary>
        private void RequireConnection()
        {
            if (!IsConnected || _info == null)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "RTC room is not joined."
                );
        }
    }
}
