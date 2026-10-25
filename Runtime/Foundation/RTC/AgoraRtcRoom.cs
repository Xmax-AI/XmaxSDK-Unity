//
//  AgoraRtcRoom.cs
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
using Agora.Rtc;

namespace Xmax.SDK
{

    /// <summary>Adapts one Agora room, account mapping, publication events, and ordered signaling.</summary>
    internal sealed class AgoraRtcRoom : IRtcRoom
    {

        /// <summary>The native engine owned by the platform adapter.</summary>
        private readonly IRtcEngine _engine;

        /// <summary>The creating Unity synchronization context.</summary>
        private readonly SynchronizationContext _context;

        /// <summary>The connection revision rejecting queued callbacks after leave.</summary>
        private long _revision;

        /// <summary>The active room credentials, cleared before native teardown.</summary>
        private RoomJoinConfiguration _info;

        /// <summary>The ordered data-stream message queue.</summary>
        private RtcMessageQueue _messages;

        /// <summary>The native data-stream identifier.</summary>
        private int _dataStream;

        /// <summary>The numeric UID to application account mapping.</summary>
        private readonly Dictionary<uint, string> _accounts = new Dictionary<uint, string>();

        /// <summary>The last observed video publication flags.</summary>
        private readonly Dictionary<uint, bool> _publications = new Dictionary<uint, bool>();

        /// <summary>The native users already announced as published.</summary>
        private readonly HashSet<uint> _announced = new HashSet<uint>();

        /// <summary>The decoded frame mailbox for this connection.</summary>
        private RtcRemoteVideoSink _video;

        /// <summary>Whether native room teardown has already started.</summary>
        private bool _closed;

        /// <inheritdoc/>
        public event Action Joined;

        /// <inheritdoc/>
        public event Action<XmaxException> FatalError;

        /// <inheritdoc/>
        public event Action<RtcNetworkQuality> NetworkQualityChanged;

        /// <summary>Occurs for copied measurements of the current local video stream.</summary>
        public event Action<UplinkVideoStatistics> UplinkVideoStatisticsChanged;

        /// <summary>Occurs for copied measurements of a resolved remote account.</summary>
        public event Action<DownlinkVideoStatistics> DownlinkVideoStatisticsChanged;

        /// <summary>Occurs when native encoding becomes limited or recovers.</summary>
        public event Action<RtcPerformanceAlarm> PerformanceAlarm;

        /// <inheritdoc/>
        public event Action<RemoteStream> VideoPublished;

        /// <inheritdoc/>
        public event Action<RemoteStream> VideoUnpublished;

        /// <inheritdoc/>
        public event Action<RemoteStream, XmaxVideoFrame> FrameReceived;

        /// <summary>Creates an unjoined room without owning native engine destruction.</summary>
        internal AgoraRtcRoom(
            IRtcEngine engine
        )
        {
            _engine = engine;
            _context = SynchronizationContext.Current ?? throw new XmaxException(
                XmaxErrorCode.InvalidConfiguration,
                "Create RTC rooms on the Unity main thread."
            );
        }

        /// <inheritdoc/>
        public void Join(
            RoomJoinConfiguration info
        )
        {
            if (_closed || _info != null)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "The native room cannot be joined again."
                );
            _info = info;
            var revision = ++_revision;
            _video = new RtcRemoteVideoSink(
                info.RoomId,
                _context,
                ReceiveFrame,
                Fail
            );
            _engine.InitEventHandler(new RtcRoomEventBridge(
                    this,
                    revision
                ));
            Check(
                _engine.RegisterVideoFrameObserver(
                    _video.Observer,
                    VIDEO_OBSERVER_FRAME_TYPE.FRAME_TYPE_YUV420,
                    VIDEO_MODULE_POSITION.POSITION_PRE_RENDERER,
                    OBSERVER_MODE.INTPTR
                ),
                "RegisterVideoFrameObserver"
            );
            var options = new ChannelMediaOptions();
            options.channelProfile.SetValue(CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_LIVE_BROADCASTING);
            options.clientRoleType.SetValue(CLIENT_ROLE_TYPE.CLIENT_ROLE_BROADCASTER);
            options.publishCameraTrack.SetValue(false);
            options.publishMicrophoneTrack.SetValue(false);
            options.publishCustomVideoTrack.SetValue(false);
            options.customVideoTrackId.SetValue(0);
            options.autoSubscribeAudio.SetValue(false);
            options.autoSubscribeVideo.SetValue(false);
            Check(
                _engine.JoinChannelWithUserAccount(
                    info.Token,
                    info.RoomId,
                    info.UserId,
                    options
                ),
                "JoinChannelWithUserAccount"
            );

        }

        /// <inheritdoc/>
        public void BeginSignaling()
        {
            var revision = _revision;
            Check(
                _engine.CreateDataStream(
                    ref _dataStream,
                    new DataStreamConfig { ordered = true, syncWithAudio = false }
                ),
                "CreateDataStream"
            );
            _messages = new RtcMessageQueue(
                packet => Check(
                    _engine.SendStreamMessage(
                        _dataStream,
                        packet,
                        (uint)packet.Length
                    ),
                    "SendStreamMessage"
                ),
                error => Dispatch(
                    revision,
                    () => Fail(error)
                )
            );
        }

        /// <inheritdoc/>
        public async Task LeaveAsync()
        {
            if (_closed)
                return;
            _closed = true;
            ++_revision;
            _info = null;
            _video?.Close();
            var messages = _messages;
            _messages = null;
            try
            {
                if (messages != null)
                    await messages.CloseAsync();
            }
            finally
            {
                try
                {
                    _engine.InitEventHandler(null);
                    _engine.UnRegisterVideoFrameObserver();
                    _engine.LeaveChannel();
                }
                finally
                {
                    _accounts.Clear();
                    _publications.Clear();
                    _announced.Clear();
                    _video = null;
                }
            }
        }

        /// <inheritdoc/>
        public void SubscribeVideo(
            RemoteStream key
        )
        {
            if (_info == null || key.RoomId != _info.RoomId)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "RTC room is not joined."
                );
            foreach (var account in _accounts)
                if (account.Value == key.UserId)
                {
                    Check(
                        _engine.MuteRemoteVideoStream(
                            account.Key,
                            false
                        ),
                        "SubscribeVideo"
                    );
                    return;
                }
            throw new XmaxException(
                XmaxErrorCode.RtcError,
                "Remote user account is unavailable."
            );
        }

        /// <inheritdoc/>
        public Task SendRoomMessageAsync(
            string message,
            CancellationToken cancellationToken = default,
            string replacingPendingMessage = null,
            bool reportFailure = true
        )
        {
            if (_messages == null)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "RTC signaling is unavailable."
                );
            return _messages.Enqueue(
                message,
                cancellationToken,
                replacingPendingMessage,
                reportFailure
            );
        }

        /// <inheritdoc/>
        public void DiscardPendingTracks() => _messages?.DiscardPendingTracks();

        /// <inheritdoc/>
        public void PublishLocalVideo()
        {
            if (_info == null || _messages == null)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "RTC room is not joined."
                );
            var options = new ChannelMediaOptions();
            options.publishCustomVideoTrack.SetValue(true);
            options.customVideoTrackId.SetValue(0);
            Check(
                _engine.UpdateChannelMediaOptions(options),
                "PublishLocalVideo"
            );
        }

        /// <summary>Logs and delivers copied uplink measurements while this room is active.</summary>
        internal void NotifyUplinkStatistics(
            UplinkVideoStatistics statistics
        )
        {
            RtcStatsLogger.LogUplinkVideoStatistics(statistics);
            EventDispatch.Raise(
                UplinkVideoStatisticsChanged,
                statistics
            );
        }

        /// <summary>Resolves an application account before logging and delivering downlink measurements.</summary>
        internal void NotifyDownlinkStatistics(
            uint uid,
            DownlinkVideoStatistics statistics
        )
        {
            Announce(uid);
            if (!_accounts.TryGetValue(
                uid,
                out var account
            ))
                return;
            var resolved = statistics.WithUserId(account);
            RtcStatsLogger.LogDownlinkVideoStatistics(resolved);
            EventDispatch.Raise(
                DownlinkVideoStatisticsChanged,
                resolved
            );
        }

        /// <summary>Delivers native encoding limitation or recovery measurements.</summary>
        internal void NotifyPerformanceAlarm(
            RtcPerformanceAlarm alarm
        ) => EventDispatch.Raise(
            PerformanceAlarm,
            alarm
        );

        /// <summary>
        /// Queues room callbacks outside native callbacks and rejects previous connections.
        /// </summary>
        internal void Dispatch(
            long revision,
            Action callback
        )
        {
            _context?.Post(
                _ =>
                {
                    if (revision == _revision && _info != null)
                        callback();
                },
                null
            );
        }

        /// <summary>Rejects callbacks whose original native connection belongs to another room.</summary>
        internal void Dispatch(
            long revision,
            RtcConnection connection,
            Action callback
        )
        {
            var channelId = connection?.channelId;
            if (string.IsNullOrEmpty(channelId))
                return;

            Dispatch(
                revision,
                () =>
                {
                    if (string.Equals(channelId, _info.RoomId, StringComparison.Ordinal))
                        callback();
                }
            );
        }

        /// <summary>
        /// Resolves the string account before announcing publication.
        /// </summary>
        internal void Announce(
            uint uid
        )
        {
            if (!_accounts.ContainsKey(uid))
            {
                var info = new UserInfo();
                if (_engine.GetUserInfoByUid(
                    uid,
                    ref info
                ) == 0 && !string.IsNullOrEmpty(info.userAccount))
                    _accounts[uid] = info.userAccount;
            }
            if (!_accounts.TryGetValue(
                uid,
                out var account
            ) || !_publications.TryGetValue(
                uid,
                out var published
            ))
                return;
            var key = new RemoteStream(
                _info.RoomId,
                account
            );
            if (published && _announced.Add(uid))
                EventDispatch.Raise(
                    VideoPublished,
                    key
                );
            else if (!published && _announced.Remove(uid))
                EventDispatch.Raise(
                    VideoUnpublished,
                    key
                );
        }

        /// <summary>Reports native join completion to the transport coordinator.</summary>
        internal void NotifyJoined() => EventDispatch.Raise(Joined);

        /// <summary>Reports a local directional quality sample.</summary>
        internal void NotifyQuality(
            RtcNetworkQuality quality
        ) => EventDispatch.Raise(
            NetworkQualityChanged,
            quality
        );

        /// <summary>Reports a native room or signaling failure.</summary>
        internal void Fail(
            XmaxException error
        ) => EventDispatch.Raise(
            FatalError,
            error
        );

        /// <summary>Resolves an application account before replaying its publication state.</summary>
        internal void SetAccount(
            uint uid,
            string account
        )
        {
            if (!string.IsNullOrEmpty(account))
                _accounts[uid] = account;
            Announce(uid);
        }

        /// <summary>Records publication state before resolving the native user account.</summary>
        internal void SetPublication(
            uint uid,
            bool published
        )
        {
            _publications[uid] = published;
            Announce(uid);
        }

        /// <summary>Delivers a decoded frame with its mapped account while owned buffers remain valid.</summary>
        private void ReceiveFrame(
            uint uid,
            XmaxVideoFrame frame
        )
        {
            if (_info == null)
                return;
            Announce(uid);
            if (_info != null && _accounts.TryGetValue(
                uid,
                out var account
            ))
                EventDispatch.Raise(
                    FrameReceived,
                    new RemoteStream(
                        _info.RoomId,
                        account
                    ),
                    frame
                );
        }

        /// <summary>
        /// Converts a negative native return code to an SDK error.
        /// </summary>
        private static void Check(
            int result,
            string operation
        )
        {
            if (result < 0)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    operation + " failed: " + result + "."
                );
        }
    }
}
