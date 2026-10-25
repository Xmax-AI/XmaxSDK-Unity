//
//  StorageManager.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Xmax.SDK
{

    /// <summary>Transfers COS objects with Unity networking and commits downloads only after success.</summary>
    internal sealed class StorageManager : IStorageManager
    {

        /// <summary>The timeout of an individual upload or download request, in seconds.</summary>
        private const int TransferTimeoutSeconds = 300;

        /// <summary>The maximum size accepted by a single COS PUT Object request, in bytes.</summary>
        private const long MaximumObjectBytes = 5L * 1024 * 1024 * 1024;

        /// <inheritdoc/>
        public async Task<StoredFile> UploadAsync(
            StorageUploadSource source,
            string objectKey,
            string contentType,
            StorageConfiguration configuration,
            StorageProgressListener progress,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateUpload(
                source,
                objectKey,
                contentType,
                configuration
            );
            try
            {
                var url = ObjectUrl(
                    configuration,
                    objectKey,
                    false
                );
                using (var request = new UnityWebRequest(
                    url.AbsoluteUri,
                    "PUT"
                ))
                {
                    request.timeout = TransferTimeoutSeconds;
                    request.redirectLimit = 0;
                    request.uploadHandler = source.FilePath != null
                        ? (UploadHandler)new UploadHandlerFile(source.FilePath)
                        : new UploadHandlerRaw(source.Data);
                    request.uploadHandler.contentType = contentType;
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader(
                        "Content-Type",
                        contentType
                    );
                    request.SetRequestHeader(
                        "x-cos-security-token",
                        configuration.Credential.SessionToken
                    );
                    request.SetRequestHeader(
                        "Authorization",
                        StorageRequestSigner.AuthorizePut(
                            url,
                            contentType,
                            configuration.Credential,
                            DateTimeOffset.UtcNow
                        )
                    );
                    await TransferAsync(
                        request,
                        source.ByteCount,
                        progress,
                        XmaxErrorCode.UploadError,
                        cancellationToken
                    );
                    ReportProgress(
                        progress,
                        source.ByteCount,
                        source.ByteCount
                    );
                    var storedUrl = ResolveObjectUrl(
                        request.GetResponseHeader("Location"),
                        configuration,
                        objectKey
                    );
                    var etag = request.GetResponseHeader("ETag")?.Trim();
                    return new StoredFile(
                        storedUrl,
                        objectKey,
                        string.IsNullOrEmpty(etag) ? null : etag
                    );
                }
            }
            catch (Exception error) when (!(error is XmaxException) && !(error is OperationCanceledException))
            {
                throw new XmaxException(
                    XmaxErrorCode.UploadError,
                    "Storage upload failed: " + error.Message,
                    innerException: error
                );
            }
        }

        /// <inheritdoc/>
        public async Task<DownloadedFile> DownloadAsync(
            Uri remoteUrl,
            string destinationPath,
            StorageProgressListener progress,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsHttpUrl(remoteUrl))
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Invalid download URL."
                );
            string temporaryPath = null;
            try
            {
                var destination = Path.GetFullPath(destinationPath);
                if (Directory.Exists(destination))
                    throw new XmaxException(
                        XmaxErrorCode.InvalidConfiguration,
                        "Download destination must not be a directory."
                    );
                var parent = Path.GetDirectoryName(destination);
                if (!Directory.Exists(parent))
                    throw new XmaxException(
                        XmaxErrorCode.InvalidConfiguration,
                        "Download destination directory does not exist."
                    );
                temporaryPath = Path.Combine(
                    parent,
                    ".xmax-" + Guid.NewGuid().ToString("N") + ".download"
                );
                using (var request = new UnityWebRequest(
                    remoteUrl.AbsoluteUri,
                    "GET"
                ))
                {
                    request.timeout = TransferTimeoutSeconds;
                    request.downloadHandler = new DownloadHandlerFile(temporaryPath) { removeFileOnAbort = true };
                    await TransferAsync(
                        request,
                        null,
                        progress,
                        XmaxErrorCode.DownloadError,
                        cancellationToken
                    );
                }

                var count = new FileInfo(temporaryPath).Length;
                cancellationToken.ThrowIfCancellationRequested();
                // The staging file shares the destination filesystem so replacement preserves the old file on failure.
                if (File.Exists(destination))
                    File.Replace(
                        temporaryPath,
                        destination,
                        null
                    );
                else
                    File.Move(
                        temporaryPath,
                        destination
                    );
                ReportProgress(
                    progress,
                    count,
                    count
                );
                return new DownloadedFile(
                    destination,
                    count
                );
            }
            catch (Exception error) when (!(error is XmaxException) && !(error is OperationCanceledException))
            {
                throw new XmaxException(
                    XmaxErrorCode.DownloadError,
                    "Storage download failed: " + error.Message,
                    innerException: error
                );
            }
            finally
            {
                if (temporaryPath != null)
                {
                    try
                    {
                        if (File.Exists(temporaryPath))
                            File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        /// <summary>Runs a Unity transfer, reports byte changes, and aborts promptly on caller cancellation.</summary>
        private static async Task TransferAsync(
            UnityWebRequest request,
            long? uploadBytes,
            StorageProgressListener progress,
            XmaxErrorCode errorCode,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            long previousBytes = -1;
            long previousTotal = -1;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                var completed = (long)Math.Min((ulong)long.MaxValue, uploadBytes.HasValue ? request.uploadedBytes : request.downloadedBytes);
                long expected;
                if (uploadBytes.HasValue)
                    expected = uploadBytes.Value;
                else if (!long.TryParse(
                    request.GetResponseHeader("Content-Length"),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out expected
                ) || expected < 0)
                    expected = 0;
                if ((uploadBytes.HasValue || (request.responseCode >= 200 && request.responseCode < 300)) &&
                    (completed != previousBytes || expected != previousTotal))
                {
                    ReportProgress(
                        progress,
                        completed,
                        expected
                    );
                    previousBytes = completed;
                    previousTotal = expected;
                }
                await Task.Yield();
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success || request.responseCode < 200 || request.responseCode >= 300)
                throw StorageDiagnostics.TransferError(
                    request,
                    errorCode
                );
        }

        /// <summary>Invokes optional progress without allowing a subscriber failure to interrupt transfer.</summary>
        private static void ReportProgress(
            StorageProgressListener progress,
            long completed,
            long total
        )
        {
            try
            {
                progress?.Invoke(
                    completed,
                    total
                );
            }
            catch (Exception error)
            {
                XmaxLogger.Storage.Warn(() => "Storage progress callback failed: " + ErrorMessageFormatter.Format(error));
            }
        }

        /// <summary>Validates the single-object transfer and its complete temporary credential.</summary>
        private static void ValidateUpload(
            StorageUploadSource source,
            string objectKey,
            string contentType,
            StorageConfiguration configuration
        )
        {
            var credential = configuration?.Credential;
            if (source == null || source.ByteCount > MaximumObjectBytes || configuration == null ||
                !Identifier(configuration.Bucket) || !Identifier(configuration.Region) ||
                string.IsNullOrWhiteSpace(objectKey) || !HeaderString(contentType) || credential == null ||
                !HeaderString(credential.AccessKeyId) || !HeaderString(credential.SecretAccessKey) || !HeaderString(credential.SessionToken))
                throw new XmaxException(
                    XmaxErrorCode.UploadError,
                    "Storage upload configuration, credential, or object size is invalid."
                );
        }

        /// <summary>Requires a nonempty identifier containing only bucket and region name characters.</summary>
        private static bool Identifier(
            string value
        ) => !string.IsNullOrEmpty(value) && Regex.IsMatch(
            value,
            @"^[A-Za-z0-9.-]+$"
        );

        /// <summary>Rejects empty credentials or embedded HTTP line separators.</summary>
        private static bool HeaderString(
            string value
        ) => !string.IsNullOrWhiteSpace(value) && value.IndexOfAny(new[] { '\r', '\n' }) < 0;

        /// <summary>Resolves the response location or builds the public object URL from its configured endpoint.</summary>
        internal static Uri ResolveObjectUrl(
            string candidate,
            StorageConfiguration configuration,
            string objectKey
        )
        {
            var location = (candidate ?? string.Empty).Trim();
            if (location.StartsWith(
                "//",
                StringComparison.Ordinal
            ))
                location = "https:" + location;
            if (Uri.TryCreate(
                location,
                UriKind.Absolute,
                out var url
            ) && IsHttpUrl(url))
                return url;
            return ObjectUrl(
                configuration,
                objectKey,
                true
            );
        }

        /// <summary>Builds an encoded object URL, preserving an endpoint path only for the returned public URL.</summary>
        private static Uri ObjectUrl(
            StorageConfiguration configuration,
            string objectKey,
            bool preservePath
        )
        {
            var endpoint = (configuration.Endpoint ?? string.Empty).Trim();
            if (endpoint.Length == 0)
                endpoint = "https://" + configuration.Bucket + ".cos." + configuration.Region + ".myqcloud.com";
            else if (!endpoint.Contains("://"))
                endpoint = "https://" + endpoint;
            if (!Uri.TryCreate(
                endpoint,
                UriKind.Absolute,
                out var baseUrl
            ) || !IsHttpUrl(baseUrl) || baseUrl.UserInfo.Length > 0)
                throw new XmaxException(
                    XmaxErrorCode.UploadError,
                    "Storage endpoint is invalid."
                );

            var builder = new UriBuilder(baseUrl) { Query = string.Empty, Fragment = string.Empty };
            if (baseUrl.Host.StartsWith(
                "cos.",
                StringComparison.OrdinalIgnoreCase
            ))
                builder.Host = configuration.Bucket + "." + baseUrl.Host;
            if (!preservePath)
                builder.Path = "/";
            var parts = objectKey.Split('/');
            if (parts.Any(part => part == "." || part == ".."))
                throw new XmaxException(
                    XmaxErrorCode.UploadError,
                    "Storage object key contains an invalid path segment."
                );
            var encodedKey = string.Join(
                "/",
                parts.Select(Uri.EscapeDataString)
            );
            return new Uri(builder.Uri.AbsoluteUri.TrimEnd('/') + "/" + encodedKey);
        }

        /// <summary>Requires an absolute HTTP or HTTPS URL with a nonempty host.</summary>
        private static bool IsHttpUrl(
            Uri url
        ) => url != null && url.IsAbsoluteUri && !string.IsNullOrEmpty(url.Host) &&
            (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
    }
}
