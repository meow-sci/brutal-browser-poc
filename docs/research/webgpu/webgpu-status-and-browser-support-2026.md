---
type: Technology Assessment
title: WebGPU status and browser support, October 2026
description: Which browsers ship WebGPU on which OS/GPU (with versions and dates), which engine and native backend each uses, availability requirements (secure context, workers, OffscreenCanvas), canvas configuration and presentation semantics, device loss, and what this means for a BRUTAL Vulkan-on-WebGPU PoC.
tags: [webgpu, browsers, chrome, firefox, safari, dawn, wgpu, canvas, status]
sources:
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026
  - id: webgpu-cr
    resource: https://www.w3.org/TR/webgpu/
    title: WebGPU W3C Candidate Recommendation Draft, 15 September 2026
  - id: impl-status
    resource: https://github.com/gpuweb/gpuweb/wiki/Implementation-Status
    title: gpuweb wiki — Implementation Status (edited 2026-10-02)
  - id: caniuse
    resource: https://caniuse.com/webgpu
    title: Can I use — WebGPU (data 2026-10-13)
  - id: chrome-113
    resource: https://developer.chrome.com/blog/webgpu-release
    title: Chrome ships WebGPU (Chrome 113)
  - id: chrome-121
    resource: https://developer.chrome.com/blog/new-in-webgpu-121
    title: What's New in WebGPU (Chrome 121) — Android
  - id: chrome-144
    resource: https://developer.chrome.com/blog/new-in-webgpu-144
    title: What's New in WebGPU (Chrome 144) — Linux Intel Gen12+
  - id: chrome-147
    resource: https://developer.chrome.com/blog/new-in-webgpu-147-148
    title: What's New in WebGPU (Chrome 147–148) — Linux NVIDIA Wayland
  - id: chrome-146
    resource: https://developer.chrome.com/blog/new-in-webgpu-146
    title: What's New in WebGPU (Chrome 146) — compatibility mode
  - id: chrome-news
    resource: https://developer.chrome.com/docs/web-platform/webgpu/news
    title: Chrome "What's New in WebGPU" index
  - id: chrome-cadence
    resource: https://developer.chrome.com/blog/chrome-two-week-start
    title: Chrome two-week release cadence (from Chrome 153, 2026-09-08)
  - id: ff-141
    resource: https://mozillagfx.wordpress.com/2025/07/15/shipping-webgpu-on-windows-in-firefox-141/
    title: Mozilla Gfx — Shipping WebGPU on Windows in Firefox 141
  - id: ff-145
    resource: https://www.firefox.com/en-US/firefox/145.0/releasenotes/
    title: Firefox 145 release notes (WebGPU on macOS Tahoe, Apple Silicon)
  - id: ff-147
    resource: https://www.firefox.com/en-US/firefox/147.0/releasenotes/
    title: Firefox 147 release notes (WebGPU on all supported macOS, Apple Silicon)
  - id: safari-26
    resource: https://webkit.org/blog/17333/webkit-features-in-safari-26-0/
    title: WebKit Features in Safari 26.0 (2025-09-15)
  - id: safari-27
    resource: https://webkit.org/blog/18325/webkit-features-for-safari-27-0/
    title: WebKit Features for Safari 27.0
  - id: dawn-support
    resource: https://raw.githubusercontent.com/google/dawn/main/docs/support.md
    title: Dawn docs/support.md (backend requirements)
  - id: wgpu-readme
    resource: https://github.com/gfx-rs/wgpu
    title: wgpu README (backends)
  - id: status-agent
    resource: "scope: sub-agent desk research on browser status, 2026-10-16"
    title: Browser status research compiled 2026-10-16 (sources inline above)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
stale_after: 2027-01-16
---

# Snapshot (2026-10-16)

- Spec: W3C Candidate Recommendation Draft (15 Sep 2026); Editor's Draft 8 Oct 2026.[^webgpu-cr][^webgpu-spec]
- Current stable releases: Chrome 155 (stable 2026-10-06; Chrome moved to a two-week cadence from 153), Firefox 158 (2026-10-13), Safari 27.0.[^chrome-cadence][^status-agent]
- WebGPU is **on by default in all three engines** on their primary desktop platforms; the gaps are Linux (Chrome partial, Firefox not yet), Firefox Android, Intel Macs in Firefox, and Safari only on OS 26+.

# Browser support matrix

| Browser | Platform / GPU | Since | Date | Notes |
| --- | --- | --- | --- | --- |
| Chrome / Edge | Windows (D3D12), macOS (Metal), ChromeOS (Vulkan) | 113 | 2023-05-02 | default on[^chrome-113][^impl-status] |
| Chrome | Windows on ARM64 | — | — | behind `chrome://flags/#enable-unsafe-webgpu`[^impl-status] |
| Chrome | Android 12+, Qualcomm/ARM/Intel GPUs | 121 | 2024-01-23 | [^chrome-121] |
| Chrome | Android 16+, Imagination GPUs | 139 | 2025-08-05 | [^impl-status] |
| Chrome | Android GLES 3.1-only devices | 146 | 2026-03-10 | opt-in **compatibility mode** only[^chrome-146] |
| Chrome | Linux, Intel Gen12+ | 144 | 2026-01-13 | Vulkan backend[^chrome-144] |
| Chrome | Linux, NVIDIA on Wayland (driver ≥ 535.183.01) | 147 | 2026-04-07 | [^chrome-147] |
| Chrome | Linux, AMD and others | — | — | flag only (`--enable-unsafe-webgpu --ozone-platform=x11 --use-angle=vulkan --enable-features=Vulkan,VulkanFromANGLE`)[^impl-status] |
| Firefox | Windows | 141 | 2025-07-22 | default on[^ff-141] |
| Firefox | macOS 26 Tahoe, Apple Silicon | 145 | 2025-11-11 | [^ff-145] |
| Firefox | all supported macOS, Apple Silicon | 147 | 2026-01-13 | [^ff-147] |
| Firefox | Intel Macs | Nightly | — | [^impl-status] |
| Firefox | Linux | Nightly default; release behind `dom.webgpu.enabled` | — | Mozilla "expects to ship on Linux in 2026"[^impl-status] |
| Firefox | Android | flag (`gfx.webgpu.ignore-blocklist`, Beta/Nightly) | — | [^impl-status] |
| Safari | macOS 26, iOS 26, iPadOS 26, visionOS 26 | 26.0 | 2025-09-15 | default on[^safari-26] |
| Safari | Safari 26/27 on macOS 15/14 | — | — | not on by default per caniuse[^caniuse] |
| Safari | macOS/iOS 27 | 27.0 | ~2026-09 | adds `clip_distances`; fixes `maxStorageBuffersInFragmentStage`[^safari-27] |
| Samsung Internet | Android | 24+ (UNVERIFIED date) | — | Chromium-based[^caniuse] |
| Opera | desktop | 99+ | — | not on Linux by default[^caniuse] |

Flags: Chrome `chrome://flags/#enable-unsafe-webgpu`; Firefox `dom.webgpu.enabled` (+ `gfx.webgpu.ignore-blocklist`); Safari had a "WebGPU" feature flag before 26.[^impl-status][^caniuse]

**PoC test matrix recommendation**: Chrome stable on macOS (Apple Silicon) and Windows as primary; Safari 26+/27 on macOS as second engine (catches WebKit/Metal differences and the `bgra8unorm` vs `rgba8unorm` canvas format split); Firefox 141+ on Windows/macOS as third. Linux only on Chrome with Intel/NVIDIA-Wayland.

# Engines and native backends

| Engine | Used by | Native backends | Shader compiler |
| --- | --- | --- | --- |
| Dawn (C++) | Chrome, Edge, Opera, Samsung Internet | D3D12 (Windows, preferred), Metal (macOS 13+ / iOS), Vulkan 1.1 (Linux, ChromeOS, Android), OpenGL ES 3.1 (compat mode on Android), D3D11 (work in progress)[^dawn-support] | Tint (WGSL → HLSL/MSL/SPIR-V/GLSL) |
| wgpu (Rust) | Firefox, Servo | Vulkan, Metal, DX12 first-class; GL best-effort[^wgpu-readme] | Naga |
| WebKit WebGPU | Safari | Metal only[^safari-26] | WebKit's own WGSL compiler |

Consequence: the same WGSL/WebGPU calls run on three very different native stacks; validation is uniform (spec), performance is not. Dawn's Vulkan requirements (`depthBiasClamp`, `imageCubeArray`, `independentBlend`, `sampleRateShading`, Vulkan 1.1) are a reminder that "WebGPU core" ≈ a modest Vulkan 1.1 device.[^dawn-support]

# Availability requirements

- **Secure context**: `navigator.gpu` and every `GPU*` interface are `[SecureContext]`; served over HTTPS or `http://localhost` (Vite's dev server on localhost qualifies).[^webgpu-spec]
- **Workers**: `WorkerNavigator includes NavigatorGPU`; Chrome supports dedicated/shared/service workers (service/shared since 124), Firefox everything except service workers, Safari 26 exposes `WorkerNavigator.gpu`.[^webgpu-spec][^status-agent]
- **OffscreenCanvas**: `getContext("webgpu")` works on `OffscreenCanvas`, so rendering can live in a dedicated worker with `transferControlToOffscreen()`; presentation of a worker-owned canvas happens in the worker's "update the rendering" step (needs `requestAnimationFrame` in the worker).[^webgpu-spec]
- **Cross-origin isolation** is not required for WebGPU (it is required separately if .NET multithreading with `SharedArrayBuffer` is used — see `/dotnet-wasm/`).[^webgpu-spec]
- No Permissions Policy is defined for WebGPU in the ED (UNVERIFIED that no browser adds one).[^status-agent]

# Canvas configuration and presentation (swapchain semantics)

| Aspect | WebGPU behaviour | Implication for `VK_KHR_swapchain` emulation |
| --- | --- | --- |
| Preferred format | `navigator.gpu.getPreferredCanvasFormat()` returns only `"rgba8unorm"` or `"bgra8unorm"` (in practice `bgra8unorm` on macOS/Windows Chrome, `rgba8unorm` on Android/ChromeOS) (platform split UNVERIFIED per engine)[^webgpu-spec] | `vkGetPhysicalDeviceSurfaceFormatsKHR` reports the preferred format first (`B8G8R8A8_UNORM` or `R8G8B8A8_UNORM`) plus its `_SRGB` twin |
| Allowed formats | `bgra8unorm`, `rgba8unorm`, `rgba16float`; **no srgb canvas formats** — use the non-srgb format and list the `-srgb` format in `viewFormats`[^webgpu-spec] | `_SRGB` surface format = configure with UNORM + `viewFormats: [srgb]`, swapchain image views created with the srgb view format |
| Alpha | `alphaMode: "opaque"` (default) or `"premultiplied"`[^webgpu-spec] | `VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR` / `PRE_MULTIPLIED_BIT_KHR`; no POST_MULTIPLIED/INHERIT |
| Colour space / HDR | `colorSpace` (`srgb`, `display-p3`, and since Chrome 155–156 `srgb-linear`, `display-p3-linear`), `toneMapping: {mode: "standard"|"extended"}` with `rgba16float`[^webgpu-spec][^chrome-news] | `VK_COLOR_SPACE_SRGB_NONLINEAR_KHR` always; `EXTENDED_SRGB_LINEAR` / `DISPLAY_P3_*` optional via `VK_EXT_swapchain_colorspace` |
| Usage | defaults to `RENDER_ATTACHMENT`; may add `COPY_SRC/COPY_DST/TEXTURE_BINDING/STORAGE_BINDING` explicitly[^webgpu-spec] | report `supportedUsageFlags` = COLOR_ATTACHMENT \| TRANSFER_SRC \| TRANSFER_DST \| SAMPLED (configure with all of them) |
| Current texture | `getCurrentTexture()` returns the frame's texture, creating it on first call per frame; the same object is returned until the texture *expires*[^webgpu-spec] | `vkAcquireNextImageKHR` → call `getCurrentTexture()` and bind it to one of N proxy `VkImage`s (index rotates 0..N−1) |
| Presentation | happens in the event loop's "update the rendering" step for the document (or dedicated worker); the current texture expires at or before that point; an app should call `getCurrentTexture()` in the same task that renders to it[^webgpu-spec] | `vkQueuePresentKHR` does not present — it marks end-of-frame; the **C# frame function must return to the browser** (rAF callback) for the image to appear |
| Resize | the canvas drawing buffer size = `canvas.width/height`; changing them reallocates on the next `getCurrentTexture()`[^webgpu-spec] | detect `canvas` size ≠ swapchain extent (ResizeObserver with `devicePixelContentBoxSize`) → return `VK_ERROR_OUT_OF_DATE_KHR`/`VK_SUBOPTIMAL_KHR` from acquire/present so the app recreates its swapchain |
| Present modes | none — always compositor-synchronized, at most one frame per rAF | report only `VK_PRESENT_MODE_FIFO_KHR`; `minImageCount` 2, `maxImageCount` 3 (proxies) |
| Drawing buffer persistence | the drawing buffer keeps the last contents and is replaced (cleared) on `getCurrentTexture()` of the next frame[^webgpu-spec] | `LOAD_OP_LOAD` on a freshly acquired swapchain image sees cleared (transparent black) contents, not the previous frame — same as Vulkan's "undefined after acquire" |

# Device loss and errors

- `GPUDevice.lost` is a promise resolving with `GPUDeviceLostInfo { reason: "unknown"|"destroyed", message }`; after loss, operations become no-ops and errors are no longer surfaced.[^webgpu-spec]
- Validation errors are **asynchronous**: delivered through `uncapturederror` events or `pushErrorScope/popErrorScope` promises, not as return values.[^webgpu-spec]
- ICD mapping: on `lost`, set a flag so subsequent `vkQueueSubmit`, `vkWaitForFences`, `vkAcquireNextImageKHR` return `VK_ERROR_DEVICE_LOST`; route `uncapturederror` messages to every `VkDebugUtilsMessengerEXT` as `ERROR | VALIDATION` (after the fact — the offending Vulkan call has already returned `VK_SUCCESS`). Recovery = recreate everything, as in Vulkan.

# Open questions

- UNVERIFIED: exact `getPreferredCanvasFormat()` result per browser/OS (Firefox and Safari on macOS especially).
- UNVERIFIED: Samsung Internet 24 date and GPU coverage; whether Edge on Linux follows Chrome 144/147.
- UNVERIFIED: whether WebGPU is on by default in Safari 26/27 on macOS 15 (caniuse says no; WebKit does not say).
- UNVERIFIED: Firefox stable on Linux by default (no evidence as of Firefox 158).

[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026
[^webgpu-cr]: WebGPU W3C Candidate Recommendation Draft, 15 September 2026
[^impl-status]: gpuweb wiki — Implementation Status (edited 2026-10-02)
[^caniuse]: Can I use — WebGPU (data 2026-10-13)
[^chrome-113]: Chrome ships WebGPU (Chrome 113)
[^chrome-121]: What's New in WebGPU (Chrome 121) — Android
[^chrome-144]: What's New in WebGPU (Chrome 144) — Linux Intel Gen12+
[^chrome-147]: What's New in WebGPU (Chrome 147–148) — Linux NVIDIA Wayland
[^chrome-146]: What's New in WebGPU (Chrome 146) — compatibility mode
[^chrome-news]: Chrome "What's New in WebGPU" index
[^chrome-cadence]: Chrome two-week release cadence (from Chrome 153, 2026-09-08)
[^ff-141]: Mozilla Gfx — Shipping WebGPU on Windows in Firefox 141
[^ff-145]: Firefox 145 release notes (WebGPU on macOS Tahoe, Apple Silicon)
[^ff-147]: Firefox 147 release notes (WebGPU on all supported macOS, Apple Silicon)
[^safari-26]: WebKit Features in Safari 26.0 (2025-09-15)
[^safari-27]: WebKit Features for Safari 27.0
[^dawn-support]: Dawn docs/support.md (backend requirements)
[^wgpu-readme]: wgpu README (backends)
[^status-agent]: Browser status research compiled 2026-10-16 (sources inline above)
