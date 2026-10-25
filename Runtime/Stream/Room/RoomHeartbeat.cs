//
//  RoomHeartbeat.cs
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

    /// <summary>Sends periodic room signaling until the connection is stopped or fails.</summary>
    internal sealed class RoomHeartbeat
    {

        /// <summary>The transport submitting ordered heartbeat messages.</summary>
        private readonly IRtcManager _rtc;

        /// <summary>The cancellation source owning the current heartbeat loop.</summary>
        private CancellationTokenSource _cancellation;

        /// <summary>The observed heartbeat loop, completed when inactive.</summary>
        private Task _loop = Task.CompletedTask;

        /// <summary>Creates a heartbeat sender for one room controller.</summary>
        internal RoomHeartbeat(
            IRtcManager rtc
        )
        {
            _rtc = rtc;
        }

        /// <summary>Starts heartbeat for the joined account after the preceding loop has stopped.</summary>
        internal void Start(
            string userId
        )
        {
            if (_cancellation != null)
                throw new InvalidOperationException("Stop the previous room heartbeat before starting another.");
            _cancellation = new CancellationTokenSource();
            _loop = RunAsync(
                userId,
                _cancellation.Token
            );
        }

        /// <summary>Stops scheduling heartbeats and waits for the current cycle to exit.</summary>
        internal async Task StopAsync()
        {
            var cancellation = _cancellation;
            if (cancellation == null)
                return;
            cancellation.Cancel();
            try
            {
                await _loop;
            }
            finally
            {
                if (ReferenceEquals(
                    _cancellation,
                    cancellation
                ))
                    _cancellation = null;
                cancellation.Dispose();
            }
        }

        /// <summary>Retries admission failures on the next cycle; admitted native failures belong to RTC.</summary>
        private async Task RunAsync(
            string userId,
            CancellationToken token
        )
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(
                        10000,
                        token
                    );
                    token.ThrowIfCancellationRequested();
                    var sending = _rtc.SendRoomMessageAsync(
                        RtcRoomEvent.Heartbeat(userId),
                        token
                    );
                    _ = RtcMessageQueue.ObserveCompletionAsync(sending);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    if (token.IsCancellationRequested)
                        return;
                    XmaxLogger.Room.Error(() => "发送 RTC 房间心跳失败 (Failed to Send RTC Room Heartbeat)\n└─ "
                        + XmaxLogger.Localized(
                            "原因：",
                            "Reason: "
                        ) + ErrorMessageFormatter.Format(exception));
                }
            }
        }
    }
}
