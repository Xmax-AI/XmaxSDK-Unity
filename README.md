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

Use your front camera to turn yourself into a character from a reference image in real time.

```csharp
using Xmax.SDK;

// Initialize the client
var client = new XmaxClient(new XmaxConfiguration(
    "<YOUR_XMAX_API_KEY>",
    XmaxEnvironment.Global
));

// Initialize the realtime manager
var realtime = client.CreateRealtimeManager(new RealtimeConfiguration(RealtimeModel.X2_1));

// Create the video renderer
var remoteTexture = new XmaxVideoTexture();
// i420Material is the display object’s material, supporting _YTex, _UTex, and _VTex

// In Unity, capture camera frames in your app and submit them through an external stream
var videoFormat = new RealtimeVideoFormat(1280, 720, 30);
var localStream = realtime.CreateLocalExternalStream(videoFormat);

// Call continuously from the capture callback on the main thread with upright 1280 × 720 RGBA pixels
void SubmitFrame(byte[] rgba, long timestampMicroseconds)
{
    localStream.PushVideoFrame(XmaxVideoFrame.CreateRgba(
        rgba, videoFormat.Width, videoFormat.Height,
        timestampMicroseconds: timestampMicroseconds
    ));
}

// Start generation after frame delivery begins, and keep submitting frames while awaiting it
var remoteStream = await realtime.StartGenerationAsync(localStream, new RealtimeContext(
    prompt: "视频中角色替换成参考图中角色",
    referencePath: "https://cdn.xmax.ai/images/playground/charx/charx_image1.png?v=26&presetVersion=20261022"
));

// Bind the generated video
remoteTexture.Bind(remoteStream.VideoTrack, i420Material);

// Run the following when generation is no longer needed
// Stop camera frame delivery in your app before releasing SDK resources
await realtime.CloseAsync();
remoteTexture.Dispose();
```

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
