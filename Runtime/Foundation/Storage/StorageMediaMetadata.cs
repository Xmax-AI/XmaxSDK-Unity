//
//  StorageMediaMetadata.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Globalization;
using System.IO;
using System.Text;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
#endif

namespace Xmax.SDK
{
    /// <summary>Reads upload dimensions without decoding pixels or starting a media source.</summary>
    ///
    /// <remarks>Metadata is best effort. Unsupported or malformed media returns -- and cannot fail an upload.</remarks>
    internal static class StorageMediaMetadata
    {

        /// <summary>Returns image dimensions or a file video's oriented dimensions for diagnostic logging.</summary>
        internal static string Resolution(
            StorageUploadSource source,
            StorageMediaType mediaType
        )
        {
            try
            {
                if (mediaType == StorageMediaType.Video && source.FilePath == null)
                    return "--";

#if UNITY_ANDROID && !UNITY_EDITOR
                var native = AndroidResolution(
                    source,
                    mediaType
                );
                if (native != "--")
                    return native;
#endif
                using (var stream = source.FilePath != null
                    ? (Stream)File.OpenRead(source.FilePath)
                    : new MemoryStream(
                        source.Data,
                        false
                    ))
                using (var reader = new BinaryReader(stream))
                {
                    return mediaType == StorageMediaType.Image ? ImageResolution(reader) : VideoResolution(reader);
                }
            }
            catch (Exception)
            {
                return "--";
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR

        /// <summary>Uses Android metadata APIs without creating a decoder surface or loading image pixels.</summary>
        private static string AndroidResolution(
            StorageUploadSource source,
            StorageMediaType mediaType
        )
        {
            try
            {
                if (mediaType == StorageMediaType.Image)
                {
                    using (var factory = new AndroidJavaClass("android.graphics.BitmapFactory"))
                    using (var options = new AndroidJavaObject("android.graphics.BitmapFactory$Options"))
                    {
                        options.Set(
                            "inJustDecodeBounds",
                            true
                        );
                        using (var bitmap = source.FilePath != null
                            ? factory.CallStatic<AndroidJavaObject>(
                                "decodeFile",
                                source.FilePath,
                                options
                            )
                            : factory.CallStatic<AndroidJavaObject>(
                                "decodeByteArray",
                                source.Data,
                                0,
                                source.Data.Length,
                                options
                            ))
                        {
                            return Dimensions(
                                options.Get<int>("outWidth"),
                                options.Get<int>("outHeight")
                            );
                        }
                    }
                }

                using (var metadata = new AndroidJavaObject("android.media.MediaMetadataRetriever"))
                {
                    try
                    {
                        metadata.Call(
                            "setDataSource",
                            source.FilePath
                        );
                        int.TryParse(
                            metadata.Call<string>(
                                "extractMetadata",
                                18
                            ),
                            out var width
                        );
                        int.TryParse(
                            metadata.Call<string>(
                                "extractMetadata",
                                19
                            ),
                            out var height
                        );
                        int.TryParse(
                            metadata.Call<string>(
                                "extractMetadata",
                                24
                            ),
                            out var rotation
                        );
                        return rotation == 90 || rotation == 270
                            ? Dimensions(
                                height,
                                width
                            )
                            : Dimensions(
                                width,
                                height
                            );
                    }
                    finally
                    {
                        metadata.Call("release");
                    }
                }
            }
            catch (Exception)
            {
                return "--";
            }
        }
#endif

        /// <summary>Reads common image headers while leaving compressed pixel data untouched.</summary>
        private static string ImageResolution(
            BinaryReader reader
        )
        {
            var header = reader.ReadBytes(32);
            if (header.Length >= 24 && header[0] == 137 && Encoding.ASCII.GetString(
                header,
                1,
                7
            ) == "PNG\r\n\x1a\n")
                return Dimensions(
                    BigEndian(
                        header,
                        16
                    ),
                    BigEndian(
                        header,
                        20
                    )
                );
            if (header.Length >= 10 && Encoding.ASCII.GetString(
                header,
                0,
                3
            ) == "GIF")
                return Dimensions(
                    LittleEndian(
                        header,
                        6,
                        2
                    ),
                    LittleEndian(
                        header,
                        8,
                        2
                    )
                );
            if (header.Length >= 26 && header[0] == 'B' && header[1] == 'M' && LittleEndian(
                header,
                14,
                4
            ) >= 40)
                return Dimensions(
                    LittleEndian(
                        header,
                        18,
                        4
                    ),
                    Math.Abs((long)(int)LittleEndian(
                            header,
                            22,
                            4
                        ))
                );
            if (header.Length >= 30 && Encoding.ASCII.GetString(
                header,
                0,
                4
            ) == "RIFF" &&
                Encoding.ASCII.GetString(
                    header,
                    8,
                    4
                ) == "WEBP")
            {
                var type = Encoding.ASCII.GetString(
                    header,
                    12,
                    4
                );
                if (type == "VP8X")
                    return Dimensions(
                        1 + LittleEndian(
                            header,
                            24,
                            3
                        ),
                        1 + LittleEndian(
                            header,
                            27,
                            3
                        )
                    );
                if (type == "VP8L" && header[20] == 0x2f)
                {
                    var packed = LittleEndian(
                        header,
                        21,
                        4
                    );
                    return Dimensions(
                        1 + (packed & 0x3fff),
                        1 + ((packed >> 14) & 0x3fff)
                    );
                }
                if (type == "VP8 " && header[23] == 0x9d && header[24] == 1 && header[25] == 0x2a)
                    return Dimensions(
                        LittleEndian(
                            header,
                            26,
                            2
                        ) & 0x3fff,
                        LittleEndian(
                            header,
                            28,
                            2
                        ) & 0x3fff
                    );
            }
            if (header.Length < 2 || header[0] != 0xff || header[1] != 0xd8)
                return "--";

            reader.BaseStream.Position = 2;
            for (var segments = 0; segments < 4096 && reader.BaseStream.Position < reader.BaseStream.Length; segments++)
            {
                if (reader.ReadByte() != 0xff)
                    return "--";
                byte marker;
                do
                    marker = reader.ReadByte();
                while (marker == 0xff);
                if (marker == 0xd9 || marker == 0xda)
                    return "--";
                if (marker == 1 || (marker >= 0xd0 && marker <= 0xd8))
                    continue;
                var length = (reader.ReadByte() << 8) | reader.ReadByte();
                if (length < 2 || length - 2 > reader.BaseStream.Length - reader.BaseStream.Position)
                    return "--";
                if (length >= 7 && marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc)
                {
                    reader.ReadByte();
                    var height = (reader.ReadByte() << 8) | reader.ReadByte();
                    var width = (reader.ReadByte() << 8) | reader.ReadByte();
                    return Dimensions(
                        width,
                        height
                    );
                }
                reader.BaseStream.Seek(
                    length - 2,
                    SeekOrigin.Current
                );
            }
            return "--";
        }

        /// <summary>Finds the first video track in an ISO media file, skipping media payloads by seeking.</summary>
        private static string VideoResolution(
            BinaryReader reader
        )
        {
            var budget = 4096;
            while (Box(
                reader,
                reader.BaseStream.Length,
                ref budget,
                out var type,
                out var end
            ))
            {
                if (type == "moov")
                {
                    while (Box(
                        reader,
                        end,
                        ref budget,
                        out var child,
                        out var childEnd
                    ))
                    {
                        if (child == "trak")
                        {
                            var resolution = VideoTrack(
                                reader,
                                childEnd,
                                ref budget
                            );
                            if (resolution != "--")
                                return resolution;
                        }
                        reader.BaseStream.Position = childEnd;
                    }
                }
                reader.BaseStream.Position = end;
            }
            return "--";
        }

        /// <summary>Reads a video track's display matrix and dimensions, ignoring audio and metadata tracks.</summary>
        private static string VideoTrack(
            BinaryReader reader,
            long trackEnd,
            ref int budget
        )
        {
            var video = false;
            var resolution = "--";
            while (Box(
                reader,
                trackEnd,
                ref budget,
                out var type,
                out var end
            ))
            {
                var start = reader.BaseStream.Position;
                if (type == "tkhd" && end - start >= 84)
                {
                    var version = reader.ReadByte();
                    var matrix = start + (version == 0 ? 40 : 52);
                    if (version <= 1 && end - matrix >= 44)
                    {
                        reader.BaseStream.Position = matrix;
                        var a = (int)ReadBigEndian(reader) / 65536.0;
                        var b = (int)ReadBigEndian(reader) / 65536.0;
                        ReadBigEndian(reader);
                        var c = (int)ReadBigEndian(reader) / 65536.0;
                        var d = (int)ReadBigEndian(reader) / 65536.0;
                        reader.BaseStream.Position = matrix + 36;
                        var width = ReadBigEndian(reader) / 65536.0;
                        var height = ReadBigEndian(reader) / 65536.0;
                        resolution = Dimensions(
                            (long)Math.Round(Math.Abs(width * a + height * c), MidpointRounding.AwayFromZero),
                            (long)Math.Round(Math.Abs(width * b + height * d), MidpointRounding.AwayFromZero)
                        );
                    }
                }
                else if (type == "mdia")
                {
                    while (Box(
                        reader,
                        end,
                        ref budget,
                        out var child,
                        out var childEnd
                    ))
                    {
                        if (child == "hdlr" && childEnd - reader.BaseStream.Position >= 12)
                        {
                            reader.BaseStream.Seek(
                                8,
                                SeekOrigin.Current
                            );
                            video = Encoding.ASCII.GetString(reader.ReadBytes(4)) == "vide";
                        }
                        reader.BaseStream.Position = childEnd;
                    }
                }
                reader.BaseStream.Position = end;
            }
            return video ? resolution : "--";
        }

        /// <summary>Reads a bounded ISO box header, rejecting lengths outside its enclosing container.</summary>
        private static bool Box(
            BinaryReader reader,
            long parentEnd,
            ref int budget,
            out string type,
            out long end
        )
        {
            type = null;
            end = 0;
            var start = reader.BaseStream.Position;
            if (--budget < 0 || parentEnd - start < 8)
                return false;
            long size = ReadBigEndian(reader);
            type = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (size == 1)
            {
                if (parentEnd - reader.BaseStream.Position < 8)
                    return false;
                var large = ((ulong)ReadBigEndian(reader) << 32) | ReadBigEndian(reader);
                if (large > long.MaxValue)
                    return false;
                size = (long)large;
            }
            else if (size == 0)
                size = parentEnd - start;
            if (size < reader.BaseStream.Position - start || size > parentEnd - start)
                return false;
            end = start + size;
            return true;
        }

        /// <summary>Reads an unsigned four-byte big-endian integer.</summary>
        private static uint ReadBigEndian(
            BinaryReader reader
        ) => (uint)((reader.ReadByte() << 24) | (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte());

        /// <summary>Reads an unsigned four-byte big-endian integer from a validated header.</summary>
        private static long BigEndian(
            byte[] bytes,
            int offset
        ) => ((long)bytes[offset] << 24) | ((long)bytes[offset + 1] << 16) | ((long)bytes[offset + 2] << 8) | bytes[offset + 3];

        /// <summary>Reads an unsigned little-endian integer from a validated header.</summary>
        private static long LittleEndian(
            byte[] bytes,
            int offset,
            int count
        )
        {
            long value = 0;
            for (var i = 0; i < count; i++)
                value |= (long)bytes[offset + i] << (i * 8);
            return value;
        }

        /// <summary>Formats positive pixel dimensions or an unavailable metadata marker.</summary>
        private static string Dimensions(
            long width,
            long height
        ) => width > 0 && width <= int.MaxValue && height > 0 && height <= int.MaxValue
            ? width.ToString(CultureInfo.InvariantCulture) + " × " + height.ToString(CultureInfo.InvariantCulture) : "--";
    }
}
