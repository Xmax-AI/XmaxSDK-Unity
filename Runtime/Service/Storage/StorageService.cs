//
//  StorageService.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>Owns storage validation, temporary credential retrieval, naming, and image safety policy.</summary>
    internal sealed class StorageService : IStorageService
    {

        /// <summary>The general business API, separate from the realtime session endpoint.</summary>
        private readonly IApiService _api;

        /// <summary>The object upload and download transport.</summary>
        private readonly IStorageManager _storage;

        /// <summary>The clock used to create time-prefixed object names.</summary>
        private readonly Func<DateTimeOffset> _date;

        /// <summary>The unique suffix generator used for each object name.</summary>
        private readonly Func<string> _identifier;

        /// <summary>The known image MIME types used for file uploads without an explicit content type.</summary>
        private static readonly IReadOnlyDictionary<string, string> ImageTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["jpg"] = "image/jpeg",
            ["jpeg"] = "image/jpeg",
            ["jpe"] = "image/jpeg",
            ["png"] = "image/png",
            ["gif"] = "image/gif",
            ["webp"] = "image/webp",
            ["heic"] = "image/heic",
            ["heif"] = "image/heif",
            ["bmp"] = "image/bmp",
            ["svg"] = "image/svg+xml",
            ["tif"] = "image/tiff",
            ["tiff"] = "image/tiff",
            ["avif"] = "image/avif"
        };

        /// <summary>The known video MIME types used for file uploads without an explicit content type.</summary>
        private static readonly IReadOnlyDictionary<string, string> VideoTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mp4"] = "video/mp4",
            ["mov"] = "video/quicktime",
            ["m4v"] = "video/x-m4v",
            ["webm"] = "video/webm",
            ["avi"] = "video/x-msvideo",
            ["mkv"] = "video/x-matroska",
            ["3gp"] = "video/3gpp",
            ["3g2"] = "video/3gpp2",
            ["ts"] = "video/mp2t"
        };

        /// <summary>Creates the business service with independently supplied transport, clock, and identity generation.</summary>
        internal StorageService(
            IApiService api,
            IStorageManager storage,
            Func<DateTimeOffset> date = null,
            Func<string> identifier = null
        )
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _date = date ?? (() => DateTimeOffset.UtcNow);
            _identifier = identifier ?? (() => Guid.NewGuid().ToString().ToLowerInvariant());
        }

        /// <inheritdoc/>
        public async Task<StoredFile> UploadAsync(
            StorageUploadSource source,
            string fileName,
            string contentType,
            StorageMediaType mediaType,
            bool checksSafety,
            StorageProgressListener progress,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            try
            {
                if (source == null)
                    throw new ArgumentNullException(nameof(source));
                var type = ResolveContentType(
                    source,
                    fileName,
                    contentType,
                    mediaType
                );
                var safeName = SanitizeFileName(fileName);
                if (safeName.Length == 0 || (checksSafety && mediaType != StorageMediaType.Image))
                    throw new XmaxException(
                        XmaxErrorCode.InvalidConfiguration,
                        "Storage media name or safety configuration is invalid."
                    );

                XmaxLogger.Storage.Info(() => "开始上传 (Upload Started)\n"
                    + "├─ " + XmaxLogger.Localized(
                        "类型：",
                        "Type: "
                    ) + mediaType.ToString().ToLowerInvariant() + "\n"
                    + "├─ " + XmaxLogger.Localized(
                        "分辨率：",
                        "Resolution: "
                    ) + StorageMediaMetadata.Resolution(source, mediaType) + "\n"
                    + "├─ " + XmaxLogger.Localized(
                        "大小：",
                        "Size: "
                    ) + StorageDiagnostics.ByteCount(source.ByteCount) + "\n"
                    + "└─ " + XmaxLogger.Localized(
                        "安全检测：",
                        "Safety Check: "
                    ) + checksSafety);
                var payload = await _api.RequestAsync(
                    "GET",
                    "/cos/sts",
                    null,
                    cancellationToken
                );
                var configuration = ParseConfiguration(payload);
                var prefix = ReadString(
                    payload,
                    "prefix",
                    true
                );
                var objectKey = prefix + _date().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
                    + "_" + _identifier().ToLowerInvariant() + "_" + safeName;
                cancellationToken.ThrowIfCancellationRequested();
                var stored = await _storage.UploadAsync(
                    source,
                    objectKey,
                    type,
                    configuration,
                    progress,
                    cancellationToken
                );
                var result = stored;
                if (checksSafety)
                {
                    var checkedUrl = await CheckImageAsync(
                        stored.Url,
                        cancellationToken
                    );
                    result = new StoredFile(
                        checkedUrl,
                        stored.ObjectKey,
                        stored.ETag
                    );
                }
                cancellationToken.ThrowIfCancellationRequested();
                XmaxLogger.Storage.Info(() => "上传完成 (Upload Completed)\n"
                    + "├─ " + XmaxLogger.Localized(
                        "地址：",
                        "URL: "
                    ) + result.Url.AbsoluteUri + "\n"
                    + "└─ " + XmaxLogger.Localized(
                        "耗时：",
                        "Duration: "
                    ) + StorageDiagnostics.Duration(watch.Elapsed));
                return result;
            }
            catch (Exception error)
            {
                XmaxLogger.Storage.Error(() => StorageDiagnostics.UploadFailure(
                        error,
                        watch.Elapsed
                    ));
                if (error is XmaxException || error is OperationCanceledException)
                    throw;
                throw new XmaxException(
                    XmaxErrorCode.UploadError,
                    "Storage upload failed: " + error.Message,
                    innerException: error
                );
            }
        }

        /// <inheritdoc/>
        public Task<DownloadedFile> DownloadAsync(
            Uri remoteUrl,
            string destinationPath,
            StorageProgressListener progress,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsHttpUrl(remoteUrl) || string.IsNullOrWhiteSpace(destinationPath))
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "A valid HTTP URL and local destination path are required."
                );
            return _storage.DownloadAsync(
                remoteUrl,
                destinationPath,
                progress,
                cancellationToken
            );
        }

        /// <summary>Requires all temporary credential fields while allowing an empty endpoint and prefix.</summary>
        private static StorageConfiguration ParseConfiguration(
            JsonValue payload
        )
        {
            var credentials = payload != null && payload.IsObject && payload.ContainsKey("credentials")
                ? payload["credentials"] : null;
            return new StorageConfiguration(
                ReadString(
                    payload,
                    "bucket"
                ),
                ReadString(
                    payload,
                    "region"
                ),
                ReadString(
                    payload,
                    "endpoint",
                    true
                ),
                new StorageCredential(
                    ReadString(
                        credentials,
                        "accessKeyId"
                    ),
                    ReadString(
                        credentials,
                        "secretAccessKey"
                    ),
                    ReadString(
                        credentials,
                        "sessionToken"
                    )
                )
            );
        }

        /// <summary>Requires a successful safety verdict and a valid replacement image URL.</summary>
        private async Task<Uri> CheckImageAsync(
            Uri url,
            CancellationToken cancellationToken
        )
        {
            var body = JsonCodec.Encode(new Dictionary<string, object> { ["url"] = url.AbsoluteUri });
            var payload = await _api.RequestAsync(
                "POST",
                "/cos/image/check",
                body,
                cancellationToken
            );
            if (payload == null || !payload.IsObject || !payload.ContainsKey("safe") ||
                payload["safe"] == null || !payload["safe"].IsBoolean)
                throw new XmaxException(
                    XmaxErrorCode.ApiError,
                    "Invalid image safety check payload."
                );
            if (!(bool)payload["safe"])
                throw new XmaxException(
                    XmaxErrorCode.UnsafeImage,
                    "The image did not pass the safety check."
                );
            if (!Uri.TryCreate(
                ReadString(
                    payload,
                    "url",
                    true
                ),
                UriKind.Absolute,
                out var checkedUrl
            ) || !IsHttpUrl(checkedUrl))
                throw new XmaxException(
                    XmaxErrorCode.ApiError,
                    "Invalid image safety check URL."
                );
            return checkedUrl;
        }

        /// <summary>Reads a normalized required field without accepting nonstring credential payloads.</summary>
        private static string ReadString(
            JsonValue payload,
            string key,
            bool allowEmpty = false
        )
        {
            if (payload != null && payload.IsObject && payload.ContainsKey(key) && payload[key] != null && payload[key].IsString)
            {
                var value = ((string)payload[key]).Trim();
                if (allowEmpty || value.Length > 0)
                    return value;
            }
            throw new XmaxException(
                XmaxErrorCode.ApiError,
                "Invalid storage response field: " + key + "."
            );
        }

        /// <summary>Infers file MIME types and rejects content types outside the requested media category.</summary>
        private static string ResolveContentType(
            StorageUploadSource source,
            string fileName,
            string contentType,
            StorageMediaType mediaType
        )
        {
            if (contentType == null && source.FilePath != null)
            {
                var types = mediaType == StorageMediaType.Image ? ImageTypes : VideoTypes;
                if (!types.TryGetValue(
                    Path.GetExtension(fileName).TrimStart('.'),
                    out contentType
                ))
                    throw new XmaxException(
                        XmaxErrorCode.InvalidConfiguration,
                        "Unable to infer media content type from file extension."
                    );
            }
            var type = (contentType ?? string.Empty).Trim();
            var prefix = mediaType == StorageMediaType.Image ? "image/" : "video/";
            if (!type.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase
            ) || type.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Content type must begin with " + prefix + "."
                );
            return type;
        }

        /// <summary>Preserves letters, numbers, periods, underscores, and hyphens in uploaded file names.</summary>
        private static string SanitizeFileName(
            string fileName
        )
        {
            var sanitized = Regex.Replace(
                (fileName ?? string.Empty).Trim(),
                @"[^\p{L}\p{N}._-]+",
                "_"
            );
            return Regex.Replace(
                sanitized,
                "_+",
                "_"
            ).Trim(
                '.',
                '_',
                '-'
            );
        }

        /// <summary>Requires an absolute HTTP or HTTPS URL with a nonempty host.</summary>
        private static bool IsHttpUrl(
            Uri url
        ) => url != null && url.IsAbsoluteUri && !string.IsNullOrEmpty(url.Host) &&
            (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
    }
}
