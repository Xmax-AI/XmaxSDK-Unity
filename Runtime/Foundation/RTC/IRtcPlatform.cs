//
//  IRtcPlatform.cs
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

    /// <summary>Isolates native engine configuration and frame input from room lifecycle coordination.</summary>
    internal interface IRtcPlatform : IDisposable
    {

        /// <summary>Applies the neutral video encoder configuration.</summary>
        void ConfigureVideoEncoding(
            VideoEncodingConfiguration configuration
        );

        /// <summary>Submits an external frame whose buffers remain valid until this call returns.</summary>
        void PushFrame(
            XmaxVideoFrame frame
        );

        /// <summary>Creates the single room adapter served by this engine.</summary>
        IRtcRoom CreateRoom();
    }
}
