//
//  RuntimeInfo.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Xmax.SDK
{

    /// <summary>The Unity runtime metadata attached to API requests and RTC signaling.</summary>
    internal static class RuntimeInfo
    {

        /// <summary>Adds the current platform, operating system, SDK, and device request headers.</summary>
        internal static void ApplyHeaders(
            UnityWebRequest request
        )
        {
            request.SetRequestHeader(
                "X-Platform",
                "unity"
            );
            request.SetRequestHeader(
                "X-OS-Version",
                HeaderValue(SystemInfo.operatingSystem)
            );
            request.SetRequestHeader(
                "X-SDK-Version",
                XmaxSdkInfo.Version
            );
            request.SetRequestHeader(
                "X-Device-Model",
                HeaderValue(SystemInfo.deviceModel)
            );
        }

        /// <summary>Creates the metadata embedded in each room signal.</summary>
        internal static Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
        {
            ["platform"] = "unity",
            ["sdk_version"] = XmaxSdkInfo.Version,
            ["os_version"] = SystemInfo.operatingSystem,
            ["device_model"] = SystemInfo.deviceModel
        };

        /// <summary>Removes line breaks that are invalid in HTTP header values.</summary>
        private static string HeaderValue(
            string value
        ) => (value ?? string.Empty).Replace(
            "\r",
            " "
        ).Replace(
            "\n",
            " "
        );
    }
}
