//
//  IStorageService.cs
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

    /// <summary>The media category selecting MIME validation and storage business behavior.</summary>
    internal enum StorageMediaType
    {

        /// <summary>An image object, optionally checked for safety after upload.</summary>
        Image,

        /// <summary>A video object.</summary>
        Video
    }

    /// <summary>Coordinates upload validation, temporary credentials, object names, and image safety checks.</summary>
    internal interface IStorageService
    {

        /// <summary>Uploads validated media and optionally returns the safety service's image URL.</summary>
        Task<StoredFile> UploadAsync(
            StorageUploadSource source,
            string fileName,
            string contentType,
            StorageMediaType mediaType,
            bool checksSafety,
            StorageProgressListener progress,
            CancellationToken cancellationToken
        );

        /// <summary>Validates and downloads an HTTP object to a local file.</summary>
        Task<DownloadedFile> DownloadAsync(
            Uri remoteUrl,
            string destinationPath,
            StorageProgressListener progress,
            CancellationToken cancellationToken
        );
    }
}
