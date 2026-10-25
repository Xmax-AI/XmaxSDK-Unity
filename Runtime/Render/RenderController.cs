//
//  RenderController.cs
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

    /// <summary>
    /// Owns remote track binding and rejects frames and waits invalidated by rendering resets.
    /// </summary>
    internal sealed class RenderController : IRenderController
    {

        /// <summary>The copied remote packet queue, separate from Unity-thread rendering.</summary>
        private readonly RealtimeMediaFrameDispatcher _dispatcher = new RealtimeMediaFrameDispatcher();

        /// <summary>The currently bound remote track, or null after a complete reset.</summary>
        private RealtimeVideoTrack _track;

        /// <summary>The confirmed published source, or null while no remote video is bound.</summary>
        private RemoteStream? _remoteStream;

        /// <summary>Whether a generation owns rendering, including its wait for publication.</summary>
        private bool _generationActive;

        /// <summary>The current generation's first-frame completion signal.</summary>
        private TaskCompletionSource<bool> _ready;

        /// <summary>The revision rejecting stale or reentrant frame delivery.</summary>
        private long _revision;

        /// <inheritdoc/>
        public XmaxVideoFrame LatestFrame { get; private set; }

        /// <inheritdoc/>
        public event Action<XmaxVideoFrame> FrameReceived;

        /// <inheritdoc/>
        public void SetRemoteMediaFrameListener(
            Action<RealtimeMediaFrame> listener
        ) => _dispatcher.SetListener(listener);

        /// <inheritdoc/>
        public void Bind(
            RealtimeVideoTrack track
        )
        {
            Reset();
            _track = track;
        }

        /// <inheritdoc/>
        public void BeginGeneration()
        {
            ResetGeneration();
            _generationActive = true;
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <inheritdoc/>
        public void SetRemoteStream(
            RemoteStream? stream
        )
        {
            if (!_generationActive || Nullable.Equals(_remoteStream, stream))
                return;

            _remoteStream = stream;
            InvalidateFrames();
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher.SetStream(stream.HasValue);
        }

        /// <inheritdoc/>
        public async Task WaitUntilRemoteFrameReadyAsync(
            CancellationToken cancellationToken,
            int timeout = 10000
        )
        {
            var ready = _ready ?? throw new XmaxException(
                XmaxErrorCode.RtcError,
                "Remote rendering is not active."
            );
            await AsyncDeadline.WaitAsync(
                ready.Task,
                timeout,
                cancellationToken
            );
        }

        /// <inheritdoc/>
        public void Receive(
            XmaxVideoFrame frame
        )
        {
            if (_track == null || !_remoteStream.HasValue || _ready == null)
                return;

            frame.Validate();
            var revision = _revision;
            LatestFrame = frame;
            _dispatcher.Dispatch(frame);
            _ready.TrySetResult(true);
            _track.RaiseFrame(
                frame,
                () => revision == _revision
            );
            if (revision == _revision)
                EventDispatch.Raise(
                    FrameReceived,
                    frame,
                    () => revision == _revision
                );
        }

        /// <inheritdoc/>
        public void ResetGeneration()
        {
            _generationActive = false;
            _remoteStream = null;
            InvalidateFrames();
        }

        /// <summary>Clears cached and queued frames and invalidates waits for the retired source.</summary>
        private void InvalidateFrames()
        {
            _revision++;
            _dispatcher.SetStream(false);
            _ready?.TrySetCanceled();
            _ready = null;
            LatestFrame = null;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            ResetGeneration();
            _track = null;
        }
    }
}
