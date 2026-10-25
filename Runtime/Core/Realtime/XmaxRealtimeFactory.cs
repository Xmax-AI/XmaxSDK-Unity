//
//  XmaxRealtimeFactory.cs
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

    /// <summary>The shared components serving one public realtime manager and its lifecycle.</summary>
    internal sealed class XmaxRealtimeComponents
    {

        /// <summary>The exclusive operation admission and merged termination coordinator.</summary>
        internal XmaxRealtimeLifecycleManager Lifecycle { get; }

        /// <summary>The server session, RTC connection, and transport quality execution owner.</summary>
        internal XmaxRealtimeConnectionManager Connection { get; }

        /// <summary>The generation conditions, interactions, and first-frame coordinator.</summary>
        internal XmaxRealtimeGenerationManager Generation { get; }

        /// <summary>The shared runtime failure router.</summary>
        internal XmaxRealtimeErrorManager Errors { get; }

        /// <summary>The local media owner and baseline frame normalizer.</summary>
        internal IMediaController Media { get; }

        /// <summary>The task-bound normalized trajectory controller shared with generation.</summary>
        internal IInteractionController Interaction { get; }

        /// <summary>The remote track and first-frame controller.</summary>
        internal IRenderController Render { get; }

        /// <summary>The room, quality, encoding, and result-stream coordinator.</summary>
        internal IStreamController Stream { get; }

        /// <summary>The stateless model sizing service shared by the component graph.</summary>
        internal IMediaService MediaService { get; }

        /// <summary>The shared startup timing recorder.</summary>
        internal RealtimeTiming Timing { get; }

        /// <summary>Creates an immutable component graph whose dependencies have already been assembled.</summary>
        internal XmaxRealtimeComponents(
            XmaxRealtimeLifecycleManager lifecycle,
            XmaxRealtimeConnectionManager connection,
            XmaxRealtimeGenerationManager generation,
            XmaxRealtimeErrorManager errors,
            IMediaController media,
            IInteractionController interaction,
            IRenderController render,
            IStreamController stream,
            IMediaService mediaService,
            RealtimeTiming timing
        )
        {
            Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Generation = generation ?? throw new ArgumentNullException(nameof(generation));
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
            Media = media ?? throw new ArgumentNullException(nameof(media));
            Interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
            Render = render ?? throw new ArgumentNullException(nameof(render));
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));
            MediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
            Timing = timing ?? throw new ArgumentNullException(nameof(timing));
        }

        /// <summary>Cleans up the requested scope after lifecycle admission has canceled and awaited active work.</summary>
        internal async Task CleanupAsync(
            TerminationScope scope
        )
        {
            Stream.QualityController.ResetSamples();
            if (scope == TerminationScope.Generation)
            {
                await Generation.StopAsync();
                return;
            }
            try
            {
                try
                {
                    await Generation.StopAsync();
                }
                finally
                {
                    await Connection.DisconnectAsync();
                }
            }
            finally
            {
                Generation.ResetContext();
                Stream.QualityController.Reset();
                if (scope == TerminationScope.All)
                {
                    try
                    {
                        await Media.CloseAsync();
                    }
                    finally
                    {
                        await Stream.ReleaseAsync();
                    }
                }
            }
        }
    }

    /// <summary>Creates and wires one realtime component graph without starting media or online sessions.</summary>
    internal static class XmaxRealtimeFactory
    {

        /// <summary>Creates shared defaults while allowing controllers, services, and task identity generation to be supplied.</summary>
        internal static XmaxRealtimeComponents Make(
            XmaxConfiguration configuration,
            IRealtimeSessionService sessions = null,
            IStreamController stream = null,
            RealtimeTiming timing = null,
            IRenderController render = null,
            IMediaController media = null,
            IInteractionController interaction = null,
            IMediaService mediaService = null,
            Func<string> taskIdFactory = null
        )
        {
            var lifecycle = new XmaxRealtimeLifecycleManager();
            var errors = new XmaxRealtimeErrorManager();
            timing = timing ?? new RealtimeTiming();
            mediaService = mediaService ?? new MediaService();
            stream = stream ?? new StreamController(
                new RtcManager(),
                timing: timing,
                mediaService: mediaService
            );
            render = render ?? new RenderController();
            interaction = interaction ?? new InteractionController(stream);
            var connection = new XmaxRealtimeConnectionManager(
                sessions ?? new RealtimeSessionService(new ApiService(
                        configuration,
                        realtime: true
                    )),
                stream,
                render,
                timing
            );
            media = media ?? new MediaController(
                lifecycle.RequireThread,
                frame =>
                {
                    if (!lifecycle.IsTerminating && connection.ActiveSession != null)
                        stream.PushFrame(frame);
                },
                stream.InitializeAsync,
                stream.ReleaseAsync
            );
            var generation = new XmaxRealtimeGenerationManager(
                interaction,
                stream,
                render,
                timing,
                taskIdFactory
            );
            return Assemble(
                connection,
                generation,
                errors,
                media,
                interaction,
                render,
                stream,
                mediaService,
                timing,
                lifecycle
            );
        }

        /// <summary>Wires supplied dependencies to a shared lifecycle so a manager can consume the graph without rebuilding it.</summary>
        internal static XmaxRealtimeComponents Assemble(
            XmaxRealtimeConnectionManager connection,
            XmaxRealtimeGenerationManager generation,
            XmaxRealtimeErrorManager errors,
            IMediaController media,
            IInteractionController interaction,
            IRenderController render,
            IStreamController stream,
            IMediaService mediaService,
            RealtimeTiming timing,
            XmaxRealtimeLifecycleManager lifecycle = null
        )
        {
            lifecycle = lifecycle ?? new XmaxRealtimeLifecycleManager();
            var components = new XmaxRealtimeComponents(
                lifecycle,
                connection,
                generation,
                errors,
                media,
                interaction,
                render,
                stream,
                mediaService,
                timing
            );
            stream.FrameReceived += render.Receive;
            stream.RemoteStreamChanged += render.SetRemoteStream;
            stream.FatalError += errors.Forward;
            connection.FatalError += errors.Forward;
            media.FatalError += error => errors.Forward(
                error,
                TerminationScope.All
            );
            lifecycle.Configure(
                components.CleanupAsync,
                () => media.LocalStream != null,
                () => connection.ActiveSession?.SessionUid
            );
            errors.SetFailureHandler((
                error,
                scope
            ) =>
            {
                if (!lifecycle.HasOperation && connection.ActiveSession == null &&
                    (scope != TerminationScope.All || media.LocalStream == null))
                    return Task.CompletedTask;
                return lifecycle.TerminateAsync(
                    scope,
                    XmaxErrorConverter.Wrap(
                        error,
                        XmaxErrorSeverity.Fatal
                    )
                );
            });
            return components;
        }
    }
}
