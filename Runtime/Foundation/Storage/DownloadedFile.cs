//
//  DownloadedFile.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>A downloaded file committed at its destination.</summary>
    internal sealed class DownloadedFile
    {

        /// <summary>The absolute destination path.</summary>
        internal string FilePath { get; }

        /// <summary>The committed file size, in bytes.</summary>
        internal long ByteCount { get; }

        /// <summary>Creates a downloaded file committed at its destination.</summary>
        internal DownloadedFile(
            string filePath,
            long byteCount
        )
        {
            FilePath = filePath;
            ByteCount = byteCount;
        }
    }
}
