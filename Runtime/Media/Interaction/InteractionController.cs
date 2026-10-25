//
//  InteractionController.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xmax.SDK
{

    /// <summary>
    /// Validates normalized host input and forwards internal media points to the active generation.
    /// </summary>
    internal sealed class InteractionController : IInteractionController
    {

        /// <summary>The transport submitting normalized points for the current task.</summary>
        private readonly IStreamController _stream;

        /// <summary>The generation task accepting normalized trajectories, or null while interactions are stopped.</summary>
        private string _taskId;

        /// <summary>
        /// Creates a normalized interaction controller for one stream coordinator.
        /// </summary>
        internal InteractionController(
            IStreamController stream
        )
        {
            _stream = stream;
        }

        /// <inheritdoc/>
        public void StartInteraction(
            string taskId
        )
        {
            if (string.IsNullOrWhiteSpace(taskId))
                throw new XmaxException(
                    XmaxErrorCode.InvalidConfiguration,
                    "An interaction requires a generation task identifier."
                );
            _taskId = taskId;
        }

        /// <inheritdoc/>
        public void StopInteraction() => _taskId = null;

        /// <summary>
        /// Validates finite zero-to-one coordinates before submitting the complete trajectory.
        /// </summary>
        public void Send(
            IReadOnlyList<Vector2> tracks
        )
        {
            if (tracks == null)
                throw new ArgumentNullException(nameof(tracks));

            if (tracks.Count == 0)
                return;

            var taskId = _taskId;
            if (taskId == null)
                throw new XmaxException(
                    XmaxErrorCode.RtcError,
                    "Tracks require active realtime generation."
                );

            var points = new RealtimePoint[tracks.Count];
            for (var i = 0; i < tracks.Count; i++)
            {
                var point = tracks[i];
                if (float.IsNaN(point.x) || float.IsInfinity(point.x) ||
                    float.IsNaN(point.y) || float.IsInfinity(point.y) ||
                    point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1)
                    throw new XmaxException(
                        XmaxErrorCode.InvalidConfiguration,
                        "Track coordinates must be finite values between zero and one."
                    );
                points[i] = new RealtimePoint(
                    point.x,
                    point.y
                );
            }

            _stream.SendTracks(
                points,
                taskId
            );
        }
    }
}
