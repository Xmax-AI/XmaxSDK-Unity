//
//  IRealtimeExternalFrameSink.cs
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

    /// <summary>A thread-safe receiver for one external video source binding.</summary>
    public interface IRealtimeExternalFrameSink
    {

        /// <summary>The resolved media baseline used for preview, transmission, and interaction coordinates.</summary>
        RealtimeVideoFormat VideoFormat { get; }

        /// <summary>Whether this binding accepts frames; failure, stop, or close permanently invalidates it.</summary>
        bool IsRequestingFrames { get; }

        /// <summary>Copies a source-sized frame for delivery on Unity's main thread.</summary>
        ///
        /// <remarks>
        /// Call from any capture thread. Keep arrays unchanged during this call. Pixels must be upright, with Rotation0 metadata.
        /// Timestamps are nonnegative monotonic microseconds, strictly increasing across seeks and loops.
        /// Invalid or nonincreasing timestamps are ignored. A bounded mailbox retains only the latest pending frame.
        /// </remarks>
        void PushVideo(
            XmaxVideoFrame frame
        );

        /// <summary>Waits for an in-flight submission, invalidates the binding, and reports its first failure.</summary>
        void ReportError(
            Exception error
        );
    }
}
