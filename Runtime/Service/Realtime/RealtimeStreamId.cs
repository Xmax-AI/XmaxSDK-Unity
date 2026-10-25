//
//  RealtimeStreamId.cs
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

    /// <summary>
    /// The direction of an SDK media stream.
    /// </summary>
    public enum RealtimeStreamId
    {

        /// <summary>
        /// Generated frames received from the server.
        /// </summary>
        Remote,

        /// <summary>
        /// External frames supplied by your app.
        /// </summary>
        Local
    }
}
