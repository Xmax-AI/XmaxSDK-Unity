//
//  XmaxDownloadedFile.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>A completed local download.</summary>
    public sealed class XmaxDownloadedFile
    {

        /// <summary>The absolute path of the committed local file.</summary>
        public string FilePath { get; }

        /// <summary>The completed file size, in bytes.</summary>
        public long ByteCount { get; }

        /// <summary>Creates a completed local download.</summary>
        internal XmaxDownloadedFile(
            string filePath,
            long byteCount
        )
        {
            FilePath = filePath;
            ByteCount = byteCount;
        }
    }
}
