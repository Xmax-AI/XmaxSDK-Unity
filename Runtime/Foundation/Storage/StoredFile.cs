//
//  StoredFile.cs
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

    /// <summary>A successfully uploaded object.</summary>
    internal sealed class StoredFile
    {

        /// <summary>The resolved remote URL.</summary>
        internal Uri Url { get; }

        /// <summary>The key assigned to the uploaded object.</summary>
        internal string ObjectKey { get; }

        /// <summary>The server entity tag, or null when unavailable.</summary>
        internal string ETag { get; }

        /// <summary>Creates a successfully uploaded object.</summary>
        internal StoredFile(
            Uri url,
            string objectKey,
            string eTag
        )
        {
            Url = url;
            ObjectKey = objectKey;
            ETag = eTag;
        }
    }
}
