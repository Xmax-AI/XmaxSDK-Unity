//
//  ApiLogger.cs
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

    /// <summary>Logs API metadata and failed response contents without authentication fields.</summary>
    internal static class ApiLogger
    {

        /// <summary>Logs response status and elapsed time; failed responses include a redacted body.</summary>
        internal static void Response(
            string method,
            string path,
            long status,
            long bytes,
            long milliseconds,
            bool successful = true,
            string responseBody = null
        )
        {
            if (successful)
                XmaxLogger.Api.Debug(() => ResponseMessage(
                        method,
                        path,
                        status,
                        bytes,
                        milliseconds,
                        null
                    ));
            else
                XmaxLogger.Api.Error(() => ResponseMessage(
                        method,
                        path,
                        status,
                        bytes,
                        milliseconds,
                        responseBody
                    ));
        }

        /// <summary>Logs a failure before a complete HTTP response is available.</summary>
        internal static void Failure(
            string method,
            string path,
            Exception exception,
            long milliseconds
        )
            => XmaxLogger.Api.Error(() => method + " " + Route(path) + " 失败 (Request Failed)\n"
                + "├─ " + XmaxLogger.Localized(
                    "耗时：",
                    "Duration: "
                ) + milliseconds + " ms\n"
                + "└─ " + XmaxLogger.Localized(
                    "原因：",
                    "Reason: "
                ) + ErrorMessageFormatter.Format(exception));

        /// <summary>Formats status, duration, response size, and an optional failed response body.</summary>
        private static string ResponseMessage(
            string method,
            string path,
            long status,
            long bytes,
            long milliseconds,
            string body
        )
        {
            var prefix = method + " " + Route(path) + "\n"
                + "├─ " + XmaxLogger.Localized(
                    "状态：",
                    "Status: "
                ) + status + "\n"
                + "├─ " + XmaxLogger.Localized(
                    "耗时：",
                    "Duration: "
                ) + milliseconds + " ms\n";
            var size = XmaxLogger.Localized(
                "响应：",
                "Response Size: "
            ) + bytes + " bytes";
            return body == null ? prefix + "└─ " + size : prefix + "├─ " + size + "\n└─ "
                + XmaxLogger.Localized(
                    "正文：",
                    "Response Body: "
                ) + "\n   " + JsonCodec.FormatForLogging(body).Replace(
                    "\n",
                    "\n   "
                );
        }

        /// <summary>Removes query parameters and replaces session identifiers with a route template.</summary>
        private static string Route(
            string path
        )
        {
            var route = (path ?? string.Empty).Split('?')[0];
            if (route == "/session")
                return route;
            if (route == "/cos/sts" || route == "/cos/image/check")
                return route;
            if (route.StartsWith(
                "/session/",
                StringComparison.Ordinal
            ))
                return route.EndsWith(
                    "/heartbeat",
                    StringComparison.Ordinal
                ) ? "/session/{id}/heartbeat" : "/session/{id}";
            return "<route>";
        }
    }
}
