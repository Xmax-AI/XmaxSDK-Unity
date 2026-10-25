//
//  RealtimeSession.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>A server session response and its resolved RTC connection credentials.</summary>
    internal sealed class XmaxSession
    {

        /// <summary>The server session identifier used for renewal and closure.</summary>
        internal string SessionUid;

        /// <summary>The server application user identifier, independent of the RTC room account.</summary>
        internal string UserUid;

        /// <summary>The server lifecycle status; heartbeat expects ACTIVE while connected.</summary>
        internal string Status;

        /// <summary>The server-provided reason the session was closed, when available.</summary>
        internal string CloseReason;

        /// <summary>The resolved RTC connection credentials, or null when the response has no valid RTC payload.</summary>
        internal RoomJoinConfiguration JoinInfo;
    }
}
