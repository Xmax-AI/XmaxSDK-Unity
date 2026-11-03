//
//  RoomMessageDecoder.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;
using System.Text;

namespace Xmax.SDK
{

    /// <summary>Reassembles bounded room messages while isolating fragments from different senders.</summary>
    internal sealed class RoomMessageDecoder
    {

        /// <summary>One bounded partial message, ordered for eviction.</summary>
        private sealed class Record
        {

            /// <summary>The expected number of fragments.</summary>
            internal int Count;

            /// <summary>The insertion sequence used to evict the oldest record.</summary>
            internal long Order;

            /// <summary>The retained original JSON slices indexed by position.</summary>
            internal readonly Dictionary<int, string> Parts = new Dictionary<int, string>();

            /// <summary>The total retained UTF-8 payload size.</summary>
            internal int Bytes;
        }

        /// <summary>The maximum UTF-8 size of one original room message.</summary>
        private const int MaximumMessageBytes = 262144;

        /// <summary>The UTF-8 decoder rejecting malformed byte sequences.</summary>
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        /// <summary>The partial messages indexed by sender and provider fragment identifier.</summary>
        private readonly Dictionary<(string Sender, string Id), Record> _records = new Dictionary<(string, string), Record>();

        /// <summary>The insertion order used by bounded eviction.</summary>
        private long _sequence;

        /// <summary>The aggregate retained UTF-8 payload size.</summary>
        private int _bytes;

        /// <summary>Decodes plain JSON or completes an out-of-order provider-fragment message.</summary>
        internal JsonValue Decode(
            string sender,
            byte[] data
        )
        {
            if (data == null || data.Length == 0 || data.Length > MaximumMessageBytes)
                return null;
            JsonValue message;
            try
            {
                message = Parse(Utf8.GetString(data));
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
            if (message == null || ReadString(message, "event") != "__agora_chunk__")
                return message;
            var id = ReadString(message, "eventId");
            var part = ReadString(message, "data");
            if (string.IsNullOrEmpty(id) || id.Length > 128 || part == null ||
                !TryInteger(message, "count", 1, 512, out var count) ||
                !TryInteger(message, "index", 0, count - 1, out var index))
                return null;
            var key = (sender, id);
            if (!_records.TryGetValue(key, out var record))
            {
                record = new Record { Count = (int)count, Order = ++_sequence };
                _records.Add(key, record);
            }
            if (record.Count != count || (record.Parts.TryGetValue((int)index, out var previous) && previous != part))
            {
                Remove(key);
                return null;
            }
            if (!record.Parts.ContainsKey((int)index))
            {
                record.Parts.Add((int)index, part);
                var added = Encoding.UTF8.GetByteCount(part);
                record.Bytes += added;
                _bytes += added;
            }
            if (record.Bytes > MaximumMessageBytes)
            {
                Remove(key);
                return null;
            }
            while (_records.Count > 1000 || _bytes > 32 * 1024 * 1024)
            {
                var oldest = default((string Sender, string Id));
                var order = long.MaxValue;
                foreach (var entry in _records)
                    if (entry.Value.Order < order)
                    {
                        oldest = entry.Key;
                        order = entry.Value.Order;
                    }
                Remove(oldest);
            }
            if (!_records.ContainsKey(key) || record.Parts.Count != record.Count)
                return null;
            Remove(key);
            var joined = new StringBuilder();
            for (var position = 0; position < record.Count; position++)
                joined.Append(record.Parts[position]);
            return Parse(joined.ToString());
        }

        /// <summary>Releases all fragments when the room is invalidated.</summary>
        internal void Reset()
        {
            _records.Clear();
            _bytes = 0;
            _sequence = 0;
        }

        /// <summary>Reads a named JSON string without coercing other scalar types.</summary>
        internal static string ReadString(
            JsonValue message,
            string key
        ) => message != null && message.IsObject && message.ContainsKey(key) &&
            message[key] != null && message[key].IsString ? (string)message[key] : null;

        /// <summary>Reads an exact integer within a safe, caller-specified range.</summary>
        internal static bool TryInteger(
            JsonValue message,
            string key,
            long minimum,
            long maximum,
            out long result
        )
        {
            result = 0;
            if (message == null || !message.IsObject || !message.ContainsKey(key))
                return false;
            var value = message[key];
            if (value == null || !value.IsNumber)
                return false;
            var number = (double)value;
            if (double.IsNaN(number) || double.IsInfinity(number) || number < minimum || number > maximum || number != Math.Floor(number))
                return false;
            result = (long)number;
            return true;
        }

        /// <summary>Parses a JSON object while ignoring malformed or nonobject messages.</summary>
        private static JsonValue Parse(
            string text
        )
        {
            try
            {
                var value = JsonCodec.Decode(text);
                return value != null && value.IsObject ? value : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Removes one partial message and updates retained byte accounting.</summary>
        private void Remove(
            (string Sender, string Id) key
        )
        {
            if (!_records.TryGetValue(key, out var record))
                return;
            _records.Remove(key);
            _bytes -= record.Bytes;
        }
    }
}
