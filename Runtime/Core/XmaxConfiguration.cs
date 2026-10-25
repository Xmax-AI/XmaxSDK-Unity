//
//  XmaxConfiguration.cs
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

    /// <summary>
    /// The service address, authentication, and logging settings for a client.
    /// </summary>
    public sealed class XmaxConfiguration
    {

        /// <summary>
        /// The default API base URL for the China environment.
        /// </summary>
        public const string DefaultBaseUrl = "https://cloud.xmax.22duck.cn/open/api/v1";

        /// <summary>
        /// The API base URL for the global environment.
        /// </summary>
        public const string GlobalBaseUrl = "https://api.xmax.cloud/open/api/v1";

        /// <summary>The realtime session API base URL for the global environment.</summary>
        public const string GlobalRealtimeBaseUrl = "https://api.xmax.ai/open/api/v1";

        /// <summary>
        /// The API key used to authenticate online requests.
        /// </summary>
        public string ApiKey { get; }

        /// <summary>
        /// The base URL used for API requests.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>The base URL used for realtime session creation, heartbeat, and closure.</summary>
        public string RealtimeBaseUrl { get; }

        /// <summary>
        /// The process-wide logging options applied when a client is created.
        /// </summary>
        public XmaxLoggerOption LoggerOptions { get; }

        /// <summary>The log detail language selected by the service environment.</summary>
        internal XmaxLogger.Language LogLanguage { get; }

        /// <summary>
        /// Creates a client configuration for the selected service environment.
        /// </summary>
        ///
        /// <param name="apiKey">The online API key; may be empty for local preview.</param>
        /// <param name="environment">The service region selecting the API base URL.</param>
        /// <param name="loggerOptions">The logging filter applied when creating a client.</param>
        public XmaxConfiguration(
            string apiKey,
            XmaxEnvironment environment,
            XmaxLoggerOption loggerOptions = XmaxLoggerOption.None
        )
            : this(
                apiKey,
                environment == XmaxEnvironment.China ? DefaultBaseUrl :
                environment == XmaxEnvironment.Global ? GlobalBaseUrl :
                throw new ArgumentOutOfRangeException(nameof(environment)),
                loggerOptions
            )
        {
            LogLanguage = environment == XmaxEnvironment.Global ? XmaxLogger.Language.English : XmaxLogger.Language.Chinese;
        }

        /// <summary>
        /// Creates a client configuration with an explicit service address.
        /// </summary>
        ///
        /// <param name="apiKey">The online API key; may be empty for local preview.</param>
        /// <param name="baseUrl">The absolute HTTP or HTTPS service URL, validated before online requests.</param>
        /// <param name="loggerOptions">The logging filter applied when creating a client.</param>
        public XmaxConfiguration(
            string apiKey,
            string baseUrl = DefaultBaseUrl,
            XmaxLoggerOption loggerOptions = XmaxLoggerOption.None
        )
        {
            ApiKey = (apiKey ?? string.Empty).Trim();
            BaseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            LoggerOptions = loggerOptions;
            RealtimeBaseUrl = string.Equals(
                BaseUrl,
                GlobalBaseUrl,
                StringComparison.OrdinalIgnoreCase
            )
                ? GlobalRealtimeBaseUrl : BaseUrl;
            LogLanguage = Uri.TryCreate(
                BaseUrl,
                UriKind.Absolute,
                out var uri
            ) &&
                (string.Equals(
                    uri.Host,
                    new Uri(GlobalBaseUrl).Host,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    uri.Host,
                    new Uri(GlobalRealtimeBaseUrl).Host,
                    StringComparison.OrdinalIgnoreCase
                ))
                ? XmaxLogger.Language.English : XmaxLogger.Language.Chinese;
        }

        /// <summary>
        /// Requires a nonempty API key and an absolute HTTP or HTTPS service address for online use.
        /// </summary>
        internal void Validate()
        {
            if (ApiKey.Length == 0)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidApiKey,
                    "API key cannot be empty."
                );
            }

            if (!Uri.TryCreate(
                BaseUrl,
                UriKind.Absolute,
                out var uri
            ) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidBaseUrl,
                    $"Invalid base URL: {BaseUrl}"
                );
            }
        }
    }
}
