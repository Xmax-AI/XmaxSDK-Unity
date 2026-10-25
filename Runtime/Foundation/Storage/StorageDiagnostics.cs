//
//  StorageDiagnostics.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine.Networking;

namespace Xmax.SDK
{
    /// <summary>Preserves storage transport diagnostics and formats business log details.</summary>
    internal static class StorageDiagnostics
    {

        /// <summary>The exception data key retaining the platform error domain.</summary>
        private const string DomainKey = "Xmax.Storage.ErrorDomain";

        /// <summary>The exception data key retaining the platform or COS error code.</summary>
        private const string CodeKey = "Xmax.Storage.ErrorCode";

        /// <summary>The exception data key retaining the COS request identifier.</summary>
        private const string RequestKey = "Xmax.Storage.RequestId";

        /// <summary>Creates a public transfer error with bounded, nonexecutable COS error details.</summary>
        internal static XmaxException TransferError(
            UnityWebRequest request,
            XmaxErrorCode category
        )
        {
            var fields = new Dictionary<string, string>();
            if (request.downloadedBytes <= 65536 && request.downloadHandler is DownloadHandlerBuffer buffer)
            {
                var body = buffer.text;
                if (!string.IsNullOrEmpty(body) && body.Length <= 65536)
                {
                    try
                    {
                        using (var text = new StringReader(body))
                        using (var reader = XmlReader.Create(
                            text,
                            new XmlReaderSettings
                            {
                                DtdProcessing = DtdProcessing.Prohibit,
                                XmlResolver = null,
                                MaxCharactersInDocument = 65536
                            }
                        ))
                        {
                            reader.MoveToContent();
                            while (!reader.EOF)
                            {
                                if (reader.NodeType == XmlNodeType.Element &&
                                    (reader.LocalName == "Code" || reader.LocalName == "Message" || reader.LocalName == "RequestId"))
                                {
                                    var name = reader.LocalName;
                                    fields[name] = reader.ReadElementContentAsString();
                                }
                                else
                                    reader.Read();
                            }
                        }
                    }
                    catch (XmlException)
                    {
                        fields.Clear();
                    }
                }
            }

            fields.TryGetValue(
                "Code",
                out var cosCode
            );
            fields.TryGetValue(
                "Message",
                out var cosMessage
            );
            fields.TryGetValue(
                "RequestId",
                out var requestId
            );
            requestId = request.GetResponseHeader("x-cos-request-id") ?? requestId;
            var message = string.IsNullOrWhiteSpace(cosMessage) ? request.error ?? "Invalid HTTP response." : cosMessage;
            var description = string.IsNullOrWhiteSpace(cosCode) ? message : cosCode + ": " + message;
            if (!string.IsNullOrEmpty(requestId))
                description += " (RequestId: " + requestId + ")";

            var error = new XmaxException(
                category,
                "Storage transfer failed: " + description,
                httpStatus: request.responseCode
            );
            error.Data[DomainKey] = string.IsNullOrWhiteSpace(cosCode) ? "UnityWebRequest." + request.result : "COS";
            error.Data[CodeKey] = string.IsNullOrWhiteSpace(cosCode)
                ? (request.responseCode > 0 ? request.responseCode : (long)request.result).ToString(CultureInfo.InvariantCulture) : cosCode;
            error.Data[RequestKey] = requestId;
            return error;
        }

        /// <summary>Formats native error domain, code, message, request identity, and elapsed upload time.</summary>
        internal static string UploadFailure(
            Exception error,
            TimeSpan elapsed
        )
        {
            var domain = error.Data[DomainKey] as string ?? error.GetType().Namespace;
            var code = error.Data[CodeKey] as string ?? (error is XmaxException
                ? XmaxLogger.ErrorCode(error) : error.HResult.ToString(CultureInfo.InvariantCulture));
            var requestId = error.Data[RequestKey] as string;
            return "上传失败 (Upload Failed)\n"
                + "├─ " + XmaxLogger.Localized(
                    "错误域：",
                    "Error Domain: "
                ) + domain + "\n"
                + "├─ " + XmaxLogger.Localized(
                    "错误码：",
                    "Error Code: "
                ) + code + "\n"
                + "├─ " + XmaxLogger.Localized(
                    "错误信息：",
                    "Error Message: "
                ) + ErrorMessageFormatter.Format(error) + "\n"
                + (string.IsNullOrEmpty(requestId) ? "" : "├─ RequestId: " + requestId + "\n")
                + "└─ " + XmaxLogger.Localized(
                    "耗时：",
                    "Duration: "
                ) + Duration(elapsed);
        }

        /// <summary>Formats bytes using B, KB, MB, or GB with binary unit boundaries.</summary>
        internal static string ByteCount(
            long count
        )
        {
            if (count < 1024)
                return count.ToString(CultureInfo.InvariantCulture) + " B";
            if (count < 1024L * 1024)
                return (count / 1024.0).ToString(
                    "F1",
                    CultureInfo.InvariantCulture
                ) + " KB";
            if (count < 1024L * 1024 * 1024)
                return (count / (1024.0 * 1024)).ToString(
                    "F2",
                    CultureInfo.InvariantCulture
                ) + " MB";
            return (count / (1024.0 * 1024 * 1024)).ToString(
                "F2",
                CultureInfo.InvariantCulture
            ) + " GB";
        }

        /// <summary>Formats elapsed time as integer milliseconds or seconds with two decimal places.</summary>
        internal static string Duration(
            TimeSpan elapsed
        ) => elapsed.TotalMilliseconds < 1000
            ? ((long)elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + " ms"
            : elapsed.TotalSeconds.ToString(
                "F2",
                CultureInfo.InvariantCulture
            ) + " s";
    }
}
