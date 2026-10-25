//
//  IMediaService.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>Resolves model capabilities and independent media, generation, and return dimensions.</summary>
    internal interface IMediaService
    {

        /// <summary>Returns the immutable sizing rules for a supported model.</summary>
        RealtimeModelCapabilities GetCapabilities(
            RealtimeModel model
        );

        /// <summary>Resolves four-aligned local dimensions within model area limits.</summary>
        RealtimeVideoSize ResolveMediaSize(
            RealtimeModel model,
            int width,
            int height,
            RealtimeMediaSource source = RealtimeMediaSource.External
        );

        /// <summary>Selects the generation canvas closest to the media aspect ratio.</summary>
        RealtimeVideoSize ResolveGenerationSize(
            RealtimeModel model,
            int width,
            int height
        );

        /// <summary>Resolves four-aligned returned dimensions within bounds without upscaling.</summary>
        RealtimeVideoSize ResolveTargetSize(
            int width,
            int height,
            double maximumWidth,
            double maximumHeight
        );

        /// <summary>Resolves an external media baseline while retaining the requested frame rate.</summary>
        RealtimeVideoFormat RecommendVideoFormat(
            RealtimeModel model,
            int sourceWidth,
            int sourceHeight,
            int fps = 0
        );
    }
}
