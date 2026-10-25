//
//  StorageRequestSigner.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Xmax.SDK
{

    /// <summary>Creates COS XML request authorization from one temporary upload credential.</summary>
    internal static class StorageRequestSigner
    {

        /// <summary>Signs the PUT path and canonical content type, host, and security token headers.</summary>
        internal static string AuthorizePut(
            Uri url,
            string contentType,
            StorageCredential credential,
            DateTimeOffset now
        )
        {
            var time = (now.ToUnixTimeSeconds() - 60).ToString(CultureInfo.InvariantCulture)
                + ";" + (now.ToUnixTimeSeconds() + 25 * 60).ToString(CultureInfo.InvariantCulture);
            var signKey = Hmac(
                credential.SecretAccessKey,
                time
            );
            var headers = "content-type=" + Uri.EscapeDataString(contentType)
                + "&host=" + Uri.EscapeDataString(url.Authority.ToLowerInvariant())
                + "&x-cos-security-token=" + Uri.EscapeDataString(credential.SessionToken);
            var http = "put\n" + Uri.UnescapeDataString(url.AbsolutePath) + "\n\n" + headers + "\n";
            string digest;
            using (var sha = SHA1.Create())
                digest = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(http)));
            var signature = Hmac(
                signKey,
                "sha1\n" + time + "\n" + digest + "\n"
            );
            return "q-sign-algorithm=sha1&q-ak=" + Uri.EscapeDataString(credential.AccessKeyId)
                + "&q-sign-time=" + time + "&q-key-time=" + time
                + "&q-header-list=content-type;host;x-cos-security-token&q-url-param-list=&q-signature=" + signature;
        }

        /// <summary>Calculates a lowercase HMAC-SHA1 digest using a UTF-8 string key.</summary>
        private static string Hmac(
            string key,
            string value
        )
        {
            using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key)))
                return Hex(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }

        /// <summary>Formats the binary digest without separators or culture-specific casing.</summary>
        private static string Hex(
            byte[] bytes
        ) => BitConverter.ToString(bytes).Replace(
            "-",
            ""
        ).ToLowerInvariant();
    }
}
