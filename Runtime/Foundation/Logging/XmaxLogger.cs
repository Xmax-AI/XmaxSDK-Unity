//
//  XmaxLogger.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Threading;
using UnityEngine;

namespace Xmax.SDK
{

    /// <summary>The process-wide categories of SDK logs; values may be combined.</summary>
    [Flags]
    public enum XmaxLoggerOption
    {

        /// <summary>Disables SDK logs.</summary>
        None = 0,

        /// <summary>Service, connection, media, and signaling logs.</summary>
        Business = 1 << 0,

        /// <summary>RTC statistics and startup performance logs.</summary>
        Performance = 1 << 1,

        /// <summary>Enables all SDK log categories.</summary>
        All = Business | Performance
    }

    /// <summary>A lazy logger with SDK and domain prefixes and shared filtering.</summary>
    internal readonly struct XmaxLogger
    {

        /// <summary>The language used for log details.</summary>
        internal enum Language
        {

            /// <summary>Chinese detail labels.</summary>
            Chinese,

            /// <summary>English detail labels.</summary>
            English
        }

        /// <summary>Realtime lifecycle logs.</summary>
        internal static readonly XmaxLogger Realtime = new XmaxLogger("Realtime");

        /// <summary>RTC transport and statistics logs.</summary>
        internal static readonly XmaxLogger Rtc = new XmaxLogger("RTC");

        /// <summary>Media input and processing logs.</summary>
        internal static readonly XmaxLogger Media = new XmaxLogger("Media");

        /// <summary>HTTP API logs.</summary>
        internal static readonly XmaxLogger Api = new XmaxLogger("API");

        /// <summary>Object storage logs.</summary>
        internal static readonly XmaxLogger Storage = new XmaxLogger("Storage");

        /// <summary>Room signaling and heartbeat logs.</summary>
        internal static readonly XmaxLogger Room = new XmaxLogger("Room");

        /// <summary>Stream publishing, subscription, and quality logs.</summary>
        internal static readonly XmaxLogger Stream = new XmaxLogger("Stream");

        /// <summary>Video rendering logs.</summary>
        internal static readonly XmaxLogger Render = new XmaxLogger("Render");

        /// <summary>Trajectory interaction logs.</summary>
        internal static readonly XmaxLogger Interaction = new XmaxLogger("Interaction");

        /// <summary>System permission logs.</summary>
        internal static readonly XmaxLogger Permission = new XmaxLogger("Permission");

        /// <summary>The fixed domain name for this logger.</summary>
        private readonly string _category;

        /// <summary>The immutable global logging configuration, replaced atomically.</summary>
        private static Configuration _configuration = new Configuration(
            XmaxLoggerOption.None,
            Language.Chinese
        );

        /// <summary>Creates a logger for one domain.</summary>
        private XmaxLogger(
            string category
        )
        {
            _category = category;
        }

        /// <summary>Atomically replaces the global log options and detail language.</summary>
        internal static void Configure(
            XmaxLoggerOption options,
            Language language = Language.Chinese
        )
            => Volatile.Write(
                ref _configuration,
                new Configuration(
                    options,
                    language
                )
            );

        /// <summary>Selects the detail label for the current log language.</summary>
        internal static string Localized(
            string chinese,
            string english
        )
            => Volatile.Read(ref _configuration).Language == Language.Chinese ? chinese : english;

        /// <summary>Returns whether every requested nonempty option is enabled.</summary>
        internal static bool IsEnabled(
            XmaxLoggerOption option
        )
            => option != XmaxLoggerOption.None && (Volatile.Read(ref _configuration).Options & option) == option;

        /// <summary>Writes a debug message only when its filter is enabled.</summary>
        internal void Debug(
            Func<string> message,
            XmaxLoggerOption option = XmaxLoggerOption.Business
        )
            => Write(
                LogType.Log,
                message,
                option
            );

        /// <summary>Writes an informational message only when its filter is enabled.</summary>
        internal void Info(
            Func<string> message,
            XmaxLoggerOption option = XmaxLoggerOption.Business
        )
            => Write(
                LogType.Log,
                message,
                option
            );

        /// <summary>Writes a warning only when its filter is enabled.</summary>
        internal void Warn(
            Func<string> message,
            XmaxLoggerOption option = XmaxLoggerOption.Business
        )
            => Write(
                LogType.Warning,
                message,
                option
            );

        /// <summary>Writes an error only when its filter is enabled.</summary>
        internal void Error(
            Func<string> message,
            XmaxLoggerOption option = XmaxLoggerOption.Business
        )
            => Write(
                LogType.Error,
                message,
                option
            );

        /// <summary>Writes a structured operation failure including its diagnostic code.</summary>
        internal void Failure(
            Exception exception,
            XmaxLoggerOption option = XmaxLoggerOption.Business
        )
            => Error(
                () => "操作失败 (Operation Failed)\n├─ " + Localized(
                    "错误码：",
                    "Error Code: "
                ) + ErrorCode(exception)
                + "\n└─ " + Localized(
                    "原因：",
                    "Reason: "
                ) + ErrorMessageFormatter.Format(exception),
                option
            );

        /// <summary>Returns a diagnostic category without evaluating the exception's message.</summary>
        internal static string ErrorCode(
            Exception exception
        ) => exception is XmaxException known ? known.Code.ToString() :
            exception is OperationCanceledException ? "Cancelled" : exception?.GetType().Name ?? "Unknown";

        /// <summary>Adds SDK and domain prefixes to every message line.</summary>
        internal string FormattedMessage(
            string message
        )
        {
            var prefix = "[Xmax][" + _category + "] ";
            return prefix + message.Replace(
                "\n",
                "\n" + prefix
            );
        }

        /// <summary>Filters before formatting and isolates failures in application log handlers.</summary>
        private void Write(
            LogType level,
            Func<string> message,
            XmaxLoggerOption option
        )
        {
            if (!IsEnabled(option))
                return;
            try
            {
                var formatted = FormattedMessage(LogRedaction.Text(message()));
                if (level == LogType.Error)
                    UnityEngine.Debug.LogError(formatted);
                else if (level == LogType.Warning)
                    UnityEngine.Debug.LogWarning(formatted);
                else
                    UnityEngine.Debug.Log(formatted);
            }
            catch
            {
            }
        }

        /// <summary>An atomic snapshot of global logging preferences.</summary>
        private sealed class Configuration
        {

            /// <summary>The enabled option bitmask.</summary>
            internal readonly XmaxLoggerOption Options;

            /// <summary>The language of detail labels.</summary>
            internal readonly Language Language;

            /// <summary>Creates an immutable preference snapshot.</summary>
            internal Configuration(
                XmaxLoggerOption options,
                Language language
            )
            {
                Options = options;
                Language = language;
            }
        }
    }
}
