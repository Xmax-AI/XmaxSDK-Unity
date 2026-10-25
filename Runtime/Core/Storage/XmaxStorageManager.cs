//
//  XmaxStorageManager.cs
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

    /// <summary>The public storage facade adapting transfer results and byte progress for your app.</summary>
    public sealed class XmaxStorageManager : IXmaxStorageManager
    {

        /// <summary>The validated upload and download service.</summary>
        private readonly IStorageService _service;

        /// <summary>The thread on which Unity networking and progress are executed.</summary>
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;

        /// <summary>Creates a facade over the supplied storage service.</summary>
        internal XmaxStorageManager(
            IStorageService service
        )
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <inheritdoc/>
        public async Task<XmaxUploadedFile> UploadImageAsync(
            byte[] data,
            string fileName,
            string contentType,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        )
        {
            RequireThread();
            cancellationToken.ThrowIfCancellationRequested();
            var source = StorageUploadSource.FromData(data);
            var result = await _service.UploadAsync(
                source,
                fileName,
                contentType,
                StorageMediaType.Image,
                false,
                ProgressListener(progress),
                cancellationToken
            );
            return new XmaxUploadedFile(
                result.Url,
                result.ObjectKey,
                result.ETag
            );
        }

        /// <inheritdoc/>
        public async Task<XmaxUploadedFile> UploadImageAsync(
            string filePath,
            string contentType = null,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        )
        {
            RequireThread();
            cancellationToken.ThrowIfCancellationRequested();
            var source = StorageUploadSource.FromFile(filePath);
            var result = await _service.UploadAsync(
                source,
                Path.GetFileName(source.FilePath),
                contentType,
                StorageMediaType.Image,
                false,
                ProgressListener(progress),
                cancellationToken
            );
            return new XmaxUploadedFile(
                result.Url,
                result.ObjectKey,
                result.ETag
            );
        }

        /// <inheritdoc/>
        public async Task<XmaxUploadedFile> UploadImageWithSafetyCheckAsync(
            byte[] data,
            string fileName,
            string contentType,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        )
        {
            RequireThread();
            cancellationToken.ThrowIfCancellationRequested();
            var source = StorageUploadSource.FromData(data);
            var result = await _service.UploadAsync(
                source,
                fileName,
                contentType,
                StorageMediaType.Image,
                true,
                ProgressListener(progress),
                cancellationToken
            );
            return new XmaxUploadedFile(
                result.Url,
                result.ObjectKey,
                result.ETag
            );
        }

        /// <inheritdoc/>
        public async Task<XmaxUploadedFile> UploadImageWithSafetyCheckAsync(
            string filePath,
            string contentType = null,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        )
        {
            RequireThread();
            cancellationToken.ThrowIfCancellationRequested();
            var source = StorageUploadSource.FromFile(filePath);
            var result = await _service.UploadAsync(
                source,
                Path.GetFileName(source.FilePath),
                contentType,
                StorageMediaType.Image,
                true,
                ProgressListener(progress),
                cancellationToken
            );
            return new XmaxUploadedFile(
                result.Url,
                result.ObjectKey,
                result.ETag
            );
        }

        /// <inheritdoc/>
        public async Task<XmaxUploadedFile> UploadVideoAsync(
            byte[] data,
            string fileName,
            string contentType,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        )
        {
            RequireThread();
            cancellationToken.ThrowIfCancellationRequested();
            var source = StorageUploadSource.FromData(data);
            var result = await _service.UploadAsync(
                source,
                fileName,
                contentType,
                StorageMediaType.Video,
                false,
                ProgressListener(progress),
                cancellationToken
            );
            return new XmaxUploadedFile(
                result.Url,
                result.ObjectKey,
                result.ETag
            );
        }

        /// <inheritdoc/>
        public async Task<XmaxUploadedFile> UploadVideoAsync(
            string filePath,
            string contentType = null,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        )
        {
            RequireThread();
            cancellationToken.ThrowIfCancellationRequested();
            var source = StorageUploadSource.FromFile(filePath);
            var result = await _service.UploadAsync(
                source,
                Path.GetFileName(source.FilePath),
                contentType,
                StorageMediaType.Video,
                false,
                ProgressListener(progress),
                cancellationToken
            );
            return new XmaxUploadedFile(
                result.Url,
                result.ObjectKey,
                result.ETag
            );
        }

        /// <inheritdoc/>
        public async Task<XmaxDownloadedFile> DownloadImageAsync(
            Uri remoteUrl,
            string destinationPath,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        )
        {
            RequireThread();
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _service.DownloadAsync(
                remoteUrl,
                destinationPath,
                ProgressListener(progress),
                cancellationToken
            );
            return new XmaxDownloadedFile(
                result.FilePath,
                result.ByteCount
            );
        }

        /// <inheritdoc/>
        public async Task<XmaxDownloadedFile> DownloadVideoAsync(
            Uri remoteUrl,
            string destinationPath,
            XmaxStorageProgressHandler progress = null,
            CancellationToken cancellationToken = default
        )
        {
            RequireThread();
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _service.DownloadAsync(
                remoteUrl,
                destinationPath,
                ProgressListener(progress),
                cancellationToken
            );
            return new XmaxDownloadedFile(
                result.FilePath,
                result.ByteCount
            );
        }

        /// <summary>Requires the creating Unity context before networking or callback registration.</summary>
        private void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId || SynchronizationContext.Current == null)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Call storage APIs on the creating Unity main thread."
                );
        }

        /// <summary>Adapts transfer byte counts while isolating exceptions thrown by your app.</summary>
        private static StorageProgressListener ProgressListener(
            XmaxStorageProgressHandler handler
        )
        {
            if (handler == null)
                return null;
            return (
                completed,
                total
            ) =>
            {
                try
                {
                    handler(new XmaxStorageProgress(
                            completed,
                            total
                        ));
                }
                catch (Exception error)
                {
                    XmaxLogger.Storage.Warn(() => "Storage progress callback failed: " + ErrorMessageFormatter.Format(error));
                }
            };
        }
    }
}
