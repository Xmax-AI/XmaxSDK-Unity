# XmaxSDK Unity Usage Guide

## Configuration and platform

Create realtime managers and call their APIs on the Unity main thread. Await
asynchronous APIs without blocking that thread with `.Wait()` or `.Result`.
Online RTC currently supports Android Player with ARM64 native libraries;
Unity Editor can run local media and regression tests but cannot join RTC.

The package includes Agora Unity RTC 4.6.2 and declares its Unity UI dependency.
Do not install another copy of the Agora plugin into the same project.

```csharp
var client = new XmaxClient(new XmaxConfiguration(apiKey, XmaxEnvironment.China));
var realtime = client.CreateRealtimeManager(new RealtimeConfiguration(
    RealtimeModel.X2_1_1_Preview_CN,
    isAdaptiveQualityEnabled: true
));
```

Choose `X2_1_1_Preview_CN` or `X2_1_1_Preview_US` explicitly for the regional
model. Old model enum values are rejected instead of silently selecting a new
model. RealtimeConfiguration accepts RealtimeModel directly; unsupported enum
values are rejected. ModelDefinition is an internal registry detail.

Standard Global configuration keeps the general API at
https://api.xmax.cloud/open/api/v1 and routes realtime session requests to
https://api.xmax.ai/open/api/v1. Explicit custom base URLs remain unchanged.
API requests include `X-Platform: unity`, `X-OS-Version`, `X-SDK-Version`,
and `X-Device-Model`; room signals use the same runtime metadata.

## External input and the media baseline

Your app owns camera permissions, capture, playback, and input pacing. Describe
the actual input dimensions and keep feeding frames while awaiting generation.

```csharp
var local = realtime.CreateLocalExternalStream(
    new RealtimeVideoFormat(1920, 1080, 30)
);
local.VideoTrack.FrameReceived += frame => { /* optional local preview */ };

// Run this on the Unity main thread for every input frame:
// local.PushVideoFrame(XmaxVideoFrame.CreateRgba(rgba, 1920, 1080));

var remote = await realtime.StartGenerationAsync(
    local,
    new RealtimeContext("A watercolor landscape")
);

var textures = new XmaxVideoTexture();
textures.Bind(remote.VideoTrack, i420Material);
```

The material samples `_YTex`, `_UTex`, and `_VTex`. XmaxVideoTexture handles
strides and changing received dimensions. Your app owns the material and
handles display UV orientation and YUV conversion in its shader.

`SourceVideoFormat` retains the format passed by your app. `VideoFormat` on a
local stream describes the resolved media baseline. Raw frames are resized to
that baseline when dimensions differ, with reusable bilinear plane buffers.
RGBA, BGRA, and I420 are supported. Frames already at the baseline avoid this
resize. Direct `realtime.PushVideoFrame`, `PushRgbaFrame`, and `PushI420Frame`
normalize input for transmission without raising local preview events.
RTC transmission begins when a generation task is created, including the wait
for its first result frame. A connected room without a generation task does not
upload input frames; local stream preview remains available.

Creating a local stream needs no valid API key and creates no server session.
The no-argument creation overload uses the model's default `1024×1920@30`
format. Replacing or closing a stream invalidates its previous input callback.

For a host source with managed delivery, implement `IRealtimeExternalVideoSource`
and bind it with `await realtime.CreateExternalVideoStreamAsync(source)`.
`VideoFormat` describes the fixed source dimensions. `StartAsync(sink, token)`
installs delivery and returns when ready; it must honor cancellation. Submit
frames through `sink.PushVideo(frame)` from any capture thread, and release the
sink reference in the idempotent `StopAsync()` method. Capture devices and
players remain owned by your app.

Source frames must match the declared dimensions and contain upright pixels
with `Rotation0` metadata. Their timestamps are nonnegative monotonic
microseconds, strictly increasing across seeks and loops. Negative, duplicate,
and decreasing timestamps are ignored. The sink copies accepted pixel arrays
before returning and forwards only the latest pending frame on Unity's main
thread. Your app may reuse its arrays after `PushVideo` returns. Source-bound
streams receive input through this sink; the existing direct-input APIs retain
their current buffer and timestamp contracts.

`sink.ReportError(exception)` waits for an in-flight frame submission, invalidates
the binding, and routes a `MediaError` through complete lifecycle cleanup. Once it
returns, no submission from the binding can begin or continue uploading. Startup failure or cancellation also stops
the source. `DisconnectAsync` preserves frame delivery and preview;
`StopExternalVideoStreamAsync` stops local delivery after disconnection, and
`CloseAsync` disconnects and stops the source. Stopping invalidates the sink
before awaiting the source, so queued frames and old failures are rejected.
All source lifecycle calls and public manager methods run on Unity's main
thread; asynchronous shutdown does not transfer ownership of host resources.
On Android, asynchronous source creation acquires and initializes the exclusive
RTC engine before calling the source's `StartAsync`. Direct local stream
creation remains synchronous; connection acquires its engine before creating a
server session. Waiting for another manager's engine lease therefore creates no
online session. `DisconnectAsync` retains the engine with local media;
`StopExternalVideoStreamAsync` and `CloseAsync` release it. Close managers when
their local media is no longer needed so other managers can acquire the engine.

## Independent dimension decisions

| Format | Rules |
| --- | --- |
| Source | Actual input dimensions and rate supplied by your app. |
| Media baseline | 200,000–2,100,000 pixels for regional preview models, four-aligned, preserving aspect ratio where possible. Small sources can be upscaled. |
| Generation canvas | Closest aspect-ratio match among `1024×1920` and `1920×1024`; independent of transport quality. |
| Uplink | Derived from the unchanged media baseline, uplink quality level, and model minimum area; four-aligned and never upscaled above the baseline. |
| Target return | Derived from the unchanged media baseline and downlink quality level; four-aligned, never upscaled, without the input minimum area restriction. |

`MediaService.ResolveMediaSize`, `ResolveGenerationSize`, and `ResolveTargetSize`
expose these offline decisions. `RecommendVideoFormat` resolves an external
media baseline and accepts an explicit rate, or zero for the model default.
Camera bucket validation applies only when resolving `RealtimeMediaSource.Camera`.
External video, image dimensions, and decoded video dimensions do not need to
match camera buckets. This sizing API does not capture or decode those sources.

For example, `3840×2160` external input resolves to a `1920×1080` media
baseline and a `1920×1024` generation canvas. The start and condition messages
use `padding: contain` when the baseline and canvas aspect ratios differ.
Their `size` describes the canvas; `target_size` describes returned media.

## Adaptive transport quality

Transport levels are separate from network ratings such as Good or Bad.
For a `1024×1920@30` baseline:

| Level | Dimension scale | Applied uplink | Final target return size |
| --- | --- | --- | --- |
| L1 | 100% | 1024×1920@30 | 1024×1920 |
| L2 | 90% | 896×1680@24 | 896×1680 |
| L3 | 75% | 768×1440@24 | 768×1440 |
| L4 | 60% | 608×1140@20 | 608×1140 |
| L5 | 50% | 512×960@16 | 512×960 |

Widths and heights follow exact aspect ratio where alignment permits. Uplink
frame rates are ceilings and do not exceed the baseline rate. Uplink retains
the model minimum area. A 240-pixel short-edge floor applies without enlarging
a baseline whose short edge is already smaller. Downlink budgets use 24 fps;
actual received frame timing depends on the server and transport.

Automatic adaptation is enabled by default. Without valid history, each
direction starts at L2. Successful levels are retained for one hour, separately
by model and direction. Disabling adaptation starts every connection at L1,
ignores history, and does not write manually selected levels into history.

Three consecutive Bad/VeryBad samples propose a downgrade; five consecutive
Excellent/Good samples propose an upgrade. Other ratings reset the counters.
An adjustment attempt starts a four-second stabilization period. An upgrade
starts a twenty-second observation period; a downgrade during that period
blocks another upgrade for sixty seconds. Stale, duplicate, future, and
nonmonotonic samples are ignored; gaps over five seconds reset accumulation.

```csharp
realtime.QualityChanged += configuration =>
{
    Debug.Log($"Up: {configuration.UplinkLevel}, down: {configuration.DownlinkLevel}");
};

await realtime.SetUplinkQualityLevelAsync(RealtimeQualityLevel.L3);
await realtime.SetDownlinkQualityLevelAsync(RealtimeQualityLevel.L2);
var snapshot = realtime.QualityConfiguration;
```

Manual changes require active generation and remain subject to later automatic
adaptation. Automatic changes skip periods occupied by other operations.
Configuration errors do not terminate a working generation or commit a new
level. Disconnect and close cancel pending changes before connection cleanup.
Network samples retain their monotonic timestamp before public quality callbacks
run. Adaptive work is queued with that timestamp and task identity; delayed or
superseded samples are checked after admission rather than being retimed.

`QualityConfiguration` is a snapshot of the stable baseline and submitted
uplink/return formats. `GenerationVideoFormat` reports the model canvas.
`UplinkQualityLevel` and `DownlinkQualityLevel` are null outside an active
connection. Remote track metadata describes requested return dimensions;
each received frame describes actual dimensions, which may change later.

## Bitrate settings

`RealtimeVideoFormat` accepts optional minimum and maximum uplink bitrates in
kbps and defaults to `MaintainFramerate`. Explicit values are preserved;
minimum zero disables the minimum. The maximum must be positive, and merged
minimum/maximum bounds must be ordered.

The default maximum is 0.6 times the interpolated quality reference, rounded
to nearest even with a 100 kbps floor. Budget validation uses a default minimum
of 100 kbps; the applied RTC minimum is `min(500, maximum)` unless explicitly
specified. For example, `1024×1920@30` defaults to `500–3619 kbps`.

Return budgets are independent of uplink overrides. Start and
`change_target_size` messages use the return dimensions at 24 fps, a 500 kbps
minimum, and a maximum clamped to 500–10000 kbps.

## Room protocol and interactions

Session credentials use `rtc_app_id`, `room_id`, `room_token`, `rtc_user_id`,
and `rtc_bot_id`. The SDK embeds the same fixed Agora App ID as the iOS SDK;
the returned `rtc_app_id` must match it. Room tokens remain server-issued and
are never embedded in the SDK. RTC joins with the server-assigned string
account and maps Agora numeric UIDs before identifying the result bot.

Signaling uses an ordered Agora DataStream. Messages up to 800 UTF-8 bytes are
sent directly; larger messages use `__agora_chunk__` fragments with `eventId`,
`index`, `count`, and `data`. The queue preserves whole-message ordering,
limits traffic below the native packet and byte quotas, and coalesces unsent
trajectories. Generation confirmation requires the complete start signal and
the expected bot's published result stream, followed by a valid first frame.

`SendTracks` accepts Unity `Vector2` values normalized over the full media,
from top-left `(0,0)` to bottom-right `(1,1)`. Both coordinates must be finite
and inside `[0,1]`. The SDK converts input to its internal double-precision
`RealtimePoint` model and sends the coordinates without another normalization.
Neither uplink nor downlink adaptation changes this coordinate space.

```csharp
// A point at the center of the full media:
realtime.SendTracks(new[] { new UnityEngine.Vector2(0.5f, 0.5f) });
```

`InteractionCoordinateMapper.TryMap` returns normalized `Vector2` coordinates
after accounting for Fit margins or Fill cropping. It uses the stable media
baseline to resolve the displayed aspect ratio. Flip Unity bottom-left screen
coordinates before mapping as needed, and undo app-specific mirroring or rotation.
GenerationManager binds interactions after the result stream is confirmed,
refreshes the binding after a successful condition update, and clears it when
generation stops. Trajectories use this binding's task identifier; your app
does not supply one.

## Lifecycle and ownership

- `ConnectAsync` establishes the server session and RTC connection.
- `StartGenerationAsync(local, context)` connects on demand and waits for the
  result stream and first frame. Existing generation updates conditions.
- Each new task uses `task-<22-character Base64URL UUID>?os=unity`, without
  Base64 padding. Room signals carry this identifier in both `uid` and
  `session_uid`; condition updates, quality changes, and trajectories reuse it.
- A null context reuses the last successful conditions; first startup validates
  explicit conditions before session creation. Initial startup failure or caller
  cancellation releases the connection. Cancelling a condition update or rejecting
  it before queue submission preserves the existing generation. A native send
  failure after submission triggers connection cleanup. Condition updates complete
  once admitted to the ordered queue, rather than waiting for native sends.
  Cancelling a wait does not retract admitted messages or interrupt their
  remaining fragments; room teardown cancels the queue.
- A newer call to either StartGenerationAsync overload cancels and replaces an
  unfinished generation request, awaiting its cleanup before proceeding. Other
  operation kinds still reject conflicts. Foreground work waits for admitted
  automatic quality configuration to finish.
- The 30-second generation confirmation deadline includes queued signaling and
  the expected bot publication. A separate 10-second deadline waits for the
  first valid result frame.
- Pass faceRestoration: true to restore the original face. This setting is sent
  on both initial generation and condition updates, including updates that
  reuse cached conditions.
- `DisconnectAsync` leaves the RTC room and closes the server session, retaining
  the engine and local preview. Without a connection, it also leaves local media preparation running.
  `CloseAsync` cancels local media preparation, invalidates local input, and
  releases its buffers.
- Await `CloseAsync` and dispose texture uploaders when leaving a screen.

Native frames are copied before returning from Agora media callbacks and
forwarded on Unity's thread through a bounded latest-frame mailbox. SDK arrays
and scaling buffers may be reused after callbacks. Do not mutate input during
submission; call `Clone()` before retaining callback frames.

For copied output with timeline metadata, register a separate listener on the
Unity main thread before generation:

```csharp
realtime.SetRemoteMediaFrameListener(packet =>
{
    // This callback runs on a serial background queue. Do not use Unity APIs here.
    // packet.VideoFrame owns its pixel arrays and may be retained after return.
    // Reset downstream continuity when packet.IsDiscontinuous is true.
});
// Remove the listener and discard queued output on the Unity main thread:
// realtime.SetRemoteMediaFrameListener(null);
```

`RealtimeMediaFrame` includes a stream `Guid`, the copied `VideoFrame`, its
receiver timestamp in microseconds, `DurationMicroseconds`, and `IsDiscontinuous`.
Video duration uses the interval between consecutive valid timestamps. The first
frame and nonpositive or one-second-and-longer intervals use 1/24 second, rounded
to 41667 microseconds. A newly bound remote source gets a new stream ID, including
republication within the same generation task. Unpublishing clears the cached
frame, cancels old first-frame waits, and discards pending copied output.
The first packet, timestamp regressions, format changes,
native mailbox drops, and output queue drops mark continuity changes. Output
is bounded to 256 packets and 32 MiB of pixel data, including an executing
callback. Replacing a listener or resetting rendering discards queued packets;
an already executing callback may finish. The existing main-thread frame
callbacks remain suitable for Unity textures and retain their borrowed-buffer
contract.

The remote track's `VideoFormat` retains the media baseline's frame rate and
encoding options while reflecting requested return dimensions. This describes
the requested format; actual receive frame rate is reported by statistics. The
server downlink bitrate policy independently uses 24 fps.

`StateChanged` immediately supplies the current state on registration. Local
stream creation transitions through Preparing to Ready. Connection startup uses
Connecting and Connected; generation becomes Generating after the first valid
result frame. Final operation notifications occur after admission is released,
so their handlers can submit the next operation.

Cleanup publishes Disconnecting and then Ready when local media remains, or Idle
when no local media remains. Reasons are Normal, OrientationChanged, or Failure;
DisconnectAsync(RealtimeReason.OrientationChanged) supplies an explicit reason.
CloseAsync always returns to Idle. Awaited failures are returned through their
tasks; failures that end the workflow also appear in state.Reason. Runtime
failures raise ErrorOccurred after cleanup, and server closure failures raise
recoverable CleanupWarning events. Subscriber exceptions are isolated.
Concurrent cleanup requests use the broadest requested scope. An error during
an earlier cleanup pass does not skip an expanded Close request's local cleanup.

## Video statistics and device performance

Statistics and alarms are delivered on the creating Unity main thread regardless
of logger options. Uplink snapshots include encoded dimensions, capture, encode
and send frame rates, actual bitrate, and packet loss. Downlink snapshots include
the result account, received dimensions, decode and native RTC render frame rates,
bitrate, packet loss, and RTC media end-to-end delay. This delay excludes complete
AI generation time. Null metric fields are unavailable; zero remains measurable.

UplinkVideoStatisticsChanged receives null when publication availability resets;
DownlinkVideoStatisticsChanged receives null when its result binding is cleared.
PerformanceAlarm reports Limited or Recovered; SuggestedVideoFormat is null if
native fallback dimensions or frame rate are invalid. Native callbacks from
previous connections are rejected. Ordinary Agora engine errors are logged;
connection failure and token expiration trigger lifecycle cleanup.

## Storage

Create a manager from the client to upload image/video bytes or local files,
optionally check image safety, and download HTTP objects. Storage uses the
general API endpoint for the selected China/Global environment and is
independent of realtime connections. Create and call the manager on the Unity
main thread; progress callbacks are delivered there as well.

```csharp
var storage = client.CreateStorageManager();
var image = await storage.UploadImageWithSafetyCheckAsync(
    imageBytes,
    "reference.png",
    "image/png",
    progress: value => Debug.Log($"{value.CompletedBytes}/{value.TotalBytes}")
);
var context = new RealtimeContext("A watercolor landscape", image.Url.AbsoluteUri);

// File overloads infer MIME type from the extension when contentType is omitted.
var video = await storage.UploadVideoAsync(videoPath, cancellationToken: token);
var downloaded = await storage.DownloadVideoAsync(
    video.Url,
    System.IO.Path.Combine(Application.persistentDataPath, "result.mp4"),
    cancellationToken: token
);
```

The public interface is `IXmaxStorageManager`. `UploadImageAsync`,
`UploadImageWithSafetyCheckAsync`, and `UploadVideoAsync` each accept either
bytes with a filename and explicit MIME type, or an existing file path with an
optional MIME type. Keep byte data and source files unchanged until completion.
Uploads use one COS PUT Object request and support objects up to 5 GiB; file
uploads stream from disk. Individual storage transfers have a 300-second
timeout. Uploaded results contain `Url`, `ObjectKey`, and an optional `ETag`.

Each upload retrieves temporary credentials from `GET /cos/sts`, then uploads
to the configured bucket and endpoint. Object names use the server prefix,
Unix milliseconds, a lowercase UUID, and a sanitized filename. Safety-checked
uploads then call `POST /cos/image/check`; a rejected image raises `UnsafeImage`,
and a successful result uses the safety service's URL. A completed upload is
not deleted if a later safety check fails or is cancelled.

`DownloadImageAsync` and `DownloadVideoAsync` stream to a temporary sibling of
the destination. The parent directory must exist. A successful transfer commits
the destination by move or atomic replacement; HTTP failure and cancellation
leave an existing destination intact and remove the partial file. Downloaded
results contain the absolute `FilePath` and `ByteCount`.

`XmaxStorageProgress` reports completed/expected bytes; `TotalBytes` is zero and
`FractionCompleted` is null when the expected length is unavailable. Upload
progress reaches completion before an optional safety check finishes. Await the
operation for the final result. All operations accept `CancellationToken`;
cancellation raises `OperationCanceledException`. `UploadError`, `DownloadError`,
`UnsafeImage`, and the existing API/configuration errors distinguish failures.

Business logs include best-effort image or file-video dimensions, readable file
sizes, and elapsed time. In-memory video and unavailable metadata use `--`.
Metadata inspection does not start a built-in media source or prevent uploads.
Transport errors preserve the platform error domain and code; COS XML errors
also retain the service code, message, and request ID when supplied. Credentials
remain redacted from diagnostic logs.

The COS transport follows the official [PUT Object](https://cloud.tencent.com/document/product/436/7749)
and [request signature](https://cloud.tencent.com/document/product/436/7778)
contracts. Temporary credentials and signing values are excluded from logs.

## Internal component boundaries

The Unity implementation follows the iOS responsibility boundaries while
retaining Unity main-thread operation admission and host-owned capture.

| Layer | Components and responsibility |
| --- | --- |
| Core / Storage | `XmaxStorageManager` adapts results and progress through `IXmaxStorageManager`, depending on `IStorageService`. |
| Core | `XmaxRealtimeManager` exposes the API and admits operations through LifecycleManager. ConnectionManager owns sessions, transport quality preparation and execution, and remote format updates. GenerationManager owns conditions, interaction lifecycle, and first-frame readiness. Factory assembles shared components; ErrorManager routes failures. |
| Service / Storage | `StorageService` validates media and coordinates temporary credentials, object names, and image safety through the general business API. |
| Foundation / Storage | `StorageManager` implements streamed COS transfers and atomic download commits behind `IStorageManager`; `StorageRequestSigner` owns request authorization. |
| Service | Realtime model, state, context, stream, track, and format contracts sit beside session services. `MediaService` resolves offline dimensions. |
| Media / Render | Media owns local input and baseline normalization; interaction binds normalized media points to the generation task; Core depends on `IRenderController` for remote track binding and first-frame readiness. |
| Stream | `StreamController` coordinates result publication and confirmation. `RoomController` owns room signaling and `RoomHeartbeat`; `EncodingController` applies `EncodingBitratePolicy`; `QualityController` owns directional policies and history. |
| Foundation / RTC | `RtcManager` owns engine leases, join waits, and connection validity. `IRtcPlatform` and `IRtcRoom` isolate native APIs. Agora platform, room, event bridge, input conversion, and remote frame mailbox remain separate adapters. |

Core depends on IMediaController, IInteractionController, IRenderController,
IStreamController, and IMediaService. LifecycleManager owns state, operation
admission, request replacement, and scoped cleanup. RealtimeModel is the public
model selection; registry definitions remain internal.
Factory.Make supplies shared defaults; Factory.Assemble wires explicitly
supplied controllers and services into an immutable XmaxRealtimeComponents
graph. The internal manager constructor accepts that graph directly. The
default graph shares one IMediaService with the facade and transport quality
controller, including final downlink size resolution. Custom assembled stream
controllers own the dependencies supplied to them.
Manual and automatic quality changes share ConnectionManager's execution path.
QualityController retains the directional policy, cooldown, and history state;
the facade forwards committed quality events without applying encoder or
return-size changes itself.
GenerationManager creates each task identifier and passes it to StreamController,
which retains it only for signaling, stream confirmation, and task validity.

RTC native types stay inside the Foundation adapters; Core, Service, and Stream
consume neutral SDK contracts. The component graph shares one transport and
one rendering controller, so lifecycle cleanup and frame delivery operate on
the same connection. No frame interpolation pipeline is included.

## Logging and tests

Logger options are process-wide and applied when a client is created:
None, Business, Performance, or All. Creating a manager from an existing
client does not overwrite a newer client's logging settings. Detail labels use
Chinese for China and English for Global; custom URLs use Chinese unless they
use a known Global API host. Every line includes `[Xmax][Category]`.

Business logs include API status, duration and response size, failed response
contents, complete outbound room signals, quality adjustments, and structured
errors with SDK/API/HTTP codes. Successful API response bodies are omitted.
Authentication fields are redacted; enabling Business logs may expose generation
conditions and trajectory contents, as in the iOS SDK.

Performance logs include actual Agora uplink and downlink statistics: resolution,
bitrate, capture/encode/send or decode/render frame rates, packet-loss percentage,
and downlink RTC end-to-end media delay. This media delay does not measure the
complete AI generation time. Unavailable metrics use `—`; valid zero measurements
remain zero. Network quality ratings remain available through their existing event.

`RealtimeTiming` reports total startup duration and exactly four stages:

1. Session creation.
2. RTC room join.
3. Local stream publication (the successful native publication request).
4. First-frame arrival, measured from generation signaling to a valid frame ready
   for display, including signaling and result-stream confirmation.

Failed startup includes elapsed time, partial stages, the pending stage, and the
failure reason. Cancellation does not produce a startup failure log. Tasks from
previous attempts cannot finish the current measurement. Timing uses a monotonic
clock and the `Realtime` log category. Agora's native logging is configured
separately from XmaxSDK logging.

Install Unity Test Framework 1.6.0 and add `"testables": ["ai.xmax.sdk"]` to the
consumer project's package manifest to run package EditMode tests. Editor tests
use fake sessions and transports; Android device verification is needed for
native join, frame input, result frames, and runtime quality changes.
