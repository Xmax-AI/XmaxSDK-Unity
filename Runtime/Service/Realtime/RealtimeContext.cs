//
//  RealtimeContext.cs
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
    /// The prompt and optional server-accessible reference image for generation.
    /// </summary>
    public sealed class RealtimeContext
    {

        /// <summary>
        /// The normalized generation prompt. Empty input represents an empty prompt.
        /// </summary>
        public string Prompt { get; }

        /// <summary>
        /// The optional server-accessible reference image path.
        /// </summary>
        public string ReferencePath { get; }

        /// <summary>
        /// Creates generation conditions.
        /// </summary>
        public RealtimeContext(
            string prompt,
            string referencePath = null
        )
        {
            Prompt = (prompt ?? string.Empty).Trim();
            var normalized = (referencePath ?? string.Empty).Trim();
            ReferencePath = normalized.Length == 0 ? null : normalized;
        }
    }
}
