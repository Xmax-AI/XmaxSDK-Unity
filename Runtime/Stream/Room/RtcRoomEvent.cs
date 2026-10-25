//
//  RtcRoomEvent.cs
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
    /// Encodes generation, target-size, and normalized interaction messages.
    /// </summary>
    internal static class RtcRoomEvent
    {

        /// <summary>
        /// Encodes a generation start with independently resolved canvas and return dimensions.
        /// </summary>
        internal static string Start(
            string userId,
            string taskId,
            RealtimeVideoFormat generation,
            RealtimeVideoFormat target,
            RealtimeContext context,
            bool contain,
            bool faceRestoration = false
        )
        {
            var parameters = Parameters(
                generation,
                context,
                contain,
                faceRestoration
            );
            parameters["mirror"] = false;
            AddTarget(
                parameters,
                target
            );
            return Encode(
                "start",
                userId,
                taskId,
                parameters
            );
        }

        /// <summary>
        /// Encodes conditions for the existing generation task without changing return dimensions.
        /// </summary>
        internal static string ChangeCondition(
            string userId,
            string taskId,
            RealtimeVideoFormat generation,
            RealtimeContext context,
            bool contain,
            bool faceRestoration = false
        ) => Encode(
            "change_condition",
            userId,
            taskId,
            Parameters(
                generation,
                context,
                contain,
                faceRestoration
            )
        );

        /// <summary>
        /// Encodes a new return size and server encoding budget.
        /// </summary>
        internal static string ChangeTargetSize(
            string userId,
            string taskId,
            RealtimeVideoFormat target
        )
        {
            var parameters = new Dictionary<string, object>();
            AddTarget(
                parameters,
                target
            );
            return Encode(
                "change_target_size",
                userId,
                taskId,
                parameters
            );
        }

        /// <summary>
        /// Encodes generation termination with both task identity fields.
        /// </summary>
        internal static string Stop(
            string userId,
            string taskId
        ) => Encode(
            "stop",
            userId,
            taskId
        );

        /// <summary>
        /// Encodes a room heartbeat.
        /// </summary>
        internal static string Heartbeat(
            string userId
        ) => Encode(
            "heartbeat",
            userId,
            null
        );

        /// <summary>
        /// Encodes normalized media points without applying an additional dimension conversion.
        /// </summary>
        internal static string Tracks(
            string userId,
            string taskId,
            IReadOnlyList<RealtimePoint> tracks
        )
        {
            var points = new List<double[]>(tracks.Count);
            foreach (var point in tracks)
                points.Add(new[] { point.X, point.Y });
            var message = Envelope(
                "tracks",
                userId,
                taskId
            );
            message["tracks"] = points;
            return JsonCodec.Encode(message);
        }

        /// <summary>
        /// Creates generation canvas parameters, retaining the media when aspect ratios differ.
        /// </summary>
        private static Dictionary<string, object> Parameters(
            RealtimeVideoFormat generation,
            RealtimeContext context,
            bool contain,
            bool faceRestoration = false
        )
        {
            var parameters = new Dictionary<string, object>
            {
                ["model"] = "default",
                ["size"] = new[] { generation.Width, generation.Height },
                ["prompt"] = context.Prompt,
                ["face_replace"] = faceRestoration
            };
            if (!string.IsNullOrEmpty(context.ReferencePath))
                parameters["ref_image_path"] = context.ReferencePath;
            if (contain)
                parameters["padding"] = "contain";
            return parameters;
        }

        /// <summary>
        /// Calculates an independent downlink budget using 24 fps and a 500-to-10000 kbps range.
        /// </summary>
        private static void AddTarget(
            Dictionary<string, object> parameters,
            RealtimeVideoFormat target
        )
        {
            var resolved = EncodingBitratePolicy.Resolve(new RealtimeVideoFormat(
                    target.Width,
                    target.Height,
                    24
                ));
            parameters["target_size"] = new[] { target.Width, target.Height };
            parameters["min_bitrate"] = 500;
            parameters["max_bitrate"] = Math.Min(10000, Math.Max(500, resolved.MaximumBitrate));
        }

        /// <summary>
        /// Encodes a message with optional generation parameters.
        /// </summary>
        private static string Encode(
            string name,
            string userId,
            string taskId,
            Dictionary<string, object> parameters = null
        )
        {
            var message = Envelope(
                name,
                userId,
                taskId
            );
            if (parameters != null)
                message["params"] = parameters;
            return JsonCodec.Encode(message);
        }

        /// <summary>
        /// Creates common task and client runtime fields.
        /// </summary>
        private static Dictionary<string, object> Envelope(
            string name,
            string userId,
            string taskId
        )
        {
            var message = new Dictionary<string, object>
            {
                ["event"] = name,
                ["user_id"] = userId,
                ["runtime"] = RuntimeInfo.ToDictionary()
            };
            if (!string.IsNullOrEmpty(taskId))
            {
                message["uid"] = taskId;
                message["session_uid"] = taskId;
            }
            return message;
        }
    }
}
