//
//  IMediaController.cs
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

    /// <summary>Owns local external media and baseline frame normalization.</summary>
    internal interface IMediaController
    {

        /// <summary>The current valid local stream, or null after closing.</summary>
        RealtimeMediaStream LocalStream { get; }

        /// <summary>Occurs when the current external video binding fails and local media must be released.</summary>
        event Action<XmaxException> FatalError;

        /// <summary>Binds and starts a host-owned video source using the resolved media baseline.</summary>
        Task<RealtimeMediaStream> CreateExternalStreamAsync(
            IRealtimeExternalVideoSource source,
            RealtimeVideoFormat format,
            CancellationToken token
        );

        /// <summary>Creates a normalized external stream and invalidates the previous stream.</summary>
        RealtimeMediaStream CreateExternalStream(
            RealtimeVideoFormat sourceFormat,
            RealtimeVideoFormat format
        );

        /// <summary>Normalizes direct input to the stable local media baseline.</summary>
        XmaxVideoFrame NormalizeFrame(
            XmaxVideoFrame frame
        );

        /// <summary>Returns the owned stream baseline and rejects foreign or replaced streams.</summary>
        RealtimeVideoFormat RequireOwned(
            RealtimeMediaStream stream
        );

        /// <summary>Invalidates local frame delivery and awaits bound source shutdown.</summary>
        Task CloseAsync();
    }
}
