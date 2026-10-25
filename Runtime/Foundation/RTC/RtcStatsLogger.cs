//
//  RtcStatsLogger.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System.Globalization;

namespace Xmax.SDK
{

    /// <summary>Formats actual RTC video statistics for performance logs.</summary>
    internal static class RtcStatsLogger
    {

        /// <summary>Logs uplink metrics only when performance logging is enabled.</summary>
        internal static void LogUplinkVideoStatistics(
            UplinkVideoStatistics video
        )
            => XmaxLogger.Rtc.Debug(
                () => UplinkVideoStatisticsMessage(video),
                XmaxLoggerOption.Performance
            );

        /// <summary>Logs downlink metrics only when performance logging is enabled.</summary>
        internal static void LogDownlinkVideoStatistics(
            DownlinkVideoStatistics video
        )
            => XmaxLogger.Rtc.Debug(
                () => DownlinkVideoStatisticsMessage(video),
                XmaxLoggerOption.Performance
            );

        /// <summary>Formats resolution, actual sending bitrate, frame rates, and uplink packet loss.</summary>
        internal static string UplinkVideoStatisticsMessage(
            UplinkVideoStatistics video
        ) =>
            "视频上行统计 (Video Uplink Statistics)\n"
            + "├─ " + XmaxLogger.Localized(
                "分辨率：",
                "Resolution: "
            ) + Resolution(
                video.Width,
                video.Height
            ) + "\n"
            + "├─ " + XmaxLogger.Localized(
                "发送码率：",
                "Send Bitrate: "
            ) + Value(
                video.BitrateKbps,
                "kbps"
            ) + "\n"
            + "├─ " + XmaxLogger.Localized(
                "采集帧率：",
                "Capture Frame Rate: "
            ) + Value(
                video.CaptureFrameRate,
                "fps"
            ) + "\n"
            + "├─ " + XmaxLogger.Localized(
                "编码帧率：",
                "Encode Frame Rate: "
            ) + Value(
                video.EncodeFrameRate,
                "fps"
            ) + "\n"
            + "├─ " + XmaxLogger.Localized(
                "发送帧率：",
                "Send Frame Rate: "
            ) + Value(
                video.SendFrameRate,
                "fps"
            ) + "\n"
            + "└─ " + XmaxLogger.Localized(
                "视频丢包率：",
                "Video Packet Loss: "
            ) + Value(
                video.PacketLossPercent,
                "%"
            );

        /// <summary>Formats resolution, actual receiving bitrate, frame rates, packet loss, and RTC media delay.</summary>
        internal static string DownlinkVideoStatisticsMessage(
            DownlinkVideoStatistics video
        ) =>
            "视频下行统计 (Video Downlink Statistics)\n"
            + "├─ " + XmaxLogger.Localized(
                "分辨率：",
                "Resolution: "
            ) + Resolution(
                video.Width,
                video.Height
            ) + "\n"
            + "├─ " + XmaxLogger.Localized(
                "接收码率：",
                "Receive Bitrate: "
            ) + Value(
                video.BitrateKbps,
                "kbps"
            ) + "\n"
            + "├─ " + XmaxLogger.Localized(
                "解码帧率：",
                "Decode Frame Rate: "
            ) + Value(
                video.DecodeFrameRate,
                "fps"
            ) + "\n"
            + "├─ " + XmaxLogger.Localized(
                "渲染帧率：",
                "Render Frame Rate: "
            ) + Value(
                video.RenderFrameRate,
                "fps"
            ) + "\n"
            + "├─ " + XmaxLogger.Localized(
                "视频丢包率：",
                "Video Packet Loss: "
            ) + Value(
                video.PacketLossPercent,
                "%"
            ) + "\n"
            + "└─ " + XmaxLogger.Localized(
                "端到端时延：",
                "End-to-End Delay: "
            ) + Value(
                video.EndToEndDelayMs,
                "ms"
            );

        /// <summary>Formats available video dimensions.</summary>
        private static string Resolution(
            int? width,
            int? height
        ) => width.HasValue && height.HasValue
            ? width.Value.ToString(CultureInfo.InvariantCulture) + " × " + height.Value.ToString(CultureInfo.InvariantCulture) : "—";

        /// <summary>Formats an available measurement with its unit, preserving valid zero values.</summary>
        private static string Value(
            int? value,
            string unit
        ) => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) + " " + unit : "—";
    }
}
