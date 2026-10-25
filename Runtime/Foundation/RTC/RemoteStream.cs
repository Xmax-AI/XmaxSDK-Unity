//
//  RemoteStream.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;

namespace Xmax.SDK
{

    /// <summary>Identifies a remote main video stream by room and application account.</summary>
    internal readonly struct RemoteStream : IEquatable<RemoteStream>
    {

        /// <summary>The RTC room identifier used to reject callbacks from other rooms.</summary>
        internal readonly string RoomId;

        /// <summary>The application account publishing the remote video stream.</summary>
        internal readonly string UserId;

        /// <summary>A display identifier; equality compares the original room and user fields.</summary>
        internal string Key => $"{RoomId}:{UserId}";

        /// <summary>Creates a remote stream identity.</summary>
        internal RemoteStream(
            string roomId,
            string userId
        )
        {
            RoomId = roomId;
            UserId = userId;
        }

        /// <summary>Compares both the room identifier and application account.</summary>
        public bool Equals(
            RemoteStream other
        ) => RoomId == other.RoomId && UserId == other.UserId;

        /// <summary>Compares both the room identifier and application account.</summary>
        public override bool Equals(
            object other
        ) => other is RemoteStream stream && Equals(stream);

        /// <summary>Returns a hash consistent with room and account equality.</summary>
        public override int GetHashCode() => (RoomId ?? "").GetHashCode() ^ (UserId ?? "").GetHashCode();
    }
}
