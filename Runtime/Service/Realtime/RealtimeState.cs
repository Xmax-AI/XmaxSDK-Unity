//
//  RealtimeState.cs
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

    /// <summary>The local media and realtime connection lifecycle state.</summary>
    public enum RealtimeConnectionState
    {

        /// <summary>No local media stream is available.</summary>
        Idle,

        /// <summary>The local media stream is being prepared.</summary>
        Preparing,

        /// <summary>Local media is ready for preview, connection, and generation.</summary>
        Ready,

        /// <summary>The server session and RTC publication are being established.</summary>
        Connecting,

        /// <summary>The connection is open without an active generation task.</summary>
        Connected,

        /// <summary>Generation is ready to deliver result frames.</summary>
        Generating,

        /// <summary>Generation, RTC, and server session resources are being released.</summary>
        Disconnecting
    }

    /// <summary>
    /// The category of a terminal connection reason.
    /// </summary>
    public enum RealtimeReasonKind
    {

        /// <summary>
        /// Explicit termination or cancellation without a failure.
        /// </summary>
        Normal,

        /// <summary>The display orientation changed and the connection was explicitly released.</summary>
        OrientationChanged,

        /// <summary>
        /// A failure that caused the workflow to end.
        /// </summary>
        Failure
    }

    /// <summary>
    /// The reason a realtime workflow entered a terminal state.
    /// </summary>
    public sealed class RealtimeReason : IEquatable<RealtimeReason>
    {

        /// <summary>
        /// The shared normal-termination reason.
        /// </summary>
        private static readonly RealtimeReason NormalInstance =
            new RealtimeReason(
                RealtimeReasonKind.Normal,
                null
            );

        /// <summary>
        /// Explicit termination or cancellation without a failure.
        /// </summary>
        public static RealtimeReason Normal => NormalInstance;

        /// <summary>An orientation change requiring connection reconfiguration.</summary>
        public static RealtimeReason OrientationChanged { get; } = new RealtimeReason(
            RealtimeReasonKind.OrientationChanged,
            null
        );

        /// <summary>
        /// The operation or termination reason category.
        /// </summary>
        public RealtimeReasonKind Kind { get; }

        /// <summary>
        /// The failure that ended the workflow, or null for normal termination.
        /// </summary>
        public XmaxException Error { get; }

        /// <summary>
        /// Creates a terminal connection reason.
        /// </summary>
        private RealtimeReason(
            RealtimeReasonKind kind,
            XmaxException error
        )
        {
            Kind = kind;
            Error = error;
        }

        /// <summary>
        /// Creates a failure reason containing the same error reported to the caller.
        /// </summary>
        public static RealtimeReason Failure(
            XmaxException error
        )
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            return new RealtimeReason(
                RealtimeReasonKind.Failure,
                error
            );
        }

        /// <summary>
        /// Compares reason categories and their associated error instances.
        /// </summary>
        public bool Equals(
            RealtimeReason other
        )
        {
            return other != null && Kind == other.Kind && ReferenceEquals(
                Error,
                other.Error
            );
        }

        /// <summary>
        /// Compares reason categories and their associated error instances.
        /// </summary>
        public override bool Equals(
            object obj
        ) => Equals(obj as RealtimeReason);

        /// <summary>
        /// Returns a hash consistent with reason equality.
        /// </summary>
        public override int GetHashCode() => ((int)Kind * 397) ^ (Error?.GetHashCode() ?? 0);
    }

    /// <summary>
    /// An immutable snapshot of connection state and associated task identity.
    /// </summary>
    public sealed class RealtimeState
    {

        /// <summary>
        /// The current connection or generation phase.
        /// </summary>
        public RealtimeConnectionState ConnectionState { get; }

        /// <summary>
        /// The current or most recent server session identifier.
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// The active ready generation task identifier, or null before startup or after stopping.
        /// </summary>
        public string TaskId { get; }

        /// <summary>
        /// The terminal reason, cleared when a new operation starts.
        /// </summary>
        public RealtimeReason Reason { get; }

        /// <summary>
        /// Creates a connection state snapshot.
        /// </summary>
        public RealtimeState(
            RealtimeConnectionState connectionState,
            string sessionId = null,
            string taskId = null,
            RealtimeReason reason = null
        )
        {
            ConnectionState = connectionState;
            SessionId = sessionId;
            TaskId = taskId;
            Reason = reason;
        }
    }
}
