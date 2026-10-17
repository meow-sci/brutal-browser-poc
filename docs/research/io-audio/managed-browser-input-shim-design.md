---
type: Design Note
title: Managed browser windowing and input shim (strategy B) - DOM events to GLFW semantics
description: Design for a small C# + JavaScript layer that gives BRUTAL games GLFW-equivalent window, DPI, keyboard, mouse, wheel, pointer-lock, gamepad, clipboard and time behaviour in the browser, including mapping tables, the per-frame event queue, preventDefault policy and loop interplay.
tags: [input, keyboard, mouse, pointer-lock, gamepad, canvas, dpi, resize-observer, jsimport, glfw-semantics, strategy-b]
sources:
  - id: mdn-keyboard-api
    resource: https://developer.mozilla.org/en-US/docs/Web/API/Keyboard_API
    title: MDN Keyboard API (Keyboard Lock, KeyboardEvent.code vs key)
  - id: mdn-pointerlock-request
    resource: https://developer.mozilla.org/en-US/docs/Web/API/Element/requestPointerLock
    title: MDN Element.requestPointerLock()
  - id: mdn-pointerlock-api
    resource: https://developer.mozilla.org/en-US/docs/Web/API/Pointer_Lock_API
    title: MDN Pointer Lock API
  - id: caniuse-pointerlock
    resource: https://caniuse.com/pointerlock
    title: Can I use - Pointer Lock API
  - id: mdn-raf
    resource: https://developer.mozilla.org/en-US/docs/Web/API/Window/requestAnimationFrame
    title: MDN Window.requestAnimationFrame()
  - id: mdn-settimeout
    resource: https://developer.mozilla.org/en-US/docs/Web/API/Window/setTimeout
    title: MDN Window.setTimeout() (nesting clamp, background throttling)
  - id: mdn-resize-dpcb
    resource: https://developer.mozilla.org/en-US/docs/Web/API/ResizeObserverEntry/devicePixelContentBoxSize
    title: MDN ResizeObserverEntry.devicePixelContentBoxSize
  - id: mdn-gamepad
    resource: https://developer.mozilla.org/en-US/docs/Web/API/Gamepad_API/Using_the_Gamepad_API
    title: MDN Using the Gamepad API
  - id: w3c-gamepad
    resource: https://w3c.github.io/gamepad/#remapping
    title: W3C Gamepad - Standard Gamepad mapping
  - id: mdn-wheel-deltamode
    resource: https://developer.mozilla.org/en-US/docs/Web/API/WheelEvent/deltaMode
    title: MDN WheelEvent.deltaMode
  - id: mdn-addeventlistener
    resource: https://developer.mozilla.org/en-US/docs/Web/API/EventTarget/addEventListener
    title: MDN EventTarget.addEventListener() (passive defaults)
  - id: html-activation
    resource: https://html.spec.whatwg.org/multipage/interaction.html#activation-triggering-input-event
    title: HTML Standard - activation triggering input event
  - id: ms-jsinterop
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/?view=aspnetcore-10.0
    title: JavaScript JSImport/JSExport interop in .NET WebAssembly (aspnetcore-10.0)
  - id: pongasoft-window
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/src/cpp/emscripten/glfw3/Window.cpp
    title: contrib.glfw3 Window.cpp (wheel normalisation, cursor modes, pointer lock)
  - id: pongasoft-keymap
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/src/cpp/emscripten/glfw3/KeyboardMapping.h
    title: contrib.glfw3 KeyboardMapping.h (KeyboardEvent.code to scancode to GLFW key)
  - id: pongasoft-usage
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/docs/Usage.md
    title: contrib.glfw3 docs/Usage.md (keyboard, Super key, gamepad, clipboard, fullscreen, Hi-DPI)
  - id: pongasoft-readme
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/README.md
    title: contrib.glfw3 README release notes
  - id: emsc-libglfw
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/main/src/lib/libglfw.js
    title: Emscripten libglfw.js (built-in GLFW)
  - id: brutal-glfw-window
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Manual/Libraries/Glfw.Window.cs
    title: Brutal.Glfw Glfw.Window.cs
  - id: brutal-glfw
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw
    title: Brutal.Glfw source (enums, constants)
  - id: brutal-imgui-glfw
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/PlatformBackends/GlfwBackend.cs
    title: Brutal.ImGui.Backends GlfwBackend.cs
  - id: ksa-program
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/Program.cs
    title: KSA KSA/Program.cs (reference only)
  - id: dotnet-scheduling
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/browser/runtime/scheduling.ts
    title: dotnet/runtime release/10.0 scheduling.ts
  - id: sib-jsinterop
    resource: /dotnet-wasm/js-interop-jsimport-jsexport.md
    title: JS interop on .NET 10 browser-wasm (sibling note)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:08:04Z }
status: draft
stale_after: 2027-01-31
---

# Goal and shape

Provide the subset of `Brutal.GlfwApi` behaviour a BRUTAL game and the BRUTAL ImGui platform backend need, on top of the DOM, with **no native code**. The same design works whether it surfaces as a drop-in `Brutal.GlfwApi` or as a smaller `Brutal.Browser` API (see [the recommendation](/io-audio/recommendation-io-audio.md)); the member inventory it must satisfy is in [the API surface note](/io-audio/brutal-glfw-and-openal-api-surface.md).

```
DOM events (window/canvas)  --JS module: normalise + enqueue-->  Float64Array ring
                                                                       |
 game frame:  Glfw.PollEvents()  --one JSImport call: Drain(Span<double>)--> C# decode
                                                                       |
                   updates cached key/button/cursor state, then raises GlfwWindow.OnKey/OnCursorPos/... delegates
```

Why a pull queue: GLFW's contract is that callbacks run inside `glfwPollEvents` on the main thread; BRUTAL additionally rethrows callback exceptions from `PollEvents`. A queue drained in `PollEvents` reproduces that, costs one interop call per frame instead of one per DOM event (mousemove can arrive at several hundred Hz), and never re-enters managed code from a DOM handler. Marshalling facts used here: JS arrays of numbers marshal to `double[]`/`int[]`/`byte[]` by copy; `Span<byte|int|double>` marshals to a zero-copy `MemoryView` valid only for the duration of the call; `Task`/`JSObject` proxies are expensive.[^ms-jsinterop] The doc contains a sentence that Span parameters cannot be imported, but the runtime's own unit tests declare `[JSImport] ... ([JSMarshalAs<JSType.MemoryView>] Span<byte> value)`, and the doc's .NET 10 table lists `Span<Byte/Int32/Double>` (not `Span<float>`, which only .NET 11 adds).[^sib-jsinterop] Test `[JSImport] Drain([JSMarshalAs<JSType.MemoryView>] Span<double>)` first (UNVERIFIED for `double`) and fall back to returning a `double[]` copy (a few KB per frame).

# Canvas, window size and DPI

GLFW semantics to reproduce: `Size` is in screen coordinates, `FramebufferSize` in pixels, `ContentScale` is the ratio; programmatic `Size`/`Position` setters only change the cached value when the matching callback fires.[^brutal-glfw-window]

| GLFW concept | Browser source |
| --- | --- |
| `Size` | CSS pixel size of the canvas content box (`ResizeObserverEntry.contentBoxSize`) |
| `FramebufferSize` | `devicePixelContentBoxSize` when available, else `round(cssSize * devicePixelRatio)`; written to `canvas.width/height` by the shim when it applies a resize[^mdn-resize-dpcb] |
| `ContentScale` | `window.devicePixelRatio` on both axes |
| `Position` | `canvas.getBoundingClientRect()` origin (or 0,0) |
| `OnSize`/`OnFramebufferSize`/`OnContentScale` | raised from `PollEvents` after a resize is applied |
| `SetSizeLimits`, `AspectRatio` | CSS `min/max-width/height` and `aspect-ratio` on the canvas container |
| `Visible`, `Opacity`, `Title` | `canvas.hidden`/CSS `visibility`, CSS `opacity`, `document.title` |
| fullscreen (`SetFullscreen`) | `canvas.requestFullscreen()`; only from a user activation; must follow `requestPointerLock` if both are wanted in one gesture[^mdn-pointerlock-request] |

Mechanics:

- `ResizeObserver` with `{ box: 'device-pixel-content-box' }`, wrapped in `try/catch` with a fallback to the default box, because MDN marks the property non-Baseline (Safari status not confirmed here).[^mdn-resize-dpcb] The callback only stores the pending size.
- A DPR change (browser zoom, moving between displays) leaves the CSS content box unchanged, so a plain `ResizeObserver` stays silent; observing `device-pixel-content-box` reports it where supported, and the portable route is to re-arm `matchMedia('(resolution: ' + dpr + 'dppx)')` after each change, as both Emscripten GLFW implementations do.[^emsc-libglfw][^pongasoft-usage] UNVERIFIED: exact firing behaviour per browser.
- Apply the pending size at the start of `PollEvents`, not inside the observer. contrib.glfw3 moved to exactly this ("resizing is now delayed until `glfwPollEvents()`") to remove flicker, because resizing a canvas clears it and the frame must be re-rendered at the new size.[^pongasoft-readme][^pongasoft-usage]
- Setting a size from C# (`GlfwWindow.Size = ...`) sets the container's CSS size and queues a size event for the next poll, so the cached property updates.
- The Vulkan layer's swapchain must treat a changed `FramebufferSize` as out-of-date (the reference game's `TryAcquireNextFrame` non-success -> rebuild path in `Loading.cs` already handles that).
- CSS: `canvas { display:block; width:100%; height:100%; touch-action:none; outline:none }` and `tabindex="0"` so the canvas can hold focus.

# Event record and queue

JS side keeps a preallocated `Float64Array(8 * capacity)` ring plus head/tail counters; `drain(buffer)` copies up to `buffer.length / 8` records and returns the count; overflow drops the oldest mouse-move records first (never key/button events).

| Type id | Event | Payload (a..g) |
| --- | --- | --- |
| 1 | Key | glfwKey, scancode, action (0/1/2), mods, - |
| 2 | Char | codepoint, mods |
| 3 | MouseButton | button, action, mods |
| 4 | CursorPos | x, y (CSS px, virtual when locked) |
| 5 | CursorEnter | entered |
| 6 | Scroll | dx, dy, flags |
| 7 | Focus | focused |
| 8 | Size | cssW, cssH, fbW, fbH, scale |
| 9 | PointerLockChanged | locked |
| 10 | Zoom / Rotate | scale or degrees |
| 11 | Visibility | visible |
| 12 | Joystick | jid, connected |
| 13 | Close | - |

C# decode loop (sketch):

```csharp
public static void PollEvents() {
    BrowserInterop.BeginPoll();                 // applies pending resize, polls gamepads
    int n;
    while ((n = BrowserInterop.Drain(_buf)) > 0)
        for (int i = 0; i < n; i++) Dispatch(_buf.AsSpan(i * 8, 8));
    ExceptionAggregator.CheckExceptions();       // same contract as Brutal.Glfw
}
```

`GetKey`, `GetMouseButton` and `CursorPos` read C#-side state updated by `Dispatch`, so polling and callbacks agree (GLFW updates state while processing events, not at DOM time). Press events for an already-down key become `Repeat` (use `event.repeat` as the primary source).

# Keyboard

- Listen on `window` in capture phase for `keydown`/`keyup`; ignore events whose `target` is an editable element (`input`, `textarea`, `[contenteditable]`) so HTML overlays work; give the canvas focus on `pointerdown`.
- Use `KeyboardEvent.code` (physical position, layout independent) for `GlfwKey` and `event.key` only for character events; MDN states `code` is the physical key and `key` follows locale, layout and modifiers.[^mdn-keyboard-api] The built-in Emscripten GLFW maps deprecated `keyCode` and therefore cannot tell left from right modifiers; contrib.glfw3 maps `code` through `KeyboardMapping.h`.[^emsc-libglfw][^pongasoft-keymap]
- `mods` come from the DOM event (`shiftKey`, `ctrlKey`, `altKey`, `metaKey`), not from tracking.
- Char events: `keydown` with `event.key` of length 1 (use `codePointAt(0)`), ignored when Ctrl/Meta are held, plus `compositionend` for IME. A hidden `<textarea>` focused while the game wants text input (ImGui `WantTextInput`) is needed for IME and mobile soft keyboards (design recommendation, not sourced).
- Browser Meta/Super quirk: with Cmd/Win held, other keys never get `keyup`; contrib.glfw3 releases all keys when Super is released and uses timeouts for the held case.[^pongasoft-usage][^pongasoft-readme] Reproduce at least "release all on Meta release".

Mapping from `KeyboardEvent.code` to `GlfwKey` (values equal GLFW constants in BRUTAL's `Glfw.Constants.gen.cs`):[^brutal-glfw][^pongasoft-keymap]

| `code` | GlfwKey (value) |
| --- | --- |
| `Space` | Space (32) |
| `Quote` | Apostrophe (39) |
| `Comma`, `Minus`, `Period`, `Slash` | Comma 44, Minus 45, Period 46, Slash 47 |
| `Digit0`..`Digit9` | Number0..Number9 (48-57) |
| `Semicolon`, `Equal` | Semicolon 59, Equal 61 |
| `KeyA`..`KeyZ` | A..Z (65-90) |
| `BracketLeft`, `Backslash`, `BracketRight`, `Backquote` | LeftBracket 91, Backslash 92, RightBracket 93, GraveAccent 96 |
| `IntlBackslash`, `IntlRo`/`IntlYen` | World1 161, World2 162 (approximation, UNVERIFIED) |
| `Escape`, `Enter`, `Tab`, `Backspace` | 256, 257, 258, 259 |
| `Insert`, `Delete` | 260, 261 |
| `ArrowRight`, `ArrowLeft`, `ArrowDown`, `ArrowUp` | 262, 263, 264, 265 |
| `PageUp`, `PageDown`, `Home`, `End` | 266, 267, 268, 269 |
| `CapsLock`, `ScrollLock`, `NumLock`, `PrintScreen`, `Pause` | 280, 281, 282, 283, 284 |
| `F1`..`F25` | 290-314 |
| `Numpad0`..`Numpad9` | Kp0..Kp9 (320-329) |
| `NumpadDecimal`, `NumpadDivide`, `NumpadMultiply`, `NumpadSubtract`, `NumpadAdd`, `NumpadEnter`, `NumpadEqual` | 330, 331, 332, 333, 334, 335, 336 |
| `ShiftLeft`, `ControlLeft`, `AltLeft`, `MetaLeft` (legacy `OSLeft`) | 340, 341, 342, 343 |
| `ShiftRight`, `ControlRight`, `AltRight`, `MetaRight` (legacy `OSRight`) | 344, 345, 346, 347 |
| `ContextMenu` | Menu (348) |
| anything else | Unknown (-1) |

`scancode` (second `OnKey` argument): contrib.glfw3 derives it from `event.code` through PC/AT set-1 style `DOM_PK_*` values (for example `KeyA` = 0x1E, `ArrowUp` = 0xE048) and then maps the scancode to the GLFW key;[^pongasoft-keymap] reuse those values so scancodes agree with other Emscripten GLFW users. BRUTAL only uses the scancode for `GetKeyName`. `GetKeyName` can use `navigator.keyboard.getLayoutMap()` where available (Chromium only, UNVERIFIED here) and a US-layout name table otherwise.

## preventDefault policy (design recommendation)

When the canvas has focus (or the pointer is locked) call `preventDefault()` on `keydown`/`keyup` for: Space, arrows, PageUp/Down, Home/End (page scroll), Tab (focus traversal), Backspace, F1-F12 except where the game wants them, `/` and `'` (Firefox quick-find), Alt alone (menu bar on Windows/Linux), and Ctrl/Cmd+F/S/P/G/D/A. Let Ctrl/Cmd+C/X/V through (clipboard events, see below) and F5/F11/F12 unless the game binds them. contrib.glfw3 takes the opposite default (prevent everything except cut/copy/paste, with an opt-in "bubble" callback for keys like F12).[^pongasoft-usage] Browser/OS-reserved shortcuts (Ctrl+W/T/N, Cmd+Q, Alt+Tab) cannot be intercepted; Escape also exits pointer lock and fullscreen. The experimental Keyboard Lock API (`navigator.keyboard.lock([...codes])`, intended for games and remote-access apps) can capture some reserved keys, but MDN labels it experimental and its support list was not confirmed here.[^mdn-keyboard-api]

# Mouse, wheel and pointer lock

- Use Pointer Events on the canvas (`pointerdown/up/move/enter/leave/cancel`) with `setPointerCapture` on press so releases outside the canvas still arrive; `contextmenu` and `auxclick` default-prevented; `mousedown` default-prevented for button 1 (stops middle-click autoscroll).
- Button map (DOM `event.button` -> GlfwMouseButton): 0 -> Left(0), 1 -> Middle(2), 2 -> Right(1), 3 (back) -> Number4(3), 4 (forward) -> Number5(4). The built-in Emscripten GLFW only distinguishes 0, 1 -> 2 and everything else -> 1 (back/forward become right-click).[^emsc-libglfw]
- Cursor position: `clientX - rect.left`, `clientY - rect.top` in CSS pixels (screen coordinates), unclamped (contrib.glfw3 stopped clamping to the window).[^pongasoft-readme]
- Touch (`pointerType === 'touch'`): primary pointer becomes the left button and cursor, as both Emscripten implementations do; set `touch-action: none` on the canvas. Two-finger pinch/rotate can feed `OnTrackpadZoom`/`OnTrackpadRotate`.[^pongasoft-usage]

## Wheel

- Listener on the canvas with `{ passive: false }` and `preventDefault()`: wheel listeners default to passive on `window`/`document`/`body` and `preventDefault` is then ignored.[^mdn-addeventlistener]
- Normalisation (from contrib.glfw3, itself copied from SDL): `deltaMode` pixel x 1/100, line x 1/3, page x 80, then negate (GLFW positive Y = away from user): `offset = -delta * multiplier`.[^pongasoft-window][^mdn-wheel-deltamode] MDN warns to always check `deltaMode`.[^mdn-wheel-deltamode]
- BRUTAL's fork adds `OnScrollDetail` flags, which "only macOS reports"; other platforms raise it with the same offsets and no flags, so the shim raises `OnScroll` and `OnScrollDetail` identically and may set `Precise` for `deltaMode == 0` devices (heuristic, UNVERIFIED).[^brutal-glfw-window]
- Pinch: Chromium and Firefox report trackpad pinch as `wheel` with `ctrlKey === true`; Safari uses the non-standard `gesturestart/gesturechange/gestureend`. Map to `OnTrackpadZoom(scale factor per event)` and `OnTrackpadRotate(degrees since previous event, CCW positive)`, which is the fork's documented semantics.[^brutal-glfw-window] UNVERIFIED browser behaviour; test per browser.

## Pointer lock = `GLFW_CURSOR_DISABLED`

| Requirement | Consequence |
| --- | --- |
| `requestPointerLock()` needs transient activation; success/failure only via `pointerlockchange`/`pointerlockerror` on `document`[^mdn-pointerlock-request][^mdn-pointerlock-api] | `SetInputMode(Cursor, Disabled)` records the desired mode and tries immediately; if it fails or no activation exists it retries on the next activation-triggering event (`keydown` except Escape, `mousedown`, `pointerdown` for mouse, `pointerup` for non-mouse, `touchend`)[^html-activation] |
| After the user exits with the default gesture, an immediate re-request fails even with activation[^mdn-pointerlock-request] | treat Escape as user intent: flip the cached mode to Normal, emit `PointerLockChanged(false)` so the game can pause/show UI, and re-lock only when the game asks again |
| While locked `clientX/Y` freeze and `movementX/Y` keep coming[^mdn-pointerlock-api] | cursor position becomes a virtual unbounded position accumulating `movementX/Y` (contrib.glfw3 fixed a bug where it reported the delta instead)[^pongasoft-readme][^pongasoft-window]; on unlock restore the pre-lock position |
| `requestPointerLock({ unadjustedMovement: true })` disables OS acceleration; may reject with `NotSupportedError`; promise form not implemented everywhere[^mdn-pointerlock-api] | map `GLFW_RAW_MOUSE_MOTION = 1` to the option; feature-detect, fall back to a plain request; `Glfw.RawMotionSupported()` returns what the first attempt revealed (contrib.glfw3 does not support raw motion at all)[^pongasoft-window] |
| `requestFullscreen()` consumes the activation[^mdn-pointerlock-request] | request lock first when both are wanted |
| Pointer lock exists on desktop Chrome, Edge, Firefox and Safari 10.1+, not on iOS Safari or Chrome for Android (Can I use)[^caniuse-pointerlock] | no FPS-style mouse look on phones; use touch controls |

`GLFW_CURSOR_HIDDEN` = CSS `cursor: none`; `Normal` = restore the CSS cursor chosen by `SetCursor`. `GlfwStandardCursor` maps to CSS cursors (Arrow `default`, IBeam `text`, Crosshair `crosshair`, PointingHand `pointer`, ResizeEw `ew-resize`, ResizeNs `ns-resize`, ResizeNwse `nwse-resize`, ResizeNesw `nesw-resize`, ResizeAll `move`, NotAllowed `not-allowed`); custom `CreateCursor(image)` can render to a 2D canvas and use `cursor: url(dataURL) x y, auto` as contrib.glfw3 does.[^pongasoft-readme]

# Focus, visibility and lifecycle

- `window` `blur`, `document` `visibilitychange` (hidden) and `pagehide`: synthesise Release events for every key/button that is down, raise `OnFocus(false)` (and `OnIconify` for hidden), so no key stays stuck.
- `focus`/visible again: `OnFocus(true)`; the app should clamp the first `dt` after a hidden period (see below).
- `beforeunload`/`pagehide` -> `OnClose`. `ShouldClose` set by the game stops the loop; the page keeps a "stopped" state instead of closing.
- The reference ImGui backend queries `GetAttribute(Focused)`, `Hovered`, cursor enter/leave; track `document.hasFocus()` and pointer enter/leave to answer them.[^brutal-imgui-glfw]

# Gamepad

Polling API: `navigator.getGamepads()` returns a fresh snapshot per call and must be read every frame; `gamepadconnected` fires only after a button press or axis move when the page is focused (Firefox additionally requires visibility).[^mdn-gamepad] Poll inside `BeginPoll()`, diff against the previous snapshot, and emit Joystick events (`GlfwJoystickEvent.Connected/Disconnected`), exactly what contrib.glfw3 does inside `glfwPollEvents`.[^pongasoft-usage]

Standard mapping (button indices per the W3C table, axes 0-3 sticks) to BRUTAL's `GlfwGamepadState`:[^w3c-gamepad]

| `GlfwGamepadButton` (GLFW index) | W3C standard button index |
| --- | --- |
| A (0), B (1), X (2), Y (3) | 0, 1, 2, 3 |
| LeftBumper (4), RightBumper (5) | 4, 5 |
| Back (6), Start (7) | 8, 9 |
| Guide (8) | 16 |
| LeftThumb (9), RightThumb (10) | 10, 11 |
| DpadUp (11), DpadRight (12), DpadDown (13), DpadLeft (14) | 12, 15, 13, 14 |

Axes: LeftX/LeftY/RightX/RightY = `axes[0..3]`; LeftTrigger/RightTrigger = `buttons[6].value` / `buttons[7].value` rescaled to GLFW's [-1, 1] range (`v * 2 - 1`), which is how contrib.glfw3 represents them.[^pongasoft-readme] Note the Guide index differs (8 in GLFW, 16 in W3C), a trap contrib.glfw3 documents.[^pongasoft-usage] `JoystickIsGamepad` = `gamepad.mapping === 'standard'`; `GetJoystickName`/`Guid` = `gamepad.id`/`mapping`. The reference game reads `Glfw.GetGamepadState(0, ...)` every frame.[^ksa-program]

# Clipboard

`Glfw.Clipboard` is synchronous but the browser clipboard is asynchronous and permission gated. Keep a managed cache: `Set` writes the cache and calls `navigator.clipboard.writeText` (secure context, user activation); `window` `paste` events (triggered by the user's Ctrl/Cmd+V, which the preventDefault policy lets through) update the cache from `clipboardData`, so ImGui's synchronous read on the next frame sees the new text. contrib.glfw3 documents the same limitation: its synchronous getter returns whatever was last set and ignores the external clipboard.[^pongasoft-readme] UNVERIFIED across browsers.

# Time and the loop

- `Glfw.Time` = `(performance.now() - t0) / 1000`, `SetTime` moves `t0`, `GetTimerValue()` = integer microseconds, `GetTimerFrequency()` = 1,000,000. `requestAnimationFrame` timestamps use the same origin (`document.timeline.currentTime`), are identical for all callbacks in a frame and are coarsened to at least 1 ms precision.[^mdn-raf]
- Drive the game from `requestAnimationFrame`, not timers: rAF follows the display (60/120/144 Hz), is paused in background tabs, and animation must use the timestamp or it "will run faster on high refresh-rate screens".[^mdn-raf] Nested `setTimeout` is clamped to 4 ms after five levels and throttled in background tabs (Firefox 1 s, Chrome once per second then once per minute with intensive throttling), though throttling is waived while a page plays sound through an `AudioContext`.[^mdn-settimeout] .NET's own single-threaded scheduler is built on `setTimeout`, which is why an `await Task.Yield()` loop is a poor frame driver.[^dotnet-scheduling] Details in [main loop patterns](/io-audio/main-loop-patterns-in-dotnet-wasm.md).
- Fixed-step simulation: accumulate `dt = (t - last)/1000`, clamp to a maximum (for example 0.1-0.25 s) after hidden periods or breakpoints, step in `fixedDt` slices, render once. The reference game uses a variable `dtPlayer` and a `FrameLimit` with `Thread.Sleep`, which must not run in the browser.
- A frame cap, if wanted, skips rAF callbacks (`if (t - last < 1000/limit - 1) return;`) instead of sleeping.

# JS module surface (illustrative)

```js
// brutal-browser.js - registered with setModuleImports("brutal-browser", api)
export function init(canvasSelector) { /* attach listeners, ResizeObserver, matchMedia */ }
export function beginPoll() { /* apply pending resize; poll gamepads; push events */ }
export function drain(buf /* Float64Array view */) { /* copy up to buf.length/8 records */ return n; }
export function setCursorMode(mode, raw) { /* pointer lock / cursor:none / normal */ }
export function setFullscreen(on) { /* requestFullscreen */ }
export function getTimeSeconds() { return performance.now() / 1000; }
export function setClipboard(text) {}
```

## Open questions

- UNVERIFIED: `JSImport` with `Span<double>`/`MemoryView` parameters in .NET 10 (fallback `double[]` copy).
- UNVERIFIED: Safari support of `device-pixel-content-box`, `navigator.keyboard.getLayoutMap`, and Safari/Firefox behaviour for pinch gestures; test in each browser.
- UNVERIFIED: whether `KeyboardEvent.code` values `IntlBackslash`/`IntlRo`/`IntlYen` should map to `World1`/`World2` or to nothing for non-US layouts.
- How many of the ImGui backend's GLFW calls (monitors, `MousePassthrough`) need non-trivial answers on browser.

[^mdn-keyboard-api]: MDN Keyboard API (Keyboard Lock, KeyboardEvent.code vs key)
[^mdn-pointerlock-request]: MDN Element.requestPointerLock()
[^mdn-pointerlock-api]: MDN Pointer Lock API
[^caniuse-pointerlock]: Can I use - Pointer Lock API
[^mdn-raf]: MDN Window.requestAnimationFrame()
[^mdn-settimeout]: MDN Window.setTimeout() (nesting clamp, background throttling)
[^mdn-resize-dpcb]: MDN ResizeObserverEntry.devicePixelContentBoxSize
[^mdn-gamepad]: MDN Using the Gamepad API
[^w3c-gamepad]: W3C Gamepad - Standard Gamepad mapping
[^mdn-wheel-deltamode]: MDN WheelEvent.deltaMode
[^mdn-addeventlistener]: MDN EventTarget.addEventListener() (passive defaults)
[^html-activation]: HTML Standard - activation triggering input event
[^ms-jsinterop]: JavaScript JSImport/JSExport interop in .NET WebAssembly (aspnetcore-10.0)
[^pongasoft-window]: contrib.glfw3 Window.cpp (wheel normalisation, cursor modes, pointer lock)
[^pongasoft-keymap]: contrib.glfw3 KeyboardMapping.h (KeyboardEvent.code to scancode to GLFW key)
[^pongasoft-usage]: contrib.glfw3 docs/Usage.md (keyboard, Super key, gamepad, clipboard, fullscreen, Hi-DPI)
[^pongasoft-readme]: contrib.glfw3 README release notes
[^emsc-libglfw]: Emscripten libglfw.js (built-in GLFW)
[^brutal-glfw-window]: Brutal.Glfw Glfw.Window.cs
[^brutal-glfw]: Brutal.Glfw source (enums, constants)
[^brutal-imgui-glfw]: Brutal.ImGui.Backends GlfwBackend.cs
[^ksa-program]: KSA KSA/Program.cs (reference only)
[^dotnet-scheduling]: dotnet/runtime release/10.0 scheduling.ts
[^sib-jsinterop]: JS interop on .NET 10 browser-wasm (sibling note)
