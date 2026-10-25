//
//  QualityDirection.cs
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
    /// Collects directional samples and proposes changes without committing them.
    /// </summary>
    internal sealed class QualityDirection
    {

        /// <summary>
        /// The stable media format defining all levels.
        /// </summary>
        private readonly RealtimeVideoFormat _baseline;

        /// <summary>
        /// Whether this policy controls returned video.
        /// </summary>
        private readonly bool _downlink;

        /// <summary>
        /// The minimum uplink pixel area.
        /// </summary>
        private readonly int _minimumPixels;

        /// <summary>
        /// The consecutive good sample count.
        /// </summary>
        private int _good;

        /// <summary>
        /// The consecutive bad sample count.
        /// </summary>
        private int _poor;

        /// <summary>
        /// The most recent accepted monotonic sample time.
        /// </summary>
        private double? _lastSample;

        /// <summary>
        /// The deadline before further sample accumulation.
        /// </summary>
        private double _stableUntil;

        /// <summary>
        /// The earliest permitted upgrade time.
        /// </summary>
        private double _upgradeAfter;

        /// <summary>
        /// The post-upgrade observation deadline.
        /// </summary>
        private double _probeUntil;

        /// <summary>
        /// The successfully applied quality level.
        /// </summary>
        internal RealtimeQualityLevel Level { get; private set; }

        /// <summary>
        /// Creates a policy, raising unsupported initial uplink levels until the bitrate resolves.
        /// </summary>
        internal QualityDirection(
            RealtimeVideoFormat baseline,
            RealtimeQualityLevel level,
            bool downlink,
            int minimumPixels
        )
        {
            _baseline = baseline;
            _downlink = downlink;
            _minimumPixels = minimumPixels;
            Level = level;
            while (!Supports(Level) && Level > RealtimeQualityLevel.L1)
                Level--;
        }

        /// <summary>
        /// Returns a level's transport format.
        /// </summary>
        internal RealtimeVideoFormat Format(
            RealtimeQualityLevel level
        ) =>
            QualityVideoFormat.Resolve(
                _baseline,
                level,
                _downlink,
                _minimumPixels
            );

        /// <summary>
        /// Resets consecutive samples without changing cooldowns.
        /// </summary>
        internal void ResetSamples()
        {
            _good = 0;
            _poor = 0;
            _lastSample = null;
        }

        /// <summary>
        /// Proposes a change using valid, recent, monotonic samples.
        /// </summary>
        internal RealtimeQualityLevel? Observe(
            RealtimeNetworkQualityLevel quality,
            double sampledAt,
            double now
        )
        {
            if (double.IsNaN(sampledAt) || double.IsInfinity(sampledAt) || sampledAt > now ||
                (_lastSample.HasValue && sampledAt <= _lastSample.Value))
                return null;
            if (_lastSample.HasValue && sampledAt - _lastSample.Value > 5)
                ResetSamples();
            _lastSample = sampledAt;
            if (now - sampledAt > 5 || now < _stableUntil)
            {
                _good = 0;
                _poor = 0;
                return null;
            }
            int step;
            switch (quality)
            {
                case RealtimeNetworkQualityLevel.Bad:
                case RealtimeNetworkQualityLevel.VeryBad:
                    _good = 0;
                    if (++_poor < 3)
                        return null;
                    step = 1;
                    break;
                case RealtimeNetworkQualityLevel.Excellent:
                case RealtimeNetworkQualityLevel.Good:
                    _poor = 0;
                    if (++_good < 5 || now < _upgradeAfter)
                        return null;
                    step = -1;
                    break;
                default:
                    _good = 0;
                    _poor = 0;
                    return null;
            }
            for (var next = (int)Level + step; next >= 1 && next <= 5; next += step)
            {
                var candidate = (RealtimeQualityLevel)next;
                if (!Format(candidate).Equals(Format(Level)) && Supports(candidate))
                    return candidate;
            }
            return null;
        }

        /// <summary>
        /// Commits success and updates stabilization and upgrade probing deadlines.
        /// </summary>
        internal void Complete(
            RealtimeQualityLevel next,
            bool success,
            double now
        )
        {
            ResetSamples();
            _stableUntil = now + 4;
            if (!success)
                return;
            if (next < Level)
                _probeUntil = now + 20;
            else if (next > Level)
            {
                if (now < _probeUntil)
                    _upgradeAfter = now + 60;
                _probeUntil = 0;
            }
            Level = next;
        }

        /// <summary>
        /// Checks whether explicit bitrate bounds remain valid at a level.
        /// </summary>
        private bool Supports(
            RealtimeQualityLevel level
        )
        {
            if (_downlink)
                return true;
            try
            {
                EncodingBitratePolicy.Resolve(Format(level));
                return true;
            }
            catch (XmaxException)
            {
                return false;
            }
        }
    }
}
