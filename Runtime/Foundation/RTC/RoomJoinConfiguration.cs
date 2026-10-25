//
//  RoomJoinConfiguration.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>The server-issued room credentials and expected result account for the fixed Agora application.</summary>
    internal sealed class RoomJoinConfiguration
    {

        /// <summary>The server application identifier, required to match the SDK application.</summary>
        internal string AppId;

        /// <summary>The RTC channel identifier.</summary>
        internal string RoomId;

        /// <summary>The string application account used to join RTC.</summary>
        internal string UserId;

        /// <summary>The server-issued RTC token; never include it in logs.</summary>
        internal string Token;

        /// <summary>The required server-assigned account identifying the result stream.</summary>
        internal string BotName;
    }

}
