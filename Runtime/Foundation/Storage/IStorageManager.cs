//
//  IStorageManager.cs
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

    /// <summary>The object storage transport, independent of API credentials and image safety policy.</summary>
    internal interface IStorageManager
    {

        /// <summary>Uploads data or a file with temporary COS credentials, completing after native transfer.</summary>
        Task<StoredFile> UploadAsync(
            StorageUploadSource source,
            string objectKey,
            string contentType,
            StorageConfiguration configuration,
            StorageProgressListener progress,
            CancellationToken cancellationToken
        );

        /// <summary>Streams into a sibling temporary file and commits it only after a successful response.</summary>
        Task<DownloadedFile> DownloadAsync(
            Uri remoteUrl,
            string destinationPath,
            StorageProgressListener progress,
            CancellationToken cancellationToken
        );
    }
}
