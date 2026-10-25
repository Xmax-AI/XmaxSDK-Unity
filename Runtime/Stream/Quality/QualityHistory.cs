//
//  QualityHistory.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Globalization;
using UnityEngine;

namespace Xmax.SDK
{

    /// <summary>
    /// Stores successfully applied levels for one hour, isolated by model and direction.
    /// </summary>
    internal sealed class QualityHistory
    {

        /// <summary>
        /// The selected server model identifier.
        /// </summary>
        private readonly string _model;

        /// <summary>
        /// The preference reader, replaceable for deterministic tests.
        /// </summary>
        private readonly Func<string, string> _read;

        /// <summary>
        /// The preference writer, replaceable for deterministic tests.
        /// </summary>
        private readonly Action<string, string> _write;

        /// <summary>
        /// Creates a preference-backed quality history.
        /// </summary>
        internal QualityHistory(
            string model,
            Func<string, string> read = null,
            Action<string, string> write = null
        )
        {
            _model = model;
            _read = read ?? (key => PlayerPrefs.GetString(
                key,
                ""
            ));
            _write = write ?? PlayerPrefs.SetString;
        }

        /// <summary>
        /// Returns valid history or L2 when absent, expired, or dated in the future.
        /// </summary>
        internal RealtimeQualityLevel Read(
            bool downlink,
            double now
        )
        {
            var parts = _read(Key(downlink)).Split('|');
            if (parts.Length == 2 && int.TryParse(
                parts[0],
                out var level
            ) && level >= 1 && level <= 5 &&
                double.TryParse(
                    parts[1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var time
                ) &&
                time > 0 && now >= time && now - time < 3600)
                return (RealtimeQualityLevel)level;
            return RealtimeQualityLevel.L2;
        }

        /// <summary>
        /// Records a successfully applied level with a Unix timestamp in seconds.
        /// </summary>
        internal void Write(
            bool downlink,
            RealtimeQualityLevel level,
            double now
        )
        {
            _write(
                Key(downlink),
                ((int)level).ToString(CultureInfo.InvariantCulture) + "|" + now.ToString(
                    "R",
                    CultureInfo.InvariantCulture
                )
            );
        }

        /// <summary>
        /// Returns the model-specific directional preference key.
        /// </summary>
        private string Key(
            bool downlink
        ) => "xmax.quality.agora." + _model + (downlink ? ".downlink" : ".uplink");
    }
}
