//
//  LogRedaction.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Text.RegularExpressions;

namespace Xmax.SDK
{

    /// <summary>Removes named authentication values from diagnostic text and JSON.</summary>
    internal static class LogRedaction
    {

        /// <summary>Matches authentication field names independent of separators and casing.</summary>
        private static readonly Regex CredentialName = new Regex(
            @"^(?:xapikey|apikey|apitoken|token|accesstoken|refreshtoken|rtctoken|sessiontoken|xcossecuritytoken|accesskeyid|secretaccesskey|authorization|secret|clientsecret|password|credential|credentials|signature|qsignature)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        /// <summary>Matches credential assignments in exception or non-JSON diagnostic text.</summary>
        private static readonly Regex CredentialValue = new Regex(
            @"(?i)(\b(?:x[-_]?api[-_]?key|api[-_]?key|(?:access[-_]?|refresh[-_]?|rtc[-_]?|session[-_]?|x[-_]?cos[-_]?security[-_]?)?token|access[-_]?key[-_]?id|secret[-_]?access[-_]?key|authorization|(?:client[-_]?)?secret|password|(?:q[-_]?)?signature)\b[""']?\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;&}\]]+)",
            RegexOptions.CultureInvariant
        );

        /// <summary>Matches bearer credentials that may be embedded in transport failures.</summary>
        private static readonly Regex Bearer = new Regex(
            @"\bBearer\s+[^\s,""';}]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        /// <summary>Returns whether a JSON field carries authentication material.</summary>
        internal static bool IsCredential(
            string key
        ) => CredentialName.IsMatch((key ?? "").Replace(
                "_",
                ""
            ).Replace(
                "-",
                ""
            ));

        /// <summary>Redacts named credentials while retaining other diagnostic information.</summary>
        internal static string Text(
            string text
        ) => CredentialValue.Replace(
            Bearer.Replace(
                text ?? "",
                "Bearer <redacted>"
            ),
            match =>
            {
                var prefix = match.Groups[1].Value;
                var quoted = match.Value.Length > prefix.Length && match.Value[prefix.Length] == '\"';
                return prefix + (quoted ? "\"<redacted>\"" : "<redacted>");
            }
        );
    }
}
