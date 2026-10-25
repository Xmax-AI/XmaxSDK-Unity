//
//  XmaxRealtimeConnectionManager.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>Owns the server session, RTC connection, transport quality execution, heartbeat, and rollback.</summary>
    internal sealed class XmaxRealtimeConnectionManager
    {

        /// <summary>The server session creation, renewal, and closure service.</summary>
        private readonly IRealtimeSessionService _sessions;

        /// <summary>The room, publication, and signaling coordinator.</summary>
        private readonly IStreamController _stream;

        /// <summary>The remote track and first-frame controller.</summary>
        private readonly IRenderController _render;

        /// <summary>The HTTP heartbeat bound to the current server session.</summary>
        private readonly SessionHeartbeat _heartbeat;

        /// <summary>The shared startup timing recorder.</summary>
        private readonly RealtimeTiming _timing;

        /// <summary>The committed server session, or null before connection and after invalidation.</summary>
        internal XmaxSession ActiveSession { get; private set; }

        /// <summary>The connected remote media stream, or null outside a committed connection.</summary>
        internal RealtimeMediaStream CurrentRemoteStream { get; private set; }

        /// <summary>The prepared transport levels and formats, or null outside a prepared connection.</summary>
        internal RealtimeQualityConfiguration QualityConfiguration => _stream.QualityController.Configuration;

        /// <summary>Occurs after initial quality or a directional adjustment is successfully committed.</summary>
        internal event Action<RealtimeQualityConfiguration> QualityChanged;

        /// <summary>Occurs when an uncanceled server heartbeat fails or the session becomes inactive.</summary>
        internal event Action<XmaxException> FatalError;

        /// <summary>Occurs when server closure fails after local cleanup has been attempted.</summary>
        internal event Action<XmaxException> CleanupError;

        /// <summary>Creates a connection owner with shared session, stream, rendering, and timing components.</summary>
        internal XmaxRealtimeConnectionManager(
            IRealtimeSessionService sessions,
            IStreamController stream,
            IRenderController render,
            RealtimeTiming timing = null
        )
        {
            _sessions = sessions;
            _stream = stream;
            _render = render;
            _timing = timing;
            _heartbeat = new SessionHeartbeat(sessions);
            _heartbeat.Failed += exception => EventDispatch.Raise(
                FatalError,
                exception
            );
        }

        /// <summary>Creates a server session and joins RTC, rolling back acquired resources on failure or cancellation.</summary>
        ///
        /// <remarks>The stream must belong to this manager. Dimension overloads create a local external stream. Online RTC is supported only in Android Player.</remarks>
        internal async Task<RealtimeMediaStream> ConnectAsync(
            string model,
            RealtimeVideoFormat format,
            CancellationToken token
        )
        {
            _stream.ValidatePlatform(); // Reject unsupported platforms before creating an online session.
            XmaxSession session = null;

            try
            {
                await _stream.InitializeAsync(token);
                token.ThrowIfCancellationRequested();
                _timing?.BeginSessionCreation();
                session = await _sessions.CreateSessionAsync(
                    model,
                    token
                );
                _timing?.FinishSessionCreation();
                token.ThrowIfCancellationRequested();

                var info = RealtimeSessionService.RequireJoinInfo(session);
                var track = new RealtimeVideoTrack(info.BotName);
                _render.Bind(track);

                await _stream.ConnectAsync(
                    info,
                    format,
                    token
                );
                token.ThrowIfCancellationRequested();

                var quality = QualityConfiguration;
                if (quality != null)
                    track.UpdateVideoFormat(RemoteVideoFormat(
                            quality.Baseline,
                            quality.Downlink
                        ));
                ActiveSession = session;
                _heartbeat.Start(session.SessionUid);

                return CurrentRemoteStream = new RealtimeMediaStream(
                    RealtimeStreamId.Remote,
                    track
                );
            }
            catch
            {
                _render.Reset();

                try
                {
                    await _stream.DisconnectAsync();
                }
                finally
                {
                    await CloseSessionAsync(session);
                }

                throw;
            }
        }

        /// <summary>Prepares initial transport levels and configures independent media, generation, and return dimensions.</summary>
        internal RealtimeQualityConfiguration PrepareQuality(
            RealtimeVideoFormat baseline,
            RealtimeVideoFormat generationFormat,
            RealtimeConfiguration options,
            Action ensureCurrent
        )
        {
            ensureCurrent();
            var transport = _stream.QualityController.Prepare(
                baseline,
                options
            );
            _stream.ConfigureMedia(
                baseline,
                generationFormat,
                transport.Downlink
            );
            ensureCurrent();
            return transport;
        }

        /// <summary>Persists initial levels after generation has produced its first valid frame.</summary>
        internal void CompleteInitialQuality()
        {
            var configuration = QualityConfiguration;
            if (configuration == null)
                return;

            _stream.QualityController.Complete(
                configuration.UplinkLevel,
                false,
                true
            );
            _stream.QualityController.Complete(
                configuration.DownlinkLevel,
                true,
                true
            );
            EventDispatch.Raise(
                QualityChanged,
                QualityConfiguration
            );
        }

        /// <summary>Applies native encoding or submits target dimensions before committing a directional transport level.</summary>
        internal async Task ApplyQualityLevelAsync(
            RealtimeQualityLevel level,
            bool downlink,
            string taskId,
            CancellationToken token,
            Action ensureCurrent
        )
        {
            ensureCurrent();
            var format = _stream.QualityController.ResolveFormat(
                level,
                downlink
            );
            var current = QualityConfiguration;
            try
            {
                if (downlink)
                {
                    if (format.Width != current.Downlink.Width || format.Height != current.Downlink.Height)
                        await _stream.ChangeTargetSizeAsync(
                            taskId,
                            format,
                            token
                        );
                    ensureCurrent();
                    CurrentRemoteStream?.VideoTrack.UpdateVideoFormat(RemoteVideoFormat(
                            current.Baseline,
                            format
                        ));
                }
                else
                {
                    _stream.ConfigureVideoEncoding(format);
                    ensureCurrent();
                }
                _stream.QualityController.Complete(
                    level,
                    downlink,
                    true
                );
                XmaxLogger.Realtime.Debug(() => "传输调档 (Quality Adjustment)\n└─ "
                    + (downlink ? "Downlink" : "Uplink") + ": L" + (int)level + ", " + format.Width + " × " + format.Height);
                EventDispatch.Raise(
                    QualityChanged,
                    QualityConfiguration
                );
            }
            catch (Exception exception)
            {
                if (token.IsCancellationRequested)
                    throw;

                _stream.QualityController.Complete(
                    level,
                    downlink,
                    false
                );
                if (!(exception is OperationCanceledException))
                    XmaxLogger.Realtime.Warn(() => "传输调档失败 (Quality Adjustment Failed)\n└─ " + ErrorMessageFormatter.Format(exception));
                throw;
            }
        }

        /// <summary>Applies requested remote dimensions while retaining the media baseline's frame rate and options.</summary>
        private static RealtimeVideoFormat RemoteVideoFormat(
            RealtimeVideoFormat baseline,
            RealtimeVideoFormat target
        ) => baseline.Resized(
            target.Width,
            target.Height
        );

        /// <summary>Invalidates connection state, stops heartbeat, leaves RTC, and attempts server session closure.</summary>
        internal async Task DisconnectAsync()
        {
            var session = ActiveSession;
            ActiveSession = null;
            CurrentRemoteStream = null;
            _render.Reset();

            try
            {
                await _heartbeat.StopAsync();
                await _stream.DisconnectAsync();
            }
            finally
            {
                await CloseSessionAsync(session);
            }
        }

        /// <summary>Attempts server closure and reports recoverable cleanup warnings without replacing the original failure.</summary>
        private async Task CloseSessionAsync(
            XmaxSession session
        )
        {
            if (session == null || string.IsNullOrEmpty(session.SessionUid))
                return;

            try
            {
                await _sessions.CloseSessionAsync(
                    session.SessionUid,
                    CancellationToken.None
                );
            }
            catch (Exception exception)
            {
                XmaxRealtimeErrorManager.Report(exception);
                EventDispatch.Raise(
                    CleanupError,
                    XmaxRealtimeErrorManager.Wrap(exception)
                );
            }
        }
    }
}
