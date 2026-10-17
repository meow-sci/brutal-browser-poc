# Brutal.Browser

Browser host for BRUTAL games on .NET 10 `browser-wasm`: canvas, frame loop, input queue, time, Web Audio and logging.
Contract: `docs/architecture.md` sections 3-5, 8 and 12. No Vulkan or GLFW dependency (key and button numbers are GLFW's integers;
`Brutal.Glfw.Browser` maps them to the `Brutal.GlfwApi` enums).

## Parts

| Type | Purpose |
| --- | --- |
| `BrowserHost` | `Initialize()` (idempotent: canvas size, focus, BRUTAL log provider), `RunLoop(Func<double,bool>)`, `[JSExport] Tick(timestampMs)`, `Now`, `IsVisible`, `FrameCount`, `Exit()`, `SetTitle`, page commands: `RegisterCommand(name, Action)` / `UnregisterCommand(name)` / `[JSExport] Dispatch(name)` |
| `BrowserCanvas` | `CssSize`, `PixelSize`, `DevicePixelRatio`, `SurfaceHandle` (= 1), `Resized`, pointer lock, cursor, fullscreen |
| `BrowserInput` | `PollEvents()` drains the JS queue (one interop call per 256 records) into typed events + polled `GetKey/GetMouseButton` state |
| `BrowserSurfaces` | `CreateSurface` hook set by the web ICD; used by `Glfw.CreateWindowSurface` |
| `BrowserClipboard` | synchronous clipboard cache (refreshed by paste gestures) |
| `Audio.AudioEngine` / `Sound` / `Voice` | Web Audio; `decodeAudioData` first, NVorbis fallback (`OggVorbisDecoder`) |
| `Logging.BrowserLogging.Install()` | plugs `BrowserConsoleLoggerProvider` into BRUTAL's `LogSystem` (its `AddConsole` path throws on browser) |

## Boot (what the page does)

```js
import { dotnet } from './_framework/dotnet.js';
import * as host from './brutal-browser.js';
import * as audio from './brutal-browser-audio.js';
let runtime;
const getHeapU8 = () => runtime.localHeapViewU8();   // fresh view on every access
host.init({ canvas, getHeapU8 }); audio.init({ getHeapU8 });
runtime = await dotnet.create();
runtime.setModuleImports('brutal-browser', host.imports);
runtime.setModuleImports('brutal-browser-audio', audio.imports);
const tick = (await runtime.getAssemblyExports('Brutal.Browser.dll')).Brutal.Browser.BrowserHost.Tick;
await runtime.runMain();                              // C# Main: BrowserHost.Initialize(); ...; BrowserHost.RunLoop(frame)
const raf = (t) => { if (tick(t)) requestAnimationFrame(raf); };
requestAnimationFrame(raf);
// start overlay click handler: audio.imports.resume();          // AudioContext needs a user gesture: call it synchronously, no await before it
// page button click handler:   audio.imports.resume(); exports.Brutal.Browser.BrowserHost.Dispatch('play-sound');   // see Page commands
```

`tests/Brutal.Browser.Smoke/wwwroot/main.js` is a complete example. The JS files ship as static web assets published at the root of
the consuming app's `wwwroot` (the project uses the Razor SDK only for that; it is an ordinary class library otherwise).
The canvas must be sized by CSS (`width/height: 100%`): the module writes `canvas.width/height` (device pixels) from `PollEvents`.

## Frame protocol

`Tick(timestampMs)` computes `dt` (0 on the first frame, clamped to 0.1 s), increments `FrameCount`, calls the frame delegate and returns
whether to continue. An exception prints `ERROR <message>` (stdout) and the stack (stderr), stops the loop and returns `false`.
The frame should call `BrowserInput.PollEvents()` (or `Glfw.PollEvents()`) once.

## Page commands

The page controls the game through named commands: the game calls `BrowserHost.RegisterCommand("play-sound", app.PlaySound)` (a second registration of the same name replaces the first, `UnregisterCommand` removes it) and the page calls `exports.Brutal.Browser.BrowserHost.Dispatch('play-sound')` from a DOM event handler, so the handler runs inside the user gesture. Unknown names print `WARN unknown command <name>`; an exception in a handler prints `ERROR <message>` (stack on stderr) and does not stop the frame loop. Handlers take no arguments. `docs/architecture.md` section 5.

## Input

JS captures `keydown/keyup` (`KeyboardEvent.code` -> GLFW key + scancode via the table in `brutal-browser.js`, `preventDefault` for
Space/arrows/Tab/... and for everything while pointer-locked except F5/F11/F12 and Ctrl/Cmd+C/X/V), characters, pointer events on the canvas
(button map 0/1/2 -> left/middle/right = GLFW 0/2/1), wheel (`{ passive: false }`; pixel/100, line/3, page*80, Y negated: one notch = 1),
focus/blur (blur releases all keys and buttons), visibility, pointer lock and `ResizeObserver` (+ DPR media query). Consecutive cursor and
scroll records are coalesced. Pointer lock (`RequestPointerLock`) is retried on the next click/keydown if it needs a user gesture;
Escape releases it and the request is forgotten. While locked the cursor position is a virtual position accumulating `movementX/Y`.

## Audio

`AudioEngine.Create()` returns the process-wide engine. The `AudioContext` is created lazily (first `DecodeAsync`/`Play`/`resume()` or first user gesture) and stays suspended until `resume()` runs inside a user gesture (`ResumeAsync()` or the page's own click handler, called synchronously; the module also resumes on every `pointerdown`/`keydown`/`touchend`); `DecodeAsync` works while suspended, and `Sound.Play` on a suspended context starts the voice anyway, which is heard once the context runs. All voices go through a master `GainNode` and a pass-through `AnalyserNode`; `window.__brutal.audio.getOutputLevel()` (JS `imports.getOutputLevel()`) is the RMS 0..1 of the last analyser frame, which the e2e tests use to prove sound reaches the output. The engine prints nothing: the
game prints the `AUDIO loaded` / `AUDIO play` / `AUDIO error <msg>` diagnostics (architecture section 12). `AudioEngine.PreferManagedDecoder`
forces the NVorbis path (used by the smoke test; about 115 ms for the 3.4 s PoC asset under the interpreter).

## Limits

One canvas, one `AudioContext`, no 3D audio yet, no gamepads, no touch gestures (pinch/rotate), no IME composition UI. Desktop builds compile and
the managed parts run (`OperatingSystem.IsBrowser()` guards every JS call; `RunLoop` is a blocking loop there).

## Build and test

```
dotnet build src/Brutal.Browser -c Release
dotnet test tests/Brutal.Browser.Tests                  # key table, NVorbis decode, input state machine, GlfwApi, page commands (no browser)
cd tests/Brutal.Browser.Smoke/test && npm install && node run.mjs   # Playwright end-to-end
```
