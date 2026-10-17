---
type: How-To
title: Headless Chromium + WebGPU testing with puppeteer-core (flags that worked on macOS arm64)
description: Measured results for Chrome for Testing 151, Chrome headless shell 151 and Edge 155 on an Apple M4 Pro, the exact flags that give a WebGPU adapter headless, reliable pixel-assertion methods, Linux CI recipes from vendor docs, puppeteer vs Playwright, and a reusable script.
tags: [webgpu, headless, puppeteer, playwright, chrome, edge, swiftshader, metal, flags, testing, macos, ci]
sources:
  - id: exp05-headless
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/test/headless.mjs
    title: exp-05 headless.mjs (puppeteer-core WebGPU + .NET smoke test)
  - id: exp05-matrix
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/test/matrix.mjs
    title: exp-05 matrix.mjs (browser x flag matrix runner)
  - id: exp05-main
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/src/main.ts
    title: exp-05 main.ts (WebGPU probe and .NET boot)
  - id: exp05-results
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/web-packaging/experiment-results-exp-05.md
    title: Experiment results exp-05
  - id: pptr-headless-modes
    resource: https://pptr.dev/guides/headless-modes
    title: Puppeteer - headless modes
  - id: pptr-launch-options
    resource: https://pptr.dev/api/puppeteer.launchoptions
    title: Puppeteer LaunchOptions (headless, executablePath, args)
  - id: playwright-browsers
    resource: https://playwright.dev/docs/browsers
    title: Playwright - browsers (headless shell, channels, cache location)
  - id: chrome-supercharge
    resource: https://developer.chrome.com/blog/supercharge-web-ai-testing
    title: Supercharge Web AI model testing - WebGPU, WebGL and Headless Chrome (updated 2024-01-16)
  - id: chrome-webgpu-troubleshooting
    resource: https://developer.chrome.com/docs/web-platform/webgpu/troubleshooting-tips
    title: WebGPU troubleshooting tips
  - id: chrome-webgpu-overview
    resource: https://developer.chrome.com/docs/web-platform/webgpu/overview
    title: WebGPU overview (platform support)
  - id: chromium-flag-config
    resource: https://chromium.googlesource.com/chromium/src/+/HEAD/third_party/blink/web_tests/FlagSpecificConfig
    title: Chromium blink web_tests FlagSpecificConfig (webgpu, webgpu-swiftshader presets)
  - id: chromedp-v0141
    resource: https://github.com/chromedp/chromedp/releases/tag/v0.14.1
    title: chromedp v0.14.1 release notes (SwiftShader fallback opt-in from Chromium 139)
  - id: agent-browser-webgpu
    resource: https://agent-browser.dev/webgpu
    title: agent-browser WebGPU docs (third-party presets per OS)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T17:25:00Z }
status: draft
---

# Headless Chromium + WebGPU testing

Machine: Apple M4 Pro, macOS 27.0.1 (arm64), Node 24.18.0. **Google Chrome is not installed.** Browsers actually used (all pre-existing, nothing downloaded):

| Key | Binary | Version |
|---|---|---|
| `cft` | Playwright cache `~/Library/Caches/ms-playwright/chromium-1234/.../Google Chrome for Testing` | 151.0.7922.34 |
| `shell` | Playwright cache `.../chromium_headless_shell-1234/.../chrome-headless-shell` | 151.0.7922.34 |
| `edge` | `/Applications/Microsoft Edge.app` | 155.0.4283.45 |

(`Helium` 0.14.9.1, Chromium 150.0.7871.186, is also installed but was not tested.)[^exp05-results]

## Result in one paragraph

On macOS arm64 **new headless** (`puppeteer` `headless: true`, which is `--headless=new`) in Chrome for Testing 151 and Edge 155 exposes a **real Metal WebGPU adapter with no extra flags** (`vendor: apple`, `architecture: metal-3`, not a fallback adapter). Buffer readback, screenshot of the WebGPU canvas, and `toDataURL` all returned the expected colour. `--enable-unsafe-webgpu` is unnecessary there but harmless. The old-style **`chrome-headless-shell` needs flags**: with none there is no adapter; with only `--enable-unsafe-webgpu` it gets a SwiftShader *fallback* adapter whose render output is broken (readback zeros, screenshot white); `--enable-unsafe-webgpu --use-angle=metal` gives the real GPU and `--enable-unsafe-webgpu --use-angle=swiftshader` gives a fully working software path.[^exp05-results][^exp05-headless]

## Measured matrix (page: clear a 256x256 canvas to RGB 255,128,0 and read pixels; plus .NET `Tick` == 42)

`pixelOk` = GPU buffer readback (`copyTextureToBuffer` + `mapAsync`) matched orange. `shot` = centre pixel of a puppeteer element screenshot of the canvas. `dataURL` / `drawImage` = canvas readbacks (informational). Every row also booted .NET and returned 42 (`dotnetTick`).[^exp05-results]

| Browser | Flags (besides defaults) | Adapter | Fallback? | `pixelOk` | `shot` | Verdict |
|---|---|---|---|---|---|---|
| cft 151 | none | apple / metal-3 | no | yes | orange | PASS |
| cft 151 | `--enable-unsafe-webgpu` | apple / metal-3 | no | yes | orange | PASS |
| cft 151 | `--enable-unsafe-webgpu --use-angle=metal` | apple / metal-3 | no | yes | orange | PASS |
| cft 151 | `--enable-unsafe-webgpu --use-webgpu-adapter=swiftshader` | google / swiftshader | yes | yes | orange | PASS (software) |
| cft 151 | `--enable-unsafe-webgpu --use-angle=swiftshader --use-webgpu-adapter=swiftshader` | google / swiftshader | yes | yes | orange | PASS (software) |
| cft 151 | `--enable-unsafe-webgpu --enable-webgpu-developer-features` | apple / metal-3, description `Apple M4 Pro` | no | yes | orange | PASS |
| cft 151 | `--disable-gpu` | none (null adapter) | - | - | - | no WebGPU |
| cft 151 | `--disable-gpu --enable-unsafe-swiftshader --enable-unsafe-webgpu` | google / swiftshader | yes | **no (zeros)** | white | broken |
| edge 155 | none / `--enable-unsafe-webgpu` / `+--use-angle=metal` | apple / metal-3 | no | yes | orange | PASS |
| edge 155 | `--enable-unsafe-webgpu --use-webgpu-adapter=swiftshader` | google / swiftshader | yes | yes in 5 of 7 runs; `mapAsync` `AbortError` (device lost) in 2 of 7 | **white** every time | flaky; avoid |
| edge 155 | `--enable-unsafe-webgpu --use-angle=swiftshader --use-webgpu-adapter=swiftshader` | google / swiftshader | yes | yes | orange | PASS (software) |
| shell 151 | none | **null** | - | - | - | no WebGPU |
| shell 151 | `--enable-unsafe-webgpu` (also `+--enable-features=Vulkan`, `+--use-webgpu-adapter=swiftshader`, `+--enable-webgpu-developer-features`) | google / swiftshader | yes | **no (zeros)** | white | broken |
| shell 151 | `--enable-unsafe-webgpu --use-angle=metal` | apple / metal-3 | no | yes | orange | PASS |
| shell 151 | `--enable-unsafe-webgpu --use-angle=swiftshader` | google / swiftshader | yes | yes | orange | PASS (software) |
| cft 151, shell 151, edge 155 | `--enable-unsafe-webgpu --use-angle=swiftshader` (with or without `--enable-unsafe-swiftshader` / `--use-webgpu-adapter=swiftshader`) | google / swiftshader | yes | yes | orange | PASS (software, all three browsers) |

Playwright (`playwright-core` 1.64.1, `chromium.launch({ executablePath, headless: true, args })`) gave the same adapter outcomes for cft, shell and edge with `[]` and `['--enable-unsafe-webgpu']`.[^exp05-results]

Adapter info detail: by default `adapter.info` exposes only `vendor` and `architecture` (device and description were empty strings); adding `--enable-webgpu-developer-features` filled `description` with `Apple M4 Pro` (SwiftShader reported `SwiftShader Device (LLVM 10.0.0)`).[^exp05-results]

Interpretation (UNVERIFIED hypothesis): the SwiftShader WebGPU (software Vulkan) adapter cannot share its texture with a compositor that is not also software, so the headless shell's default compositor produces zeros/white; forcing `--use-angle=swiftshader` aligns both sides, and `--use-angle=metal` sidesteps software entirely. This is consistent with `edge ... --use-webgpu-adapter=swiftshader` (buffer mostly fine, screenshot white, occasional device loss) versus `cft` with the same flags (screenshot orange in the runs made, n=3).

## Reading pixels: what is reliable

1. **GPU buffer readback** of the canvas texture (configure with `usage: RENDER_ATTACHMENT | COPY_SRC`, `copyTextureToBuffer`, `mapAsync`) was deterministic and is the authoritative assertion. WebGPU has no `preserveDrawingBuffer`; the texture from `getCurrentTexture()` is valid for the current frame only, so copy it in the same command encoder that draws.[^exp05-main]
2. **Element screenshot** (`page.$('#gpu').screenshot()`), decoded in-page with `createImageBitmap`/`Image` + a 2D canvas, matched the clear colour on every real-GPU run and on the `--use-angle=swiftshader` runs. It is the right end-to-end check for "did the compositor show it".[^exp05-headless]
3. **`canvas.toDataURL()`** returned the colour in all passing hardware runs (same task and next frame; Chrome re-presents the last frame) but returned zeros on broken software paths.[^exp05-results]
4. **`ctx2d.drawImage(webgpuCanvas)`** was **flaky even on Metal**: `[0,0,0,0]` appeared in a few otherwise passing runs (for example cft `--enable-unsafe-webgpu`, edge with developer features) while other runs returned orange. Do not assert on it.[^exp05-results]
5. In earlier runs, doing `toDataURL`/`drawImage` *before* `mapAsync` on a SwiftShader adapter lost the device (`AbortError: ... A valid external Instance reference no longer exists`) and masked the buffer result; isolate canvas readbacks and run the buffer readback first.[^exp05-results]

`app/test/matrix.mjs` runs `headless.mjs` once per browser and preset and prints one PASS/FAIL line each.[^exp05-matrix]

## Recommended launch configuration

macOS (this machine), no GPU flags needed:

```js
import puppeteer from 'puppeteer-core';
const browser = await puppeteer.launch({
  executablePath: '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge', // or Chrome for Testing / Google Chrome
  headless: true,                               // new headless (= --headless=new)
  args: ['--enable-unsafe-webgpu'],             // harmless; keeps Linux/Windows parity
  protocolTimeout: 120000,
});
```

`headless` is `boolean | 'shell'` (default `true`, the new mode), `executablePath` replaces the bundled browser (Puppeteer only guarantees the bundled one), and `args` are extra command-line flags.[^pptr-launch-options]

- If you must use `chrome-headless-shell` (`headless: 'shell'`): add `--use-angle=metal` (hardware) or `--use-angle=swiftshader` (software); never rely on the default fallback adapter.[^pptr-headless-modes][^exp05-results]
- Add `--enable-webgpu-developer-features` when you want the full adapter description or Dawn "unsafe" APIs, and `--enable-dawn-backend-validation` for backend validation; both left the cft and edge runs green.[^chromium-flag-config][^exp05-results]
- `--disable-gpu` removes the adapter on new headless; do not pass it.

Why new headless: Puppeteer's `headless: true` is the new mode (real Chrome); `'shell'` launches the separate `chrome-headless-shell` that "does not match the behavior of the regular Chrome completely". Playwright runs the headless shell by default and uses new headless with `channel: 'chromium'` or branded channels, where behaviour "differs from chromium headless shell".[^pptr-headless-modes][^playwright-browsers]

## Linux / CI recipes (from documentation; NOT tested on this Mac)

- Google's headless guide (Linux with an NVIDIA GPU): `headless: 'new'` plus `--no-sandbox --headless=new --use-angle=vulkan --enable-features=Vulkan --disable-vulkan-surface --enable-unsafe-webgpu`; verify via `chrome://gpu` (`WebGPU: Hardware accelerated`); without correct drivers WebGPU stays "Software only".[^chrome-supercharge][^chrome-webgpu-troubleshooting]
- Software-only Linux preset documented by a third-party tool: `--enable-unsafe-webgpu --enable-features=Vulkan --use-angle=vulkan --use-vulkan=swiftshader --use-webgpu-adapter=swiftshader --disable-vulkan-surface`, needs `libvulkan1 mesa-vulkan-drivers`; the same source says headless screenshots of WebGPU canvases are unsupported on Linux and blank on Windows, so use a headed browser under Xvfb there. UNVERIFIED by us.[^agent-browser-webgpu]
- Chromium's own web tests use the `webgpu-swiftshader` preset: `--enable-unsafe-webgpu --use-webgpu-adapter=swiftshader --enable-dawn-features=allow_unsafe_apis --disable-dawn-features=use_dxc --enable-webgpu-developer-features --use-gpu-in-tests --enable-accelerated-2d-canvas`.[^chromium-flag-config]
- From Chromium 139, falling back to SwiftShader for GL is opt-in (`--enable-unsafe-swiftshader`), per chromedp's release note.[^chromedp-v0141]
- Platform support: WebGPU shipped in Chrome 113 on macOS, Windows (D3D12) and ChromeOS; Android in 121; Linux was "coming soon" on that page. `navigator.gpu` needs a secure context (HTTPS or localhost).[^chrome-webgpu-overview][^chrome-webgpu-troubleshooting]

## The script (`app/test/headless.mjs`)

Behaviour: launches the chosen browser, opens the URL (or starts Vite itself with `--serve`), logs console, page errors, failed requests and HTTP >= 400 (favicon excluded), waits for `window.__e2e.done`, takes an element screenshot of `#gpu`, decodes its centre pixel, prints either a JSON report or a `SUMMARY` line, and exits non-zero if: `navigator.gpu` is missing; `requestAdapter()` is null (unless `--allow-no-gpu`); GPU readback is wrong; `--expect-dotnet` and `Tick` did not return 42; or the page recorded errors.[^exp05-headless]

```text
node test/headless.mjs --serve --browser edge --preset none --expect-dotnet --summary
node test/headless.mjs --url http://localhost:5173/ --browser cft --flags="--enable-unsafe-webgpu --use-angle=metal" --net --reload
npm run test:matrix       # all browsers x presets in ~15 s (matrix.mjs)
```

Page side (`src/main.ts`) contract: set `window.__e2e = { done, page, webgpu, dotnet, errors }`; `webgpu` holds `hasNavigatorGpu`, `adapter`, `info`, `isFallbackAdapter`, `pixel`, `pixelOk`; `dotnet` holds `createMs`, `tickReturn`, `hostLog`, `buildInfo`. Same page works for dev and preview URLs.[^exp05-main]

## Timeouts and .NET start-up

- `dotnet.create()` took about 100 to 150 ms in the fixture on localhost (Debug build, 180 files), which is not representative of a BRUTAL-sized app. Use `page.waitForFunction('window.__e2e.done', { timeout })` with a generous timeout (the script defaults to 60 s) and a `protocolTimeout` above it.
- Run `await new Promise(r => requestAnimationFrame(r))` in-page before capturing a screenshot so the first frame has been presented (the page awaits a frame inside the probe).[^exp05-main]
- `puppeteer-core` 25.14.0 is 8 MB and downloads nothing; `playwright-core` 1.64.1 is 13 MB; neither fetched a browser. npm 12 blocks install scripts by default, so the full `puppeteer` package would need script approval to download a browser.[^exp05-results]

## Open questions

- UNVERIFIED: stable Google Chrome 14x to 15x on macOS (not installed); Chrome for Testing 151 and Edge 155 are the nearest evidence.
- UNVERIFIED: the root cause of the SwiftShader zeros/white behaviour (hypothesis above); a `chrome://gpu` dump from each configuration would settle it.
- UNVERIFIED: Linux and Windows flag sets in this document were not run.
- UNVERIFIED: Helium (Chromium 150) behaviour.
- UNVERIFIED: WebGPU pixel tests under a real BRUTAL/Vulkan-to-WebGPU translation layer (a clear-colour pass is not a stress test; see [/webgpu/webgpu-limits-features-and-compat-mode.md](/webgpu/webgpu-limits-features-and-compat-mode.md) for limits worth asserting).

[^exp05-headless]: exp-05 headless.mjs (puppeteer-core WebGPU + .NET smoke test)
[^exp05-main]: exp-05 main.ts (WebGPU probe and .NET boot)
[^exp05-results]: Experiment results exp-05
[^pptr-headless-modes]: Puppeteer - headless modes
[^pptr-launch-options]: Puppeteer LaunchOptions (headless, executablePath, args)
[^playwright-browsers]: Playwright - browsers (headless shell, channels, cache location)
[^chrome-supercharge]: Supercharge Web AI model testing - WebGPU, WebGL and Headless Chrome (updated 2024-01-16)
[^chrome-webgpu-troubleshooting]: WebGPU troubleshooting tips
[^chrome-webgpu-overview]: WebGPU overview (platform support)
[^chromium-flag-config]: Chromium blink web_tests FlagSpecificConfig (webgpu, webgpu-swiftshader presets)
[^chromedp-v0141]: chromedp v0.14.1 release notes (SwiftShader fallback opt-in from Chromium 139)
[^agent-browser-webgpu]: agent-browser WebGPU docs (third-party presets per OS)
[^exp05-matrix]: exp-05 matrix.mjs (browser x flag matrix runner)
