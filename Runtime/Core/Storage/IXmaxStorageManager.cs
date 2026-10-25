//
//  IXmaxStorageManager.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>Uploads and downloads media using the client's storage service environment.</summary>
    ///
    /// <remarks>
    /// Create and call this manager on the Unity main thread. Byte arrays and upload files must remain
    /// unchanged until completion. File uploads infer MIME type when omitted; byte uploads require it.
    /// Progress describes the transfer only; safety-checked uploads complete after the additional check.
    /// Cancellation aborts transfer and removes partial downloads without replacing an existing destination.
    /// </remarks>
    public interface IXmaxStorageManager
    {

        /// <summary>Uploads image bytes, completing after the transfer.</summary>
        Task<XmaxUploadedFile> UploadImageAsync(
            byte[] data,
            string fileName,
            string contentType,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        );

        /// <summary>Uploads an existing image file, completing after the transfer.</summary>
        Task<XmaxUploadedFile> UploadImageAsync(
            string filePath,
            string contentType = null,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        );

        /// <summary>Uploads image bytes and checks image safety before returning its URL.</summary>
        Task<XmaxUploadedFile> UploadImageWithSafetyCheckAsync(
            byte[] data,
            string fileName,
            string contentType,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        );

        /// <summary>Uploads an existing image file and checks image safety before returning its URL.</summary>
        Task<XmaxUploadedFile> UploadImageWithSafetyCheckAsync(
            string filePath,
            string contentType = null,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        );

        /// <summary>Uploads video bytes, completing after the transfer.</summary>
        Task<XmaxUploadedFile> UploadVideoAsync(
            byte[] data,
            string fileName,
            string contentType,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        );

        /// <summary>Uploads an existing video file, completing after the transfer.</summary>
        Task<XmaxUploadedFile> UploadVideoAsync(
            string filePath,
            string contentType = null,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        );

        /// <summary>Downloads image data and atomically commits the destination file.</summary>
        Task<XmaxDownloadedFile> DownloadImageAsync(
            Uri remoteUrl,
            string destinationPath,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        );

        /// <summary>Downloads video data and atomically commits the destination file.</summary>
        Task<XmaxDownloadedFile> DownloadVideoAsync(
            Uri remoteUrl,
            string destinationPath,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        );
    }
}
