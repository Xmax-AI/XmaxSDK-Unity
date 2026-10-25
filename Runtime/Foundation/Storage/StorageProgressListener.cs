//
//  StorageProgressListener.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>Receives completed bytes and expected bytes, with zero representing an unknown length.</summary>
    internal delegate void StorageProgressListener(
        long completedBytes,
        long totalBytes
    );
}
