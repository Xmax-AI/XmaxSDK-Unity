//
//  RtcRoomEventBridge.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using Agora.Rtc;

namespace Xmax.SDK
{

    /// <summary>Marshals Agora callbacks into connection-scoped neutral room events.</summary>
    internal sealed class RtcRoomEventBridge : IRtcEngineEventHandler
    {

        /// <summary>
        /// The owning RTC adapter.
        /// </summary>
        private readonly AgoraRtcRoom _owner;

        /// <summary>
        /// The connection revision captured during registration.
        /// </summary>
        private readonly long _revision;

        /// <summary>
        /// Creates a connection-scoped callback adapter.
        /// </summary>
        internal RtcRoomEventBridge(
            AgoraRtcRoom owner,
            long revision
        )
        {
            _owner = owner;
            _revision = revision;
        }

        /// <inheritdoc/>
        public override void OnJoinChannelSuccess(
            RtcConnection connection,
            int elapsed
        ) =>
            _owner.Dispatch(
                _revision,
                connection,
                () => _owner.NotifyJoined()
            );

        /// <inheritdoc/>
        public override void OnError(
            int err,
            string msg
        ) =>
            _owner.Dispatch(
                _revision,
                () => XmaxLogger.Rtc.Error(() => "声网 RTC 错误 (Agora RTC Error)\n└─ " + err)
            );

        /// <inheritdoc/>
        public override void OnUserInfoUpdated(
            uint uid,
            UserInfo info
        )
        {
            var account = info.userAccount;
            _owner.Dispatch(
                _revision,
                () =>
                {
                    _owner.SetAccount(
                        uid,
                        account
                    );
                }
            );
        }

        /// <inheritdoc/>
        public override void OnUserJoined(
            RtcConnection connection,
            uint remoteUid,
            int elapsed
        ) =>
            _owner.Dispatch(
                _revision,
                connection,
                () => _owner.Announce(remoteUid)
            );

        /// <inheritdoc/>
        public override void OnUserStateChanged(
            RtcConnection connection,
            uint remoteUid,
            uint state
        ) =>
            Publication(
                connection,
                remoteUid,
                (state & 2) == 0 && (state & 16) != 0 && (state & 256) != 0
            );

        /// <inheritdoc/>
        public override void OnUserMuteVideo(
            RtcConnection connection,
            uint remoteUid,
            bool muted
        ) => Publication(
            connection,
            remoteUid,
            !muted
        );

        /// <inheritdoc/>
        public override void OnUserEnableVideo(
            RtcConnection connection,
            uint remoteUid,
            bool enabled
        ) => Publication(
            connection,
            remoteUid,
            enabled
        );

        /// <inheritdoc/>
        public override void OnUserEnableLocalVideo(
            RtcConnection connection,
            uint remoteUid,
            bool enabled
        ) => Publication(
            connection,
            remoteUid,
            enabled
        );

        /// <inheritdoc/>
        public override void OnRemoteVideoStateChanged(
            RtcConnection connection,
            uint remoteUid,
            REMOTE_VIDEO_STATE state,
            REMOTE_VIDEO_STATE_REASON reason,
            int elapsed
        )
        {
            if (state == REMOTE_VIDEO_STATE.REMOTE_VIDEO_STATE_STARTING ||
                state == REMOTE_VIDEO_STATE.REMOTE_VIDEO_STATE_DECODING)
                Publication(
                    connection,
                    remoteUid,
                    true
                );
            else if (reason == REMOTE_VIDEO_STATE_REASON.REMOTE_VIDEO_STATE_REASON_REMOTE_MUTED ||
                reason == REMOTE_VIDEO_STATE_REASON.REMOTE_VIDEO_STATE_REASON_REMOTE_OFFLINE)
                Publication(
                    connection,
                    remoteUid,
                    false
                );
        }

        /// <inheritdoc/>
        public override void OnRequestToken(
            RtcConnection connection
        ) => _owner.Dispatch(
            _revision,
            connection,
            () => _owner.Fail(new XmaxException(
                    XmaxErrorCode.RtcError,
                    "The RTC room token has expired. Reconnect to obtain a new session."
                ))
        );

        /// <inheritdoc/>
        public override void OnUserOffline(
            RtcConnection connection,
            uint remoteUid,
            USER_OFFLINE_REASON_TYPE reason
        ) => Publication(
            connection,
            remoteUid,
            false
        );

        /// <summary>
        /// Records publication before attempting account resolution.
        /// </summary>
        private void Publication(
            RtcConnection connection,
            uint uid,
            bool published
        ) => _owner.Dispatch(
            _revision,
            connection,
            () =>
            {
                _owner.SetPublication(
                    uid,
                    published
                );
            }
        );

        /// <inheritdoc/>
        public override void OnNetworkQuality(
            RtcConnection connection,
            uint remoteUid,
            int txQuality,
            int rxQuality
        )
        {
            if (remoteUid == 0 || remoteUid == connection.localUid)
                _owner.Dispatch(
                    _revision,
                    connection,
                    () => _owner.NotifyQuality(
                        RtcQualityConverter.Convert(
                            txQuality,
                            rxQuality
                        )
                    )
                );
        }

        /// <inheritdoc/>
        public override void OnLocalVideoStats(
            RtcConnection connection,
            VIDEO_SOURCE_TYPE sourceType,
            LocalVideoStats stats
        )
        {
            if (stats == null)
                return;
            var statistics = RtcQualityConverter.UplinkVideoStatistics(stats);
            var adaptation = (int)stats.qualityAdaptIndication;
            var alarm = new RtcPerformanceAlarm(
                adaptation == 2,
                stats.encodedFrameWidth,
                stats.encodedFrameHeight,
                stats.encoderOutputFrameRate
            );
            _owner.Dispatch(
                _revision,
                connection,
                () =>
                {
                    _owner.NotifyUplinkStatistics(statistics);
                    if (adaptation == 1 || adaptation == 2)
                        _owner.NotifyPerformanceAlarm(alarm);
                }
            );
        }

        /// <inheritdoc/>
        public override void OnRemoteVideoStats(
            RtcConnection connection,
            RemoteVideoStats stats
        )
        {
            if (stats == null)
                return;
            var uid = stats.uid;
            var statistics = RtcQualityConverter.DownlinkVideoStatistics(stats);
            _owner.Dispatch(
                _revision,
                connection,
                () => _owner.NotifyDownlinkStatistics(
                    uid,
                    statistics
                )
            );
        }

        /// <inheritdoc/>
        public override void OnConnectionStateChanged(
            RtcConnection connection,
            CONNECTION_STATE_TYPE state,
            CONNECTION_CHANGED_REASON_TYPE reason
        )
        {
            if (state == CONNECTION_STATE_TYPE.CONNECTION_STATE_FAILED)
                _owner.Dispatch(
                    _revision,
                    connection,
                    () => _owner.Fail(new XmaxException(
                            XmaxErrorCode.RtcError,
                            "Agora connection failed: " + (int)reason + "."
                        ))
                );
        }
    }
}
