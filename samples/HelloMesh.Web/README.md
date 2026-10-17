# HelloMesh.Web

Vite 8 (vanilla TypeScript, no framework) host page for `samples/HelloMesh/HelloMesh.Browser`: it serves the .NET browser build's whole `wwwroot/`, creates the WebGPU device, boots the .NET runtime and drives the frame loop (`docs/architecture.md` sections 4, 5, 10, 12). Game and ICD logic stay in C#; this app only does what C# cannot.

| Path | What |
| --- | --- |
| `index.html` | `<canvas id="canvas">` (fills the window), bottom-right bar with `<button id="play-sound">` (disabled until boot finishes), start overlay `<button id="start">`, `#status`, `#error` overlay |
| `src/main.ts` | boot order of architecture.md section 5 with graceful failures (about 150 lines, strict TS) |
| `src/types/dotnet.d.ts` | copy of the runtime pack's `dotnet.d.ts` (`~/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/`); there is no npm package for it |
| `plugins/dotnet-wwwroot.ts` | Vite plugin: serves/copies the .NET app's `wwwroot` unbundled (dev, preview, build) |
| `scripts/build-dotnet.mjs` | `npm run build:dotnet` = `dotnet publish` of the browser project |
| `test/e2e.mjs` | Playwright end-to-end test (plain script, exit code 0/1/2) |
| `test/serve-check.mjs` | browser-free check that every file the runtime and `main.ts` request is served with a sane MIME type |

## Run

```sh
npm install
npm run build:dotnet      # dotnet publish -c Release ../HelloMesh/HelloMesh.Browser/HelloMesh.Browser.csproj
npm run dev               # http://localhost:5173, full reload when the .NET output changes
npm run build             # tsc + vite build -> dist/ (Vite output + the published .NET wwwroot incl. .br/.gz)
npm run preview           # serves dist/ with .br/.gz negotiation (http://localhost:4173)
npm run typecheck
npm run test:e2e          # headless Chromium (see below); npm run test:e2e:chrome uses Google Chrome
```

For a faster dev loop use a Debug build: `dotnet build -c Debug` in the browser project and `DOTNET_CONFIGURATION=Debug npm run dev` (about 1 s incremental; the dev server reloads the page when the build finishes). Edits to a `wwwroot/*.js` file of any referenced project reload the page without a `dotnet build`.

Environment (read by the plugin, `build:dotnet` and the tests):

| Variable | Default | Meaning |
| --- | --- | --- |
| `DOTNET_APP_DIR` | `../HelloMesh/HelloMesh.Browser` | .NET project directory, relative to this folder |
| `DOTNET_CONFIGURATION` | `Release` | `Debug` or `Release` (picks `bin/<cfg>/...`) |
| `DOTNET_PROJECT` | `<appDir>/<dir name>.csproj` | only for `build:dotnet` |

Vite's `base` is supported (`vite build --base /games/hellomesh/`); `main.ts` loads everything through `import.meta.env.BASE_URL` (use an absolute path as base).

## How the plugin finds the .NET output

`plugins/dotnet-wwwroot.ts` looks in `<appDir>/bin/<cfg>/net10.0/` (or its `browser-wasm/` subfolder) and re-checks at most once a second, so the dev server may start before the first build and picks the output up when it appears:

1. **Publish output** `publish/wwwroot`: complete (framework, the libraries' root-level JS, `.br`/`.gz`); `npm run build:dotnet` makes it. **`vite build` copies this folder** into `dist/` byte for byte (the runtime verifies integrity hashes), falling back to 2 when it does not exist; no .NET output at all is a build error.
2. **Build output** `wwwroot/` (`dotnet build` materialises only `_framework/` there) plus the static web assets manifest `<App>.staticwebassets.runtime.json`, which maps every other route (`StaticWebAssetBasePath=/` library files such as `brutal-browser.js`, `_content/*`) to its source file. So `npm run dev` works against a plain `dotnet build`, and an edit of a `wwwroot/*.js` file in any referenced project is served at once.

In dev the **newest output wins**: a fresh `dotnet publish` is served first (the same trimmed/relinked files that go into `dist/`); a later `dotnet build`, or an edit of a source `wwwroot` file, makes 2 take over until the next publish.

Behaviour:

- Dev: a middleware before Vite's own serves the routes with the right MIME types (`.wasm` application/wasm, `.js` text/javascript, `.json`, `.dat`/`.pdb` octet-stream, ...), `ETag` + `no-cache`. A missing `_framework/*` or `_content/*` file is a loud 404 (not Vite's SPA fallback, which would answer `200 text/html`). The .NET `index.html` is never served or copied: this app owns it.
- **Fingerprints**: a request for a plain name (`_framework/dotnet.js`, `webgpu-glue.js`) falls back to a fingerprinted file (`dotnet.ab12cd34ef.js`; the newest one, since `dotnet publish` never deletes old ones), and `vite build` writes plain-named copies of such JS entry points.
- Preview: `.br`/`.gz` negotiation (`Content-Encoding`, `Vary`) over `dist/`, which plain `vite preview` lacks.
- Debounced (300 ms) `full-reload` when files under the served folders change.

## Boot (src/main.ts)

1. `navigator.gpu`, `requestAdapter()`, `requestDevice()` (all adapter features and limits), canvas context `configure({ format: getPreferredCanvasFormat(), alphaMode: 'opaque', usage: RENDER_ATTACHMENT | COPY_SRC })`.
2. Dynamic `import(base + 'webgpu-glue.js' | 'brutal-browser.js' | 'brutal-browser-audio.js' | '_framework/dotnet.js')`, never bundled.
3. `dotnet.withDiagnosticTracing(false).create()`; `getHeapU8 = () => runtime.localHeapViewU8()`; each module's `init(...)`; `setModuleImports('webgpu' | 'brutal-browser' | 'brutal-browser-audio', module.imports)`; `runMain(mainAssemblyName, [])`.
4. `getAssemblyExports('Brutal.Browser.dll')`, then a `requestAnimationFrame` loop calling `Brutal.Browser.BrowserHost.Tick(ts)` until it returns `false`. The loop starts right after `runMain` (the overlay only gates audio); the start button calls `imports.resume()` of the audio module (synchronously, inside the user gesture) and hides the overlay without waiting for the AudioContext. The `Play sound` button (`#play-sound`, enabled at this point) does the same `resume()` first and then `exports.Brutal.Browser.BrowserHost.Dispatch('play-sound')`, the page → game command that `HelloMesh.Browser/Program.cs` registered with `BrowserHost.RegisterCommand` (architecture.md section 5); it moves keyboard focus back to the canvas so that Space keeps going to the game instead of re-clicking the button. To add a control: add an element, register a command in C#, call `resume()` (if it may start audio) and `Dispatch(name)` from its click handler.
5. Console output is forwarded unchanged; `ERROR ...` lines, uncaught errors, unhandled rejections, WebGPU uncaptured errors/device loss and boot failures are shown in `#error` (and boot failures also logged as `ERROR host boot failed: ...`). `window.__brutal` is provided by the JS modules, not by `main.ts` (`main.ts` only adds the test hooks `window.__brutalAudioState` when the audio module has `getState`, and `window.__brutalDispatch(name)`).

The `context.configure` of step 1 is a bootstrap: the ICD configures the canvas again in `vkCreateSwapchainKHR` with the swapchain's format, usage and sRGB view format.

## End-to-end test

```sh
npm run test:e2e                      # READY, AUDIO loaded, a sound started before the first click, FRAME 120, pixels, mouse -> CAMERA,
                                      # 200 px drag = 80 deg yaw, ArrowUp -> CAMERA with a smaller dist, Space and #play-sound -> AUDIO play
                                      # with output level > 0.01 within 500 ms, unknown command -> WARN, audio state (28 checks); prints the
                                      # measured levels and the host's PERF line (informational)
npm run test:e2e:chrome               # Google Chrome (executablePath) instead of Playwright's Chromium
node test/e2e.mjs --strict-autoplay   # + --autoplay-policy=document-user-activation-required: the AudioContext must be suspended until the first
                                      # click, stay suspended for a click without user activation, and play the deferred voice after the first click (30 checks)
node test/e2e.mjs --preview           # test dist/ through `vite preview` (run `npm run build` first)
node test/e2e.mjs --url http://localhost:5173/   # against a running server (`npm run dev`)
node test/e2e.mjs --browser-path <exe> --headed --timeout 120000 --flags "--enable-unsafe-webgpu" --out test/out
node test/serve-check.mjs [--preview]
```

The script starts the Vite dev server itself (`createServer`), launches Chromium (`channel: 'chromium'` = full Chromium in new headless mode, which has a real Metal/Vulkan/D3D12 WebGPU adapter; `chrome-headless-shell`, what `headless: true` alone would use, has no WebGPU adapter), captures the console, and writes `test/out/frame.png` and `test/out/console.log`. Pixels: a screenshot decoded with `pngjs` (canvas centre must differ from the corner clear colour) and, when present, `window.__brutal.readPixel(x, y)` (GPU readback) plus `window.__brutal.frameCount/lastError`. Audio: `window.__brutalAudioState()` or `window.__brutal.audio.getState()`, skipped when neither exists, and a real output check: `window.__brutal.audio.getOutputLevel()` (RMS of the master mix through an `AnalyserNode`) is polled for a value above 0.01 within 500 ms of `AUDIO play`, after Space (with the canvas blurred, to prove the key does not need focus) and after a click on `#play-sound`; it reads 0 for a suspended context, a source that never started, a muted master or a missing resume (the checks were verified to fail for each of those). The pre-gesture phase evaluates through CDP with `userGesture: false`, because `page.evaluate()` and locator calls count as user gestures and would unlock the AudioContext. `--strict-autoplay` pins `--autoplay-policy=document-user-activation-required`, Chrome's desktop default; do not use `user-gesture-required`, which for Web Audio only restricts cross-origin iframes (the context of a top-level page starts `running`). The level cannot see the OS mixer, a muted tab or a muted device. It fails on any `ERROR` console line, page error, failed request, HTTP >= 400 or visible `#error`. Exit codes: `0` all checks passed, `1` a check failed, `2` the browser could not be launched. `navigator.gpu` needs a secure context, so the page is always loaded from `http://localhost:<port>/`.

## What HelloMesh.Browser must provide

- `Microsoft.NET.Sdk.WebAssembly`, `WasmEnableHotReload=false`, `InvariantGlobalization=true`; `Main` returns promptly; `[JSExport] Brutal.Browser.BrowserHost.Tick(double) : bool` in `Brutal.Browser.dll`.
- At the root of its published `wwwroot/` (libraries with `StaticWebAssetBasePath=/`): `webgpu-glue.js`, `brutal-browser.js`, `brutal-browser-audio.js`, each with `export function init(options)` and `export const imports = {...}`; `imports.resume()` and `imports.getState()` on the audio module.
- `[JSExport] Brutal.Browser.BrowserHost.Dispatch(string) : void` (same assembly as `Tick`) and a registered `"play-sound"` command (`BrowserHost.RegisterCommand`), which the `Play sound` button needs; an unregistered command only prints `WARN unknown command play-sound`.
- Console markers from architecture.md section 12 (`READY`, `FRAME n` every 60 frames, `CAMERA ...`, `AUDIO play`, `ERROR ...`) and `window.__brutal.readPixel/frameCount/lastError` from `webgpu-glue.js`.
- No dependency on its own `index.html` / `main.js`: this app's `index.html` and `src/main.ts` replace the template's (they are never served or copied).

## Troubleshooting

- **"WebGPU is not available" / "No WebGPU adapter"** (shown in `#error`): use Chrome/Edge 113+ over `https://` or `http://localhost`. Headless: use the new headless mode (`channel: 'chromium'` or a full Chrome binary); `chrome-headless-shell` has no adapter without `--use-angle=metal|swiftshader` flags. `--disable-gpu` removes the adapter. Software adapters (SwiftShader) may render blank screenshots.
- **`Failed to import library initializer` / 404 for `_content/Microsoft.DotNet.HotReload...`**: set `<WasmEnableHotReload>false</WasmEnableHotReload>` in the browser project (it defaults to true for Debug). The plugin serves `_content/*` through the manifest, but the Hot Reload initializer is useless behind Vite.
- **`NO .NET OUTPUT` / 404 for `_framework/dotnet.js`**: build first (`npm run build:dotnet` or `dotnet build -c <cfg>`) and check `DOTNET_APP_DIR` / `DOTNET_CONFIGURATION`; the plugin logs the folders it looked at. The dev server recovers on its own once the output appears.
- **MIME type errors** (`wasm` "falling back to slower ArrayBuffer instantiation", or JS parsed as HTML): a request fell through to Vite's SPA fallback (`200 text/html`). Under `_framework/` and `_content/` the plugin answers 404 instead; for another route add its extension to `MIME` in the plugin.
- **Old code runs after `dotnet publish`**: publish never deletes old fingerprinted files. The plugin prefers the newest by mtime, but `dist/` still copies stale files; delete `bin/<cfg>/net10.0/publish` (or `bin obj`) before publishing.
- **Playwright cannot find a browser**: the installed Chromium revision must match `playwright-core` (1.64.1 needs build 1248); fall back to `--chrome` or `--browser-path <exe>`. Closing Chromium can take seconds on a loaded machine; the script prints its verdict first and gives teardown 10 s.
- **`Content-Encoding` errors in preview/hosting**: serve `.br`/`.gz` with the matching header, as the plugin does; the `.wasm` files must not be re-compressed or rewritten (integrity hashes).
