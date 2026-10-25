//
//  StorageConfiguration.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>The bucket, endpoint, region, and temporary upload credential.</summary>
    internal sealed class StorageConfiguration
    {

        /// <summary>The bucket name including its application suffix.</summary>
        internal string Bucket { get; }

        /// <summary>The COS region identifier.</summary>
        internal string Region { get; }

        /// <summary>The optional endpoint; an empty value selects the regional bucket endpoint.</summary>
        internal string Endpoint { get; }

        /// <summary>The temporary credential for this operation.</summary>
        internal StorageCredential Credential { get; }

        /// <summary>Creates the bucket, endpoint, region, and temporary upload credential.</summary>
        internal StorageConfiguration(
            string bucket,
            string region,
            string endpoint,
            StorageCredential credential
        )
        {
            Bucket = bucket;
            Region = region;
            Endpoint = endpoint;
            Credential = credential;
        }
    }
}
