//
//  RtcManager.cs
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
using UnityEngine;

namespace Xmax.SDK
{

    /// <summary>Coordinates native engine leases, room join waits, and callback validity.</summary>
    internal sealed class RtcManager : IRtcManager
    {

        /// <summary>The platform factory allowing native implementation replacement.</summary>
        private readonly Func<IRtcPlatform> _makePlatform;

        /// <summary>The exclusively leased native engine adapter.</summary>
        private IRtcPlatform _platform;

        /// <summary>The room served by the active platform.</summary>
        private IRtcRoom _room;

        /// <summary>The process-wide native engine lease.</summary>
        private IDisposable _lease;

        /// <summary>The pending room join wait.</summary>
        private TaskCompletionSource<bool> _joined;

        /// <summary>The connection revision rejecting callbacks from released rooms.</summary>
        private long _revision;

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

        /// <summary>Creates a transport coordinator using the configured native platform factory.</summary>
        internal RtcManager(
            Func<IRtcPlatform> makePlatform = null
        )
        {
            _makePlatform = makePlatform ?? RtcEngineManager.CreatePlatform;
        }

        /// <inheritdoc/>
        public void ValidatePlatform()
        {
            if (Application.platform != RuntimePlatform.Android)
                throw new XmaxException(
                    XmaxErrorCode.NotSupported,
                    "XmaxSDK RTC supports Android Player only."
                );
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Local preview remains available in the Editor without loading native RTC.
            if (Application.platform != RuntimePlatform.Android || _platform != null)
                return;
            if (SynchronizationContext.Current == null)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Initialize RTC on the Unity main thread."
                );

            var lease = await RtcEngineManager.AcquireAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _platform = _makePlatform();
                _lease = lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task JoinAsync(
            RoomJoinConfiguration info,
            VideoEncodingConfiguration configuration,
            CancellationToken cancellationToken
        )
        {
            ValidatePlatform();
            await LeaveAsync();
            await InitializeAsync(cancellationToken);
            var revision = ++_revision;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _platform.ConfigureVideoEncoding(configuration);
                var room = _room = _platform.CreateRoom();

                _joined = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                room.Joined += () =>
                {
                    if (IsCurrent(
                        room,
                        revision
                    ))
                        _joined?.TrySetResult(true);
                };
                room.FatalError += error =>
                {
                    if (IsCurrent(
                        room,
                        revision
                    ))
                        Fail(error);
                };
                room.NetworkQualityChanged += quality =>
                {
                    if (IsCurrent(
                        room,
                        revision
                    ))
                        EventDispatch.Raise(
                            NetworkQualityChanged,
                            quality
                        );
                };
                room.UplinkVideoStatisticsChanged += statistics =>
                {
                    if (IsCurrent(
                        room,
                        revision
                    ))
                        EventDispatch.Raise(
                            UplinkVideoStatisticsChanged,
                            statistics
                        );
                };
                room.DownlinkVideoStatisticsChanged += statistics =>
                {
                    if (IsCurrent(
                        room,
                        revision
                    ))
                        EventDispatch.Raise(
                            DownlinkVideoStatisticsChanged,
                            statistics
                        );
                };
                room.PerformanceAlarm += alarm =>
                {
                    if (IsCurrent(
                        room,
                        revision
                    ))
                        EventDispatch.Raise(
                            PerformanceAlarm,
                            alarm
                        );
                };
                room.VideoPublished += stream =>
                {
                    if (IsCurrent(
                        room,
                        revision
                    ))
                        EventDispatch.Raise(
                            VideoPublished,
                            stream
                        );
                };
                room.VideoUnpublished += stream =>
                {
                    if (IsCurrent(
                        room,
                        revision
                    ))
                        EventDispatch.Raise(
                            VideoUnpublished,
                            stream
                        );
                };
                room.FrameReceived += (
                    stream,
                    frame
                ) =>
                {
                    if (IsCurrent(
                        room,
                        revision
                    ))
                        EventDispatch.Raise(
                            FrameReceived,
                            stream,
                            frame
                        );
                };
                room.Join(info);
                await AsyncDeadline.WaitAsync(
                    _joined.Task,
                    15000,
                    cancellationToken
                );
                cancellationToken.ThrowIfCancellationRequested();
                room.BeginSignaling();
            }
            catch
            {
                await LeaveAsync();
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task LeaveAsync()
        {
            ++_revision;
            _joined?.TrySetCanceled();
            _joined = null;
            var room = _room;
            _room = null;
            if (room != null)
                await room.LeaveAsync();
        }

        /// <inheritdoc/>
        public async Task DestroyAsync()
        {
            var platform = _platform;
            var lease = _lease;
            try
            {
                await LeaveAsync();
            }
            finally
            {
                _platform = null;
                _lease = null;
                try
                {
                    platform?.Dispose();
                }
                finally
                {
                    lease?.Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public void ConfigureVideoEncoding(
            VideoEncodingConfiguration configuration
        )
        {
            RequireRoom();
            _platform.ConfigureVideoEncoding(configuration);
        }

        /// <inheritdoc/>
        public void PushFrame(
            XmaxVideoFrame frame
        )
        {
            RequireRoom();
            _platform.PushFrame(frame);
        }

        /// <inheritdoc/>
        public void SubscribeVideo(
            RemoteStream key
        ) => RequireRoom().SubscribeVideo(key);

        /// <inheritdoc/>
        public Task SendRoomMessageAsync(
            string message,
            CancellationToken cancellationToken = default,
            string replacingPendingMessage = null,
            bool reportFailure = true
        ) =>
            RequireRoom().SendRoomMessageAsync(
                message,
                cancellationToken,
                replacingPendingMessage,
                reportFailure
            );

        /// <inheritdoc/>
        public void DiscardPendingTracks() => _room?.DiscardPendingTracks();

        /// <inheritdoc/>
        public void PublishLocalVideo() => RequireRoom().PublishLocalVideo();

        /// <summary>Rejects operations without an initialized room.</summary>
        private IRtcRoom RequireRoom() => _room ?? throw new XmaxException(
            XmaxErrorCode.RtcError,
            "RTC room is not joined."
        );

        /// <summary>Checks callback ownership without copying or deferring borrowed frame buffers.</summary>
        private bool IsCurrent(
            IRtcRoom room,
            long revision
        ) => revision == _revision && ReferenceEquals(
            room,
            _room
        );

        /// <summary>Completes pending join failure or forwards an established-room failure.</summary>
        private void Fail(
            XmaxException error
        )
        {
            if (_joined != null && !_joined.Task.IsCompleted)
                _joined.TrySetException(error);
            else
                EventDispatch.Raise(
                    FatalError,
                    error
                );
        }
    }
}
