//
//  QualityController.cs
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
    /// Converts RTC ratings and owns independent quality policies and their model history.
    /// </summary>
    internal sealed class QualityController : IQualityController
    {

        /// <summary>The shared model sizing service used for final return dimensions.</summary>
        private readonly IMediaService _mediaService;

        /// <inheritdoc/>
        public event Action<RealtimeNetworkQuality, double> NetworkQualitySampled;

        /// <summary>The stable media baseline from which every transport format is derived.</summary>
        private RealtimeVideoFormat _baseline;

        /// <summary>The uplink policy, or null outside a prepared connection.</summary>
        private QualityDirection _uplink;

        /// <summary>The downlink policy, or null outside a prepared connection.</summary>
        private QualityDirection _downlink;

        /// <summary>The model-specific history used only when automatic adaptation is enabled.</summary>
        private QualityHistory _history;

        /// <summary>Whether local publication makes uplink statistics available.</summary>
        private bool _uplinkStatisticsEnabled;

        /// <summary>The application account whose downlink statistics are delivered.</summary>
        private string _downlinkStatisticsUser;

        /// <summary>The revision rejecting reentrant uplink reset callbacks.</summary>
        private long _uplinkStatisticsRevision;

        /// <summary>The revision rejecting reentrant downlink reset callbacks.</summary>
        private long _downlinkStatisticsRevision;

        /// <inheritdoc/>
        public event Action<UplinkVideoStatistics> UplinkVideoStatisticsChanged;

        /// <inheritdoc/>
        public event Action<DownlinkVideoStatistics> DownlinkVideoStatisticsChanged;

        /// <inheritdoc/>
        public event Action<RealtimePerformanceAlarm> PerformanceAlarm;

        /// <inheritdoc/>
        public RealtimeQualityConfiguration Configuration => _uplink == null || _downlink == null
            ? null : new RealtimeQualityConfiguration
            {
                Baseline = _baseline,
                UplinkLevel = _uplink.Level,
                DownlinkLevel = _downlink.Level,
                Uplink = ResolveFormat(
                    _uplink.Level,
                    false
                ),
                Downlink = ResolveFormat(
                    _downlink.Level,
                    true
                )
            };

        /// <summary>
        /// Occurs when converted directional network ratings are available.
        /// </summary>
        public event Action<RealtimeNetworkQuality> NetworkQualityChanged;

        /// <summary>
        /// Creates a quality controller with an optional RTC rating source.
        /// </summary>
        internal QualityController(
            IRtcManager rtc = null,
            IMediaService mediaService = null
        )
        {
            _mediaService = mediaService ?? new MediaService();
            if (rtc != null)
            {
                rtc.NetworkQualityChanged += Receive;
                rtc.UplinkVideoStatisticsChanged += statistics =>
                {
                    if (_uplinkStatisticsEnabled)
                        EventDispatch.Raise(
                            UplinkVideoStatisticsChanged,
                            statistics,
                            () => _uplinkStatisticsEnabled
                        );
                };
                rtc.DownlinkVideoStatisticsChanged += statistics =>
                {
                    if (statistics != null && statistics.UserId == _downlinkStatisticsUser)
                        EventDispatch.Raise(
                            DownlinkVideoStatisticsChanged,
                            statistics,
                            () => statistics.UserId == _downlinkStatisticsUser
                        );
                };
                rtc.PerformanceAlarm += alarm =>
                {
                    if (!_uplinkStatisticsEnabled)
                        return;
                    RealtimeVideoFormat? format = alarm.Width > 0 && alarm.Height > 0 && alarm.FrameRate > 0
                        ? new RealtimeVideoFormat(
                            alarm.Width,
                            alarm.Height,
                            alarm.FrameRate
                        ) : (RealtimeVideoFormat?)null;
                    EventDispatch.Raise(
                        PerformanceAlarm,
                        new RealtimePerformanceAlarm(
                            alarm.Limited ? RealtimePerformanceStatus.Limited : RealtimePerformanceStatus.Recovered,
                            format
                        ),
                        () => _uplinkStatisticsEnabled
                    );
                };
            }
        }

        /// <inheritdoc/>
        public RealtimeQualityConfiguration Prepare(
            RealtimeVideoFormat baseline,
            RealtimeConfiguration options
        )
        {
            _baseline = baseline;
            _history = options.IsAdaptiveQualityEnabled ? new QualityHistory(options.Definition.Name) : null;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _uplink = new QualityDirection(
                baseline,
                _history?.Read(
                    false,
                    now
                ) ?? RealtimeQualityLevel.L1,
                false,
                options.Definition.Capabilities?.MinimumPixels ?? 0
            );
            _downlink = new QualityDirection(
                baseline,
                _history?.Read(
                    true,
                    now
                ) ?? RealtimeQualityLevel.L1,
                true,
                0
            );
            return Configuration;
        }

        /// <inheritdoc/>
        public RealtimeVideoFormat ResolveFormat(
            RealtimeQualityLevel level,
            bool downlink
        )
        {
            if (_uplink == null || _downlink == null)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "Transport quality is not prepared."
                );
            QualityVideoFormat.ValidateLevel(level);
            return downlink ? QualityVideoFormat.ResolveTarget(
                _baseline,
                level,
                _mediaService
            ) : _uplink.Format(level);
        }

        /// <inheritdoc/>
        public QualityChange Propose(
            RealtimeNetworkQuality quality,
            double sampledAt
        )
        {
            var now = MonotonicTime();
            return new QualityChange(
                _uplink?.Observe(
                    quality.Uplink,
                    sampledAt,
                    now
                ),
                _downlink?.Observe(
                    quality.Downlink,
                    sampledAt,
                    now
                )
            );
        }

        /// <inheritdoc/>
        public void Complete(
            RealtimeQualityLevel level,
            bool downlink,
            bool success
        )
        {
            var direction = downlink ? _downlink : _uplink;
            if (direction == null)
                return;
            direction.Complete(
                level,
                success,
                MonotonicTime()
            );
            if (success)
                _history?.Write(
                    downlink,
                    level,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                );
        }

        /// <inheritdoc/>
        public void ResetSamples()
        {
            _uplink?.ResetSamples();
            _downlink?.ResetSamples();
        }

        /// <inheritdoc/>
        public void SetUplinkStatisticsEnabled(
            bool enabled
        )
        {
            var revision = ++_uplinkStatisticsRevision;
            _uplinkStatisticsEnabled = enabled;
            EventDispatch.Raise(
                UplinkVideoStatisticsChanged,
                (UplinkVideoStatistics)null,
                () => revision == _uplinkStatisticsRevision
            );
        }

        /// <inheritdoc/>
        public void SetDownlinkStatisticsUser(
            string userId
        )
        {
            if (_downlinkStatisticsUser == userId)
                return;
            var revision = ++_downlinkStatisticsRevision;
            _downlinkStatisticsUser = userId;
            EventDispatch.Raise(
                DownlinkVideoStatisticsChanged,
                (DownlinkVideoStatistics)null,
                () => revision == _downlinkStatisticsRevision
            );
        }

        /// <inheritdoc/>
        public void Reset()
        {
            SetUplinkStatisticsEnabled(false);
            SetDownlinkStatisticsUser(null);
            _uplink = null;
            _downlink = null;
            _history = null;
        }

        /// <summary>
        /// Converts ratings and delivers the public update on the creating Unity thread.
        /// </summary>
        private void Receive(
            RtcNetworkQuality quality
        )
        {
            var sampledAt = MonotonicTime();
            var converted = new RealtimeNetworkQuality(
                Level(quality.Uplink),
                Level(quality.Downlink),
                quality.PacketLossRatio,
                quality.RoundTripMilliseconds,
                quality.BandwidthBitsPerSecond
            );
            EventDispatch.Raise(
                NetworkQualitySampled,
                converted,
                sampledAt
            );
            EventDispatch.Raise(
                NetworkQualityChanged,
                converted
            );
        }

        /// <summary>
        /// Returns a public rating, using Unknown for unsupported native values.
        /// </summary>
        private static RealtimeNetworkQualityLevel Level(
            RtcQualityLevel level
        ) => Enum.IsDefined(
            typeof(RtcQualityLevel),
            level
        )
            ? (RealtimeNetworkQualityLevel)(int)level : RealtimeNetworkQualityLevel.Unknown;

        /// <summary>Returns monotonic seconds for policy sampling and stabilization.</summary>
        internal static double MonotonicTime() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
    }
}
