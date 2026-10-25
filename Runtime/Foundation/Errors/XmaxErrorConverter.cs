//
//  XmaxErrorConverter.cs
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

    /// <summary>Normalizes exceptions without depending on realtime lifecycle or transport components.</summary>
    internal static class XmaxErrorConverter
    {

        /// <summary>Preserves SDK diagnostics and reuses a known error when its severity already matches.</summary>
        internal static XmaxException Wrap(
            Exception exception,
            XmaxErrorSeverity severity = XmaxErrorSeverity.Recoverable
        )
        {
            var known = exception as XmaxException;
            if (known != null && known.Severity == severity)
                return known;
            return new XmaxException(
                known?.Code ?? XmaxErrorCode.RtcError,
                exception.Message,
                known?.ApiCode,
                known?.HttpStatus,
                exception,
                severity
            );
        }
    }
}
