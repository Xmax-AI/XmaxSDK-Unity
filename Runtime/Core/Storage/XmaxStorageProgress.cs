//
//  XmaxStorageProgress.cs
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

    /// <summary>The byte progress of a storage transfer; TotalBytes is zero when its length is unknown.</summary>
    public readonly struct XmaxStorageProgress
    {

        /// <summary>The bytes transferred so far.</summary>
        public long CompletedBytes { get; }

        /// <summary>The expected transfer size, or zero when unknown.</summary>
        public long TotalBytes { get; }

        /// <summary>The completed fraction in the range zero to one, or null when the size is unknown.</summary>
        public double? FractionCompleted => TotalBytes > 0
            ? Math.Min(1, (double)CompletedBytes / TotalBytes) : (double?)null;

        /// <summary>Creates a nonnegative byte progress snapshot.</summary>
        internal XmaxStorageProgress(
            long completedBytes,
            long totalBytes
        )
        {
            CompletedBytes = Math.Max(0, completedBytes);
            TotalBytes = Math.Max(0, totalBytes);
        }
    }

    /// <summary>Receives transfer progress on the Unity main thread; callback failures are isolated.</summary>
    public delegate void XmaxStorageProgressHandler(
        XmaxStorageProgress progress
    );
}
