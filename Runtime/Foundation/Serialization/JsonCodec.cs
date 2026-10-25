//
//  JsonCodec.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;
using Agora.Rtc.LitJson;

namespace Xmax.SDK
{

    /// <summary>
    /// Isolates the bundled JSON implementation from SDK business layers.
    /// </summary>
    internal static class JsonCodec
    {

        /// <summary>
        /// Encodes a JSON object or array.
        /// </summary>
        internal static string Encode(
            object value
        ) => JsonMapper.ToJson(value);

        /// <summary>
        /// Parses JSON into a neutral SDK value.
        /// </summary>
        internal static JsonValue Decode(
            string text
        ) => JsonValue.Wrap(JsonMapper.ToObject(text));

        /// <summary>Formats sorted diagnostic JSON while redacting authentication fields.</summary>
        internal static string FormatForLogging(
            string text
        )
        {
            try
            {
                var writer = new JsonWriter { PrettyPrint = true, IndentValue = 2 };
                JsonMapper.ToJson(
                    LogValue(JsonMapper.ToObject(text)),
                    writer
                );
                return writer.ToString();
            }
            catch
            {
                return LogRedaction.Text(text);
            }
        }

        /// <summary>Copies JSON into sorted diagnostic values without credential contents.</summary>
        private static object LogValue(
            JsonData value
        )
        {
            if (value == null)
                return null;
            if (value.IsObject)
            {
                var result = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (var key in value.Keys)
                    result[key] = LogRedaction.IsCredential(key) ? "<redacted>" : LogValue(value[key]);
                return result;
            }
            if (value.IsArray)
            {
                var result = new List<object>();
                for (var index = 0; index < value.Count; index++)
                    result.Add(LogValue(value[index]));
                return result;
            }
            if (value.IsString)
                return LogRedaction.Text((string)value);
            if (value.IsBoolean)
                return (bool)value;
            if (value.IsInt)
                return (int)value;
            if (value.IsLong)
                return (long)value;
            if (value.IsUInt)
                return (uint)value;
            if (value.IsULong)
                return (ulong)value;
            if (value.IsDouble)
                return (double)value;
            return null;
        }
    }

    /// <summary>
    /// A neutral JSON value supporting object lookup and checked scalar conversions.
    /// </summary>
    internal sealed class JsonValue
    {

        /// <summary>
        /// The wrapped serialization-library value.
        /// </summary>
        private readonly JsonData _data;

        /// <summary>
        /// Wraps a serialization-library value.
        /// </summary>
        private JsonValue(
            JsonData data
        )
        {
            _data = data;
        }

        /// <summary>
        /// Returns a wrapped value, preserving JSON null as a null reference.
        /// </summary>
        internal static JsonValue Wrap(
            JsonData data
        ) => data == null ? null : new JsonValue(data);

        /// <summary>
        /// Whether this value is a JSON object.
        /// </summary>
        internal bool IsObject => _data.IsObject;

        /// <summary>
        /// Whether this value is a JSON string.
        /// </summary>
        internal bool IsString => _data.IsString;

        /// <summary>
        /// Whether this value is a JSON Boolean.
        /// </summary>
        internal bool IsBoolean => _data.IsBoolean;

        /// <summary>
        /// Whether this value is a 32-bit JSON integer.
        /// </summary>
        internal bool IsInt => _data.IsInt;

        /// <summary>
        /// Whether this value is a 64-bit JSON integer.
        /// </summary>
        internal bool IsLong => _data.IsLong;

        /// <summary>
        /// Checks whether a JSON object contains a named member.
        /// </summary>
        internal bool ContainsKey(
            string key
        ) => _data.IsObject && _data.ContainsKey(key);

        /// <summary>
        /// The named object member; callers must validate the object and key first.
        /// </summary>
        internal JsonValue this[string key] => Wrap(_data[key]);

        /// <summary>
        /// Converts a JSON string, rejecting other value types.
        /// </summary>
        public static explicit operator string(
            JsonValue value
        ) => (string)value._data;

        /// <summary>
        /// Converts a JSON Boolean, rejecting other value types.
        /// </summary>
        public static explicit operator bool(
            JsonValue value
        ) => (bool)value._data;

        /// <summary>
        /// Converts a 32-bit JSON integer, rejecting other value types.
        /// </summary>
        public static explicit operator int(
            JsonValue value
        ) => (int)value._data;

        /// <summary>
        /// Converts a 64-bit JSON integer, rejecting other value types.
        /// </summary>
        public static explicit operator long(
            JsonValue value
        ) => (long)value._data;
    }
}
