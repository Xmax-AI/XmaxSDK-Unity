//
//  StorageUploadSource.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.IO;

namespace Xmax.SDK
{

    /// <summary>The borrowed byte data or existing file streamed by one storage upload.</summary>
    internal sealed class StorageUploadSource
    {

        /// <summary>The borrowed data, or null when the source is a file.</summary>
        internal byte[] Data { get; }

        /// <summary>The absolute source path, or null when the source is byte data.</summary>
        internal string FilePath { get; }

        /// <summary>The source size measured before requesting credentials.</summary>
        internal long ByteCount { get; }

        /// <summary>Creates a source descriptor without loading file contents into memory.</summary>
        private StorageUploadSource(
            byte[] data,
            string filePath,
            long byteCount
        )
        {
            Data = data;
            FilePath = filePath;
            ByteCount = byteCount;
        }

        /// <summary>Requires nonempty byte data that remains unchanged until upload completes.</summary>
        internal static StorageUploadSource FromData(
            byte[] data
        )
        {
            if (data == null || data.Length == 0)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Upload data cannot be empty."
                );
            return new StorageUploadSource(
                data,
                null,
                data.LongLength
            );
        }

        /// <summary>Requires an existing local file and resolves its absolute path.</summary>
        internal static StorageUploadSource FromFile(
            string filePath
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    throw new XmaxException(
                        XmaxErrorCode.InvalidConfiguration,
                        "Upload path must reference an existing file."
                    );
                var path = Path.GetFullPath(filePath);
                return new StorageUploadSource(
                    null,
                    path,
                    new FileInfo(path).Length
                );
            }
            catch (XmaxException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Upload file is unavailable.",
                    innerException: error
                );
            }
        }
    }
}
