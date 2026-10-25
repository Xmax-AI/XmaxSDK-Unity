//
//  ApiService.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Xmax.SDK
{

    /// <summary>Sends HTTP requests and parses API response envelopes.</summary>
    internal interface IApiService
    {

        /// <summary>Validates configuration and sends a cancellable JSON request, returning successful response data.</summary>
        Task<JsonValue> RequestAsync(
            string method,
            string path,
            string body,
            CancellationToken cancellationToken
        );
    }

    /// <summary>Sends authenticated UnityWebRequest requests and normalizes API and network failures.</summary>
    internal sealed class ApiService : IApiService
    {

        /// <summary>The HTTP timeout, in seconds.</summary>
        private const int TimeoutSeconds = 15;

        /// <summary>The shared service and authentication configuration.</summary>
        private readonly XmaxConfiguration _configuration;

        /// <summary>The service-specific API root selected when this service is created.</summary>
        private readonly string _baseUrl;

        /// <summary>Creates an HTTP API service using the client configuration.</summary>
        internal ApiService(
            XmaxConfiguration configuration,
            bool realtime = false
        )
        {
            _configuration = configuration;
            _baseUrl = realtime ? configuration.RealtimeBaseUrl : configuration.BaseUrl;
        }

        /// <summary>Sends a cancellable JSON request and returns its validated response data; a successful empty DELETE response returns null.</summary>
        public async Task<JsonValue> RequestAsync(
            string method,
            string path,
            string body,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            _configuration.Validate();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var responseLogged = false;

            try
            {
                using (var request = new UnityWebRequest(
                    _baseUrl + path,
                    method
                ))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    if (body != null)
                    {
                        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    }

                    request.timeout = TimeoutSeconds;
                    request.SetRequestHeader(
                        "Accept",
                        "application/json"
                    );
                    request.SetRequestHeader(
                        "Content-Type",
                        "application/json"
                    );
                    request.SetRequestHeader(
                        "X-Api-Key",
                        _configuration.ApiKey
                    );

                    RuntimeInfo.ApplyHeaders(request);
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            request.Abort();
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        await Task.Yield();
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var text = request.downloadHandler?.text ?? string.Empty;
                    JsonValue result;
                    try
                    {
                        result = ParseEnvelope(
                            text,
                            request.responseCode,
                            request.result == UnityWebRequest.Result.ConnectionError,
                            request.error,
                            method == "DELETE"
                        );
                    }
                    catch
                    {
                        if (request.responseCode > 0)
                        {
                            responseLogged = true;
                            ApiLogger.Response(
                                method,
                                path,
                                request.responseCode,
                                (long)request.downloadedBytes,
                                elapsed.ElapsedMilliseconds,
                                false,
                                text
                            );
                        }
                        throw;
                    }
                    responseLogged = true;
                    ApiLogger.Response(
                        method,
                        path,
                        request.responseCode,
                        (long)request.downloadedBytes,
                        elapsed.ElapsedMilliseconds
                    );
                    return result;
                }
            }
            catch (Exception exception)
            {
                if (!responseLogged && !(exception is OperationCanceledException))
                    ApiLogger.Failure(
                        method,
                        path,
                        exception,
                        elapsed.ElapsedMilliseconds
                    );

                throw;
            }
        }

        /// <summary>Parses response data and converts network, API, and malformed-response failures into SDK errors.</summary>
        internal static JsonValue ParseEnvelope(
            string text,
            long status,
            bool connectionError,
            string error,
            bool allowEmpty = false
        )
        {
            if (connectionError)
                throw new XmaxException(
                    XmaxErrorCode.NetworkError,
                    error ?? "HTTP request failed.",
                    httpStatus: status
                );

            if (allowEmpty && status >= 200 && status < 300 && string.IsNullOrWhiteSpace(text))
                return null;

            JsonValue envelope;

            try
            {
                envelope = JsonCodec.Decode(text);
            }
            catch (Exception exception)
            {
                throw new XmaxException(
                    XmaxErrorCode.ApiError,
                    "Server returned invalid JSON.",
                    httpStatus: status,
                    innerException: exception
                );
            }

            if (envelope == null || !envelope.IsObject)
                throw new XmaxException(
                    XmaxErrorCode.ApiError,
                    "Invalid API response envelope.",
                    httpStatus: status
                );

            var success = envelope.ContainsKey("success") &&
                envelope["success"] != null &&
                envelope["success"].IsBoolean &&
                (bool)envelope["success"];
            if (status >= 200 && status < 300 && success && envelope.ContainsKey("data"))
                return envelope["data"];

            int? code = envelope.ContainsKey("code") && envelope["code"] != null && envelope["code"].IsInt
                ? (int)envelope["code"]
                : (int?)null;
            var message = envelope.ContainsKey("message") && envelope["message"] != null && envelope["message"].IsString
                ? (string)envelope["message"]
                : error;

            throw new XmaxException(
                XmaxErrorCode.ApiError,
                message ?? "Xmax API request failed.",
                code,
                status
            );
        }
    }
}
