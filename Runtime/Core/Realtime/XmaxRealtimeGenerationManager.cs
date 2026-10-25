//
//  XmaxRealtimeGenerationManager.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Xmax.SDK
{

    /// <summary>
    /// Manages generation conditions, interactions, result confirmation, and first-frame readiness.
    /// </summary>
    internal sealed class XmaxRealtimeGenerationManager
    {

        /// <summary>The task-bound interaction controller shared by generation startup, updates, and cleanup.</summary>
        private readonly IInteractionController _interaction;

        /// <summary>
        /// The room signaling and media transport controller.
        /// </summary>
        private readonly IStreamController _stream;

        /// <summary>
        /// The remote track and first-frame readiness controller.
        /// </summary>
        private readonly IRenderController _render;

        /// <summary>The identifier factory for each new generation task.</summary>
        private readonly Func<string> _taskIdFactory;

        /// <summary>The startup timing record shared with connection and stream controllers.</summary>
        private readonly RealtimeTiming _timing;

        /// <summary>
        /// The last successfully applied generation conditions.
        /// </summary>
        private RealtimeContext _context;

        /// <summary>
        /// The active ready generation task identifier, or null before startup or after stopping.
        /// </summary>
        internal string TaskId { get; private set; }

        /// <summary>
        /// Creates a generation coordinator.
        /// </summary>
        internal XmaxRealtimeGenerationManager(
            IInteractionController interaction,
            IStreamController stream,
            IRenderController render,
            RealtimeTiming timing = null,
            Func<string> taskIdFactory = null
        )
        {
            _interaction = interaction;
            _stream = stream;
            _render = render;
            _timing = timing;
            _taskIdFactory = taskIdFactory ?? CreateTaskId;
        }

        /// <summary>Creates a task identifier from an unpadded Base64URL UUID with the Unity platform marker.</summary>
        internal static string CreateTaskId()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            // Convert the mixed-endian GUID layout to UUID network byte order.
            Array.Reverse(
                bytes,
                0,
                4
            );
            Array.Reverse(
                bytes,
                4,
                2
            );
            Array.Reverse(
                bytes,
                6,
                2
            );
            var encoded = Convert.ToBase64String(bytes).TrimEnd('=').Replace(
                '+',
                '-'
            ).Replace(
                '/',
                '_'
            );
            return "task-" + encoded + "?os=unity";
        }

        /// <summary>Requires explicit conditions or previously applied conditions before any online startup.</summary>
        internal void ValidateContext(
            RealtimeContext context
        )
        {
            if (context == null && _context == null)
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "A realtime context is required for the first generation."
                );
        }

        /// <summary>
        /// Starts generation and waits for result confirmation and the first frame; an existing task updates
        /// its conditions.
        /// </summary>
        internal async Task<string> StartAsync(
            RealtimeContext context,
            RealtimeVideoFormat format,
            CancellationToken token,
            bool faceRestoration = false
        )
        {
            token.ThrowIfCancellationRequested();
            ValidateContext(context);
            var resolved = context ?? _context;
            if (TaskId != null)
            {
                await _stream.ChangeGenerationConditionAsync(
                    resolved,
                    format,
                    TaskId,
                    token,
                    faceRestoration
                );
                token.ThrowIfCancellationRequested();
                _interaction.StartInteraction(TaskId);
                token.ThrowIfCancellationRequested();
                _context = resolved;
                return TaskId;
            }

            _render.BeginGeneration();

            try
            {
                var taskId = _taskIdFactory();
                await _stream.StartGenerationAsync(
                    taskId,
                    resolved,
                    format,
                    token,
                    faceRestoration
                );
                token.ThrowIfCancellationRequested();
                _interaction.StartInteraction(taskId);
                token.ThrowIfCancellationRequested();
                await _render.WaitUntilRemoteFrameReadyAsync(token);
                token.ThrowIfCancellationRequested();
                _timing?.FinishFirstFrame(taskId);
                _context = resolved;
                TaskId = taskId;

                return taskId;
            }
            catch
            {
                await StopAsync();

                throw;
            }
        }

        /// <summary>
        /// Stops interactions and invalidates the generation and remote frame wait while retaining successful conditions.
        /// </summary>
        internal async Task StopAsync()
        {
            TaskId = null;
            _interaction.StopInteraction();
            _render.ResetGeneration();
            await _stream.StopGenerationAsync();
        }

        /// <summary>Submits normalized input through the interaction binding owned by the current generation.</summary>
        internal void SendTracks(
            IReadOnlyList<Vector2> tracks
        ) => _interaction.Send(tracks);

        /// <summary>
        /// Clears cached conditions so the next generation requires explicit conditions.
        /// </summary>
        internal void ResetContext() => _context = null;
    }
}
