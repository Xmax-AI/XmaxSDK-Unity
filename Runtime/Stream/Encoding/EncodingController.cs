//
//  EncodingController.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>Applies resolved uplink encoder settings through the neutral RTC transport.</summary>
    internal sealed class EncodingController : IEncodingController
    {

        /// <summary>The transport owning the native encoder.</summary>
        private readonly IRtcManager _rtc;

        /// <summary>Creates an encoder controller without configuring native resources.</summary>
        internal EncodingController(
            IRtcManager rtc
        )
        {
            _rtc = rtc;
        }

        /// <inheritdoc/>
        public void Configure(
            RealtimeVideoFormat format
        ) => _rtc.ConfigureVideoEncoding(EncodingBitratePolicy.Resolve(format));
    }
}
