//
//  XmaxRealtimeErrorManager.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>
    /// Normalizes SDK failures and observes background lifecycle cleanup.
    /// </summary>
    internal sealed class XmaxRealtimeErrorManager
    {

        /// <summary>The active facade's failure cleanup route.</summary>
        private Func<XmaxException, TerminationScope, Task> _failureHandler;

        /// <summary>The observed cleanup task for the latest forwarded background failure.</summary>
        private Task _cleanup = Task.CompletedTask;

        /// <summary>Binds failure cleanup after the complete component graph has been assigned.</summary>
        internal void SetFailureHandler(
            Func<XmaxException, TerminationScope, Task> handler
        ) => _failureHandler = handler;

        /// <summary>Routes a background failure and observes cleanup without an async-void path.</summary>
        internal void Forward(
            XmaxException error
        )
            => Forward(
                error,
                TerminationScope.Connection
            );

        /// <summary>Routes a failure to connection or complete media cleanup and observes its completion.</summary>
        internal void Forward(
            XmaxException error,
            TerminationScope scope
        )
        {
            Report(error);
            try
            {
                _cleanup = ObserveCleanupAsync(_failureHandler?.Invoke(
                        error,
                        scope
                    ) ?? Task.CompletedTask);
            }
            catch (Exception exception)
            {
                Report(exception);
            }
        }

        /// <summary>Observes background cleanup failures independently of public event subscribers.</summary>
        private static async Task ObserveCleanupAsync(
            Task cleanup
        )
        {
            try
            {
                await cleanup;
            }
            catch (Exception exception)
            {
                Report(exception);
            }
        }

        /// <summary>Logs a realtime failure with its category and diagnostic details.</summary>
        internal static void Report(
            Exception error
        )
        {
            XmaxLogger.Realtime.Error(() => "实时服务错误 (Realtime Service Error)\n"
                + "├─ " + XmaxLogger.Localized(
                    "错误码：",
                    "Error Code: "
                ) + XmaxLogger.ErrorCode(error) + "\n"
                + "└─ " + XmaxLogger.Localized(
                    "信息：",
                    "Message: "
                ) + ErrorMessageFormatter.Format(error));
        }

        /// <summary>
        /// Preserves known SDK error details while applying the requested severity.
        /// </summary>
        internal static XmaxException Wrap(
            Exception exception,
            XmaxErrorSeverity severity = XmaxErrorSeverity.Recoverable
        )
        {
            return XmaxErrorConverter.Wrap(
                exception,
                severity
            );
        }
    }
}
