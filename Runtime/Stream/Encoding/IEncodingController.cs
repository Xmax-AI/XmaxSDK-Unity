//
//  IEncodingController.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>Applies uplink encoding independently from room signaling and quality sampling.</summary>
    internal interface IEncodingController
    {

        /// <summary>Resolves and applies dimensions, frame rate, bitrate bounds, and degradation preference.</summary>
        void Configure(
            RealtimeVideoFormat format
        );
    }
}
