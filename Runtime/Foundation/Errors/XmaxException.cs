//
//  XmaxException.cs
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

    /// <summary>The effect of a failure on the active realtime connection.</summary>
    public enum XmaxErrorSeverity
    {

        /// <summary>The operation or cleanup failed without requiring termination of a working connection.</summary>
        Recoverable,

        /// <summary>The connection requires cleanup before it can be established again.</summary>
        Fatal
    }

    /// <summary>The SDK failure category shared by services and realtime components.</summary>
    public enum XmaxErrorCode
    {

        /// <summary>The online request has no valid API key.</summary>
        InvalidApiKey,

        /// <summary>The service base URL is invalid.</summary>
        InvalidBaseUrl,

        /// <summary>Arguments, call timing, or the calling thread violate an API requirement.</summary>
        InvalidConfiguration,

        /// <summary>The HTTP transport failed to connect or complete the request.</summary>
        NetworkError,

        /// <summary>The HTTP API returned a failure or an invalid response.</summary>
        ApiError,

        /// <summary>The server session state or RTC connection credentials are invalid.</summary>
        SessionError,

        /// <summary>The RTC transport or realtime generation operation failed.</summary>
        RtcError,

        /// <summary>An awaited operation exceeded its deadline.</summary>
        Timeout,

        /// <summary>The platform, format, or model does not support the requested operation.</summary>
        NotSupported,

        /// <summary>An external media source or frame binding failed.</summary>
        MediaError,

        /// <summary>The object storage upload failed.</summary>
        UploadError,

        /// <summary>The object storage download or local file commit failed.</summary>
        DownloadError,

        /// <summary>The uploaded image did not pass the server safety check.</summary>
        UnsafeImage
    }

    /// <summary>An SDK failure retaining its category, severity, server diagnostics, and underlying exception.</summary>
    public sealed class XmaxException : Exception
    {

        /// <summary>The SDK failure category.</summary>
        public XmaxErrorCode Code { get; }

        /// <summary>The server business error code, or null when unavailable.</summary>
        public int? ApiCode { get; }

        /// <summary>The HTTP response status, or null when the failure has no associated response.</summary>
        public long? HttpStatus { get; }

        /// <summary>Whether the failure requires termination of the current connection.</summary>
        public XmaxErrorSeverity Severity { get; }

        /// <summary>Creates an SDK exception retaining supplied server diagnostics and the original cause.</summary>
        public XmaxException(
            XmaxErrorCode code,
            string message,
            int? apiCode = null,
            long? httpStatus = null,
            Exception innerException = null,
            XmaxErrorSeverity severity = XmaxErrorSeverity.Recoverable
        )
            : base(
                message,
                innerException
            )
        {
            Code = code;
            ApiCode = apiCode;
            HttpStatus = httpStatus;
            Severity = severity;
        }
    }
}
