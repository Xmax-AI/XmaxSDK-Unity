//
//  AgoraRtcPlatform.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using Agora.Rtc;

namespace Xmax.SDK
{

    /// <summary>Owns the Agora native engine, external input, and encoder configuration.</summary>
    internal sealed class AgoraRtcPlatform : IRtcPlatform
    {

        /// <summary>The native engine, cleared after disposal.</summary>
        private IRtcEngine _engine;

        /// <summary>The reusable external frame packer.</summary>
        private readonly RtcVideoConverter _video = new RtcVideoConverter();

        /// <summary>Initializes the fixed Agora application with camera and microphone capture disabled.</summary>
        internal AgoraRtcPlatform()
        {
            _engine = Agora.Rtc.RtcEngine.CreateAgoraRtcEngine();
            try
            {
                Check(
                    _engine.Initialize(new RtcEngineContext
                    {
                        appId = RtcEngineManager.DefaultAppId,
                        channelProfile = CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_LIVE_BROADCASTING
                    }),
                    "Initialize"
                );
                Check(
                    _engine.EnableVideo(),
                    "EnableVideo"
                );
                Check(
                    _engine.EnableLocalVideo(false),
                    "DisableCamera"
                );
                Check(
                    _engine.EnableLocalAudio(false),
                    "DisableMicrophone"
                );
                Check(
                    _engine.SetExternalVideoSource(
                        true,
                        false,
                        EXTERNAL_VIDEO_SOURCE_TYPE.VIDEO_FRAME,
                        new SenderOptions()
                    ),
                    "SetExternalVideoSource"
                );
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        public void ConfigureVideoEncoding(
            VideoEncodingConfiguration configuration
        ) =>
            Check(
                _engine.SetVideoEncoderConfiguration(ToVideoEncoderConfig(configuration)),
                "SetVideoEncoderConfiguration"
            );

        /// <inheritdoc/>
        public void PushFrame(
            XmaxVideoFrame frame
        ) => Check(
            _engine.PushVideoFrame(_video.Convert(frame)),
            "PushVideoFrame"
        );

        /// <inheritdoc/>
        public IRtcRoom CreateRoom()
        {
            return new AgoraRtcRoom(_engine);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            var engine = _engine;
            _engine = null;
            engine?.Dispose(true);
        }

        /// <summary>
        /// Converts neutral encoding parameters to Agora's adaptive-orientation configuration.
        /// </summary>
        internal static VideoEncoderConfiguration ToVideoEncoderConfig(
            VideoEncodingConfiguration configuration
        )
        {
            return new VideoEncoderConfiguration
            {
                dimensions = new VideoDimensions(
                    configuration.Width,
                    configuration.Height
                ),
                frameRate = configuration.FrameRate,
                bitrate = configuration.MaximumBitrate,
                minBitrate = configuration.MinimumBitrate,
                orientationMode = ORIENTATION_MODE.ORIENTATION_MODE_ADAPTIVE,
                mirrorMode = VIDEO_MIRROR_MODE_TYPE.VIDEO_MIRROR_MODE_DISABLED,
                degradationPreference = configuration.EncoderPreference == RealtimeVideoEncoderPreference.MaintainFramerate
                    ? DEGRADATION_PREFERENCE.MAINTAIN_FRAMERATE
                    : configuration.EncoderPreference == RealtimeVideoEncoderPreference.MaintainQuality
                        ? DEGRADATION_PREFERENCE.MAINTAIN_QUALITY : DEGRADATION_PREFERENCE.MAINTAIN_AUTO
            };
        }

        /// <summary>
        /// Converts a negative native return code to an SDK error.
        /// </summary>
        internal static void Check(
            int result,
            string operation
        )
        {
            if (result < 0)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    operation + " failed: " + result + "."
                );
        }
    }
}
