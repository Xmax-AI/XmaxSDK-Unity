<p align="center">
  <img src="./Materials/images/brand/xmax-sdk.png" alt="XmaxSDK — Realtime Interactive Video Generation" width="880">
</p>

<p align="center">
  <a href="https://unity.com/"><img src="https://img.shields.io/badge/Unity-6000.0%2B-000000?logo=unity" alt="Unity 6000.0+"></a>
  <a href="https://developer.android.com/"><img src="https://img.shields.io/badge/Android-ARM64-3DDC84?logo=android" alt="Android ARM64"></a>
  <a href="https://platform.xmaxai.com/"><img src="https://img.shields.io/badge/Realtime-AI-FF9500" alt="Realtime AI"></a>
  <a href="./LICENSE"><img src="https://img.shields.io/badge/License-MIT-4C9A2A" alt="MIT License"></a>
</p>

We introduce XmaxSDK, a Unity SDK designed for real-time interactive video generation via Xmax models. XmaxSDK implements an end-to-end pipeline covering media acquisition, video streaming, realtime generation, and on-device rendering, enabling developers to seamlessly integrate low-latency, high-fidelity video transformations into creative applications at a much lower cost than alternative solutions.

<p align="center"><img src="./Materials/images/xlab/generation-demo.gif" alt="X-Lab realtime generation demo" width="33%" /><img src="./Materials/images/xlab/index-demo.gif" alt="X-Lab index demo" width="33%" /><img src="./Materials/images/xlab/storage-demo.gif" alt="X-Lab storage demo" width="33%" /></p>

<br>

## Why XmaxSDK?

<table>
  <thead>
    <tr>
      <th height="104" align="center" valign="middle">
        <img src="./Materials/images/why/low-latency.svg" alt="Low latency" width="36" height="36"><br>Low latency
      </th>
      <th height="104" align="center" valign="middle">
        <img src="./Materials/images/why/low-cost.svg" alt="Cost efficiency" width="36" height="36"><br>Cost efficiency
      </th>
      <th height="104" align="center" valign="middle">
        <img src="./Materials/images/why/high-fidelity.svg" alt="High fidelity" width="36" height="36"><br>High fidelity
      </th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>End-to-end latency is measured in <img src="./Materials/images/why/latency-highlight.svg" alt="hundreds of milliseconds" width="192" height="20" align="absmiddle">, ensuring that updates to generation conditions and interaction controls are reflected instantly.</td>
      <td>Run on a <img src="./Materials/images/why/gpu-highlight.svg" alt="single RTX 5090" width="126" height="20" align="absmiddle">, reducing inference costs by orders of magnitude versus datacenter GPUs like H100.</td>
      <td>Our models support real-time generation at up to <img src="./Materials/images/why/resolution-highlight.svg" alt="1080p" width="48" height="20" align="absmiddle">, delivering production-ready, high-quality video output.</td>
    </tr>
  </tbody>
</table>

<br>

## Prerequisites

- Unity 6000.0 or later
- Android build target (including the ARM64 Android runtime on PICO devices)
- An Xmax API key

> [!WARNING]
> Never commit your Xmax API key to version control. Pass it securely at
> runtime or use short-lived temporary keys issued by the Xmax API. For
> step-by-step instructions, see
> [Authentication](https://platform.xmaxai.com/docs/authentication).

<br>

## Installation

XmaxSDK is distributed as a Unity package. Add a local UPM dependency to
your project's `Packages/manifest.json`:

```json
"ai.xmax.sdk": "file:../../XmaxSDK"
```

To run the SDK regression tests, install `com.unity.test-framework` 1.6.0
and add `"testables": ["ai.xmax.sdk"]`.

<br>

## Quick Start

### Configure permissions

Add a camera usage declaration to your application's Android manifest:

```xml
<uses-permission android:name="android.permission.CAMERA" />
```

Camera permissions and capture are managed by the host — the SDK never
requests system permissions and never touches the camera itself.

<br>

### Generate and display video

The following snippet creates a local camera stream, starts real-time
generation, and binds the remote track to an I420 material. Create the
manager and call its APIs on the Unity main thread:

```csharp
using System.Threading.Tasks;
using UnityEngine;
using Xmax.SDK;

var client = new XmaxClient(
    new XmaxConfiguration("YOUR_XMAX_API_KEY")
);

var realtime = client.CreateRealtimeManager(
    new RealtimeConfiguration(RealtimeModel.X2_1_1_Preview_CN)
);

// Describe the actual source dimensions. This example uses 1920×1080@30.
// XmaxSDK resolves the local media baseline and generation canvas separately.
var localStream = realtime.CreateLocalExternalStream(
    new RealtimeVideoFormat(1920, 1080, 30)
);

// Start your camera pipeline and push every frame on the main thread:
// localStream.PushVideoFrame(XmaxVideoFrame.CreateRgba(rgba, width, height));

var remoteStream = await realtime.StartGenerationAsync(
    localStream,
    new RealtimeContext(
        prompt: "视频中角色替换成参考图中角色",
        referencePath: "https://platform.xmaxai.com/images/source/charx/chatx_image1.jpg"
    )
);

// The shader of i420Material must sample _YTex, _UTex, and _VTex; the SDK
// handles strides, texture upload, and size changes.
var texture = new XmaxVideoTexture();
texture.Bind(remoteStream.VideoTrack, i420Material);
```

`ConnectAsync` and RTC only run on Android Player; calling them in the Unity
Editor returns `NotSupported`. Local camera preview does not require a valid
API key — the key and service URL are validated at connect time.

<br>

### Listen for events

After creating `realtime`, register the listeners you need before creating
the input stream or starting generation.

| Listener | Purpose |
| --- | --- |
| `StateChanged` | Observe media readiness and realtime states; cleanup returns to `Ready` or `Idle`, with a termination `Reason`. |
| `RemoteFrameReceived` | Receive borrowed generated frames on the Unity thread. |
| `SetRemoteMediaFrameListener` | Register copied video packets with stream identity and continuity metadata on a serial background queue. |
| `NetworkQualityChanged` | Monitor uplink and downlink network quality. |
| `UplinkVideoStatisticsChanged` / `DownlinkVideoStatisticsChanged` | Receive measured video statistics, or null when unavailable after reset. |
| `PerformanceAlarm` | Receive device encoding limitation or recovery with an optional suggested format. |

For example, monitor state changes and termination reasons:

```csharp
realtime.StateChanged += state =>
{
    Debug.Log($"State: {state.ConnectionState}");
    if (state.Reason?.Kind == RealtimeReasonKind.Failure)
        Debug.Log($"Ended with {state.Reason.Error.Code}: {state.Reason.Error.Message}");
};
```

Handle errors thrown by async calls with `try/catch`. Failures that end the
realtime workflow are reported through `ErrorOccurred` after cleanup
completes, sharing the same error instance as the terminal state's `Reason`;
server session close failures are reported as recoverable `CleanupWarning`
events.

<br>

### Models, media sizing, and adaptive quality

Use `RealtimeModel.X2_1_1_Preview_CN` or `RealtimeModel.X2_1_1_Preview_US`
for the corresponding regional preview model. The previous `X2_0` and
`X2_0_Pro` models are no longer supported.

External input preserves its aspect ratio within the model's 200,000–2,100,000
pixel range. `LocalStream.VideoFormat` describes the resolved media baseline;
`SourceVideoFormat` retains the input format. Generation uses the closest
`1024×1920` or `1920×1024` canvas and requests containment when aspect ratios
differ.

Adaptive quality is enabled by default. Uplink and downlink independently
select L1–L5, using recent model-specific history or L2 without valid history.
To start at L1 and control quality manually, pass
`isAdaptiveQualityEnabled: false` to `RealtimeConfiguration`.

```csharp
await realtime.SetUplinkQualityLevelAsync(RealtimeQualityLevel.L3);
await realtime.SetDownlinkQualityLevelAsync(RealtimeQualityLevel.L2);
```

These APIs require active generation. A downlink change completes when the
configuration has been submitted; received frames report the actual output
dimensions. Media baseline dimensions and interaction coordinates stay
unchanged when transport quality changes.

`SendTracks` accepts normalized Unity `Vector2` coordinates from top-left
`(0,0)` to bottom-right `(1,1)` across the full media.

<br>

### Upload reference media

Storage uses the client's general service endpoint and works independently of
realtime connections. Create and call its APIs on the Unity main thread:

```csharp
var storage = client.CreateStorageManager();
var uploaded = await storage.UploadImageWithSafetyCheckAsync(
    imageBytes,
    "reference.png",
    "image/png"
);
var context = new RealtimeContext("A watercolor landscape", uploaded.Url.AbsoluteUri);
```

Image and video uploads accept bytes or existing files, support progress and
cancellation, and return the object URL and key. Downloads stream to a temporary
file before committing the destination. See [Storage](./Materials/usage.md#storage)
for overloads, transfer limits, and error handling.

<br>

### Resource Cleanup

- **`DisconnectAsync` — Stop Remote Generation**

  Stops remote generation, leaves the RTC room, and closes the server session
  while retaining the engine with the local stream and preview. Use
  this when ending the online session but staying on the current screen. You
  can start a new session later using the same local stream:

  ```csharp
  await realtime.DisconnectAsync();
  ```

- **`CloseAsync` — Full Teardown & Release**

  Ends the remote session, invalidates the local stream, and releases all
  engine resources. Use this when leaving or dismissing the generation
  screen:

  ```csharp
  await realtime.CloseAsync();
  ```

> **Note:** These methods are alternatives, not sequential steps. When
> exiting a screen, call `CloseAsync` directly — there is no need to call
> `DisconnectAsync` first.

<br>

> [!TIP]
> For complete usage examples, including explicit local streams, encoding
> configuration, interaction coordinates, and logging options, see the
> [usage guide](./Materials/usage.md).

<br>

## Dependencies

- <ins><strong>Agora RTC SDK</strong></ins> enables low-latency, realtime video communication.

<br>

## Contact us

For integration assistance and technical support, contact us at
[sdk@xmax.ai](mailto:sdk@xmax.ai).

<br>

## License

XmaxSDK is available under the terms of the [MIT License](LICENSE).
