# Agora Unity RTC runtime

This directory vendors the managed Unity wrapper and Android ARM64 runtime
from Agora Unity RTC SDK 4.6.2 build.1.

Official archive:
https://download.agora.io/sdk/release/Agora_Unity_RTC_SDK_FULL_20260212_633_4.6.2-build.1.zip

Archive SHA-256: `be76609e3a3bcab6de99d2d397117050b53954d50e7aeb5d4565fcd542a2e53d`.

Included: the upstream `Code/` tree, wrapper and RTC Java libraries, and the
Android ARM64 native libraries from `AgoraRtcEngineKit.plugin`.
Example scenes, desktop/iOS libraries, other Android architectures, and the
JavaDoc archive are omitted. Upstream source headers and Unity GUIDs are kept.

Integration changes:

- `AgoraRtcSDK.asmdef` explicitly references `UnityEngine.UI`.
- The Android library importer is architecture-neutral; its only native ABI is
  ARM64. Your consuming Android application must build for ARM64.
- `RtcEngineEventHandlerNative.OnEvent` captures the registered handler before
  enqueueing callbacks and passes that handler to `OnEventGen`. Deferred events
  therefore retain their original recipient when XmaxSDK leaves and rejoins
  using the same engine. Preserve this change in both observer files when
  updating or regenerating the wrapper; reading the global handler at delivery
  time can redirect an old connection's callbacks into a new room.

`Runtime/link.xml` preserves this wrapper's reflected RTC parameter and callback
types when the consuming app uses IL2CPP managed stripping.

XmaxSDK owns capture and rendering integration outside this vendor directory.
Camera and microphone capture are disabled in the adapter; your app owns
camera permissions and capture. The vendor manifest contains its upstream
permission declarations. Agora files retain their upstream licensing terms;
the XmaxSDK MIT license does not replace those terms.
