//
//  IRealtimeExternalVideoSource.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>A host-owned video source whose frame delivery is bound to a local realtime stream.</summary>
    ///
    /// <remarks>Start and stop run on the creating Unity main thread. Capture devices and players remain owned by your app.</remarks>
    public interface IRealtimeExternalVideoSource
    {

        /// <summary>The source dimensions and encoding options, unchanged until frame delivery stops.</summary>
        RealtimeVideoFormat VideoFormat { get; }

        /// <summary>Installs frame delivery and completes when the source is ready, without waiting for generation.</summary>
        ///
        /// <remarks>Supply upright frames through the sink. Honor cancellation; startup failure or cancellation is followed by StopAsync.</remarks>
        Task StartAsync(
            IRealtimeExternalFrameSink sink,
            CancellationToken cancellationToken = default
        );

        /// <summary>Stops frame delivery and releases the sink reference; repeated calls must be safe.</summary>
        Task StopAsync();
    }
}
