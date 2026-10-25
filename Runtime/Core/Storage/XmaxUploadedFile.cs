//
//  XmaxUploadedFile.cs
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

    /// <summary>A stored object address, key, and optional entity tag.</summary>
    public sealed class XmaxUploadedFile
    {

        /// <summary>The remote object URL, including any URL returned by image safety checking.</summary>
        public Uri Url { get; }

        /// <summary>The object key assigned within the storage bucket.</summary>
        public string ObjectKey { get; }

        /// <summary>The response entity tag, or null when unavailable.</summary>
        public string ETag { get; }

        /// <summary>Creates a stored object address, key, and optional entity tag.</summary>
        internal XmaxUploadedFile(
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
