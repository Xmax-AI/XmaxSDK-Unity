//
//  StorageCredential.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>Temporary COS authentication material held only for the current upload.</summary>
    internal sealed class StorageCredential
    {

        /// <summary>The temporary access key identifier.</summary>
        internal string AccessKeyId { get; }

        /// <summary>The temporary signing secret.</summary>
        internal string SecretAccessKey { get; }

        /// <summary>The security token supplied with the temporary key.</summary>
        internal string SessionToken { get; }

        /// <summary>Creates temporary COS authentication material held only for the current upload.</summary>
        internal StorageCredential(
            string accessKeyId,
            string secretAccessKey,
            string sessionToken
        )
        {
            AccessKeyId = accessKeyId;
            SecretAccessKey = secretAccessKey;
            SessionToken = sessionToken;
        }
    }
}
