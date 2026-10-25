//
//  ErrorMessageFormatter.cs
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

    /// <summary>Formats SDK and platform failures with their diagnostic codes.</summary>
    internal static class ErrorMessageFormatter
    {

        /// <summary>Returns the error message and available SDK, API, and HTTP codes.</summary>
        internal static string Format(
            Exception error
        )
        {
            if (error is OperationCanceledException)
                return XmaxLogger.Localized(
                    "操作已取消",
                    "Operation cancelled"
                );
            if (error is XmaxException known)
            {
                var api = known.ApiCode.HasValue ? XmaxLogger.Localized(
                    "，业务码 ",
                    ", API "
                ) + known.ApiCode.Value : "";
                var http = known.HttpStatus.HasValue ? ", HTTP " + known.HttpStatus.Value : "";
                return LogRedaction.Text(known.Message) + " (" + known.Code + api + http + ")";
            }
            return LogRedaction.Text(error?.Message ?? XmaxLogger.Localized(
                    "未知错误",
                    "Unknown error"
                ))
                + " (" + XmaxLogger.ErrorCode(error) + ")";
        }
    }
}
