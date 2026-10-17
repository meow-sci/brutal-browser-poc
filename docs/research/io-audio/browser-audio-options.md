---
type: Technology Assessment
title: Browser audio options for BRUTAL games - OpenAL on Emscripten vs managed Web Audio, Ogg Vorbis decode matrix, FMOD and SDL notes
description: Compares Emscripten's library_openal.js with a managed Web Audio shim driven through JSImport, documents autoplay handling, 3D audio, streaming and latency, gives the Ogg Vorbis decodeAudioData support matrix and the managed-decoder fallback (NVorbis), and notes FMOD HTML5 and SDL audio.
tags: [audio, web-audio, openal, ogg-vorbis, decodeaudiodata, nvorbis, fmod, autoplay, audioworklet, strategy-b]
sources:
  - id: emsc-libopenal
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/main/src/lib/libopenal.js
    title: Emscripten src/lib/libopenal.js (main, fetched 2026-10-16)
  - id: emsc-libopenal-3156
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/3.1.56/src/library_openal.js
    title: Emscripten 3.1.56 src/library_openal.js
  - id: emsc-libcore
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/main/src/lib/libcore.js
    title: Emscripten src/lib/libcore.js ($autoResumeAudioContext)
  - id: emsc-libwebaudio
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/main/src/lib/libwebaudio.js
    title: Emscripten src/lib/libwebaudio.js (Wasm Audio Worklets)
  - id: html-activation
    resource: https://html.spec.whatwg.org/multipage/interaction.html#activation-triggering-input-event
    title: HTML Standard - activation triggering input event
  - id: mdn-autoplay
    resource: https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Autoplay
    title: MDN Autoplay guide (Web Audio section, getAutoplayPolicy)
  - id: chrome-autoplay
    resource: https://developer.chrome.com/blog/web-audio-autoplay
    title: Chrome - Web Audio autoplay policy
  - id: chrome-audioworklet
    resource: https://developer.chrome.com/blog/audio-worklet
    title: Chrome - Enter Audio Worklet
  - id: mdn-decode
    resource: https://developer.mozilla.org/en-US/docs/Web/API/BaseAudioContext/decodeAudioData
    title: MDN BaseAudioContext.decodeAudioData()
  - id: webaudio-spec
    resource: https://webaudio.github.io/web-audio-api/#dom-baseaudiocontext-decodeaudiodata
    title: Web Audio API spec - decodeAudioData
  - id: mdn-panner
    resource: https://developer.mozilla.org/en-US/docs/Web/API/PannerNode
    title: MDN PannerNode
  - id: mdn-codecs
    resource: https://developer.mozilla.org/en-US/docs/Web/Media/Guides/Formats/Audio_codecs
    title: MDN Web audio codec guide (Vorbis browser support row)
  - id: caniuse-ogg
    resource: https://caniuse.com/ogg-vorbis
    title: Can I use - Ogg Vorbis audio format
  - id: webkit-184
    resource: https://webkit.org/blog/16574/webkit-features-in-safari-18-4/
    title: WebKit blog - WebKit Features in Safari 18.4
  - id: cog-375
    resource: https://github.com/dvoyni/cog/issues/375
    title: dvoyni/cog issue 375 - Web Audio Ogg Vorbis across browsers (secondary source, no browser tests by its author)
  - id: oggmented
    resource: https://github.com/jfrancos/oggmented
    title: jfrancos/oggmented - decodeAudioData(oggVorbisBuffer) in any browser
  - id: nvorbis-gh
    resource: https://github.com/NVorbis/NVorbis
    title: NVorbis GitHub repository (README)
  - id: nvorbis-nuget
    resource: https://www.nuget.org/packages/NVorbis
    title: NuGet - NVorbis
  - id: ms-jsinterop
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/?view=aspnetcore-10.0
    title: JavaScript JSImport/JSExport interop in .NET WebAssembly (aspnetcore-10.0)
  - id: fmod-html5
    resource: https://www.fmod.com/docs/2.02/api/platforms-html5.html
    title: FMOD Engine 2.02 - Platform Details HTML5 (JavaScript-rendered page; facts taken from a search summary)
  - id: fmod-licensing
    resource: https://fmod.com/licensing
    title: FMOD licensing (JavaScript-rendered page; facts taken from a search summary)
  - id: sdl-emaudio
    resource: https://raw.githubusercontent.com/libsdl-org/SDL/release-3.4.x/src/audio/emscripten/SDL_emscriptenaudio.c
    title: SDL release-3.4.x src/audio/emscripten/SDL_emscriptenaudio.c
  - id: mdn-settimeout
    resource: https://developer.mozilla.org/en-US/docs/Web/API/Window/setTimeout
    title: MDN Window.setTimeout() (timer throttling and AudioContext exemption)
  - id: brutal-openal
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl
    title: BRUTAL Brutal.OpenAl source
  - id: brutal-sdl-mixer-notices
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl.Mixer/LICENSES/BRUTAL-SDL-MIXER-THIRD-PARTY-NOTICES.txt
    title: Brutal.Sdl.Mixer third-party notices (SDL_mixer 3.2.4 with stb_vorbis, dr_flac, dr_mp3)
  - id: brutal-openal-loader
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Manual/NativeLoader.cs
    title: Brutal.OpenAl NativeLoader.cs
  - id: brutal-platform
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Platform.cs
    title: Brutal.Core Platform.cs
  - id: ksa-audio
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/GameAudio.cs
    title: KSA KSA/GameAudio.cs (FMOD use, reference only)
  - id: ogg-probe
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/KSA_SFX_ROCKET_START_V2_01.ogg
    title: PoC audio asset (file, ffprobe, afinfo probes run 2026-10-16)
  - id: edge-test
    resource: "scope: headless Microsoft Edge 155.0.4283.45 on macOS driven over the DevTools protocol; page decoded the PoC OGG (base64 in the page) with AudioContext.decodeAudioData, 2026-10-16 (session scratchpad r5/ogg-test)"
    title: Local decodeAudioData test in Edge 155
  - id: ksa-linux
    resource: /Users/asherwin/repos/meow-sci/ksa-linux/Content/Core
    title: KSA Linux build Content/Core (file probes of Sounds/Music, read-only)
  - id: gap-script
    resource: "scope: python3 -I comparison of Brutal.OpenAl EntryPoint strings with libopenal.js function names, run 2026-10-16 (session scratchpad r5/cmp_al.py)"
    title: OpenAL entry-point gap analysis
  - id: sib-native-linking
    resource: /dotnet-wasm/native-linking-and-emscripten.md
    title: Native linking in .NET 10 wasm builds, Emscripten 3.1.56, ports (sibling note)
  - id: sib-fnptr
    resource: /dotnet-wasm/function-pointers-and-native-callbacks.md
    title: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (sibling note)
  - id: sib-jsinterop
    resource: /dotnet-wasm/js-interop-jsimport-jsexport.md
    title: JS interop on .NET 10 browser-wasm (sibling note)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:08:04Z }
status: draft
stale_after: 2027-01-31
---

# The PoC asset and what the real game needs

- `KSA_SFX_ROCKET_START_V2_01.ogg`: Ogg Vorbis, stereo, 48,000 Hz, 3.4067 s, libVorbis 1.3.3, 88 KB; `afinfo` reports 163,520 valid frames plus 128 priming and 896 remainder frames.[^ogg-probe]
- The KSA Linux content has 37 `.ogg` files under `Content/Core/Sounds` and `Music` (all sampled ones stereo 48 kHz Vorbis, about 224-256 kb/s) and 38 `.wav` files.[^ksa-linux]
- KSA plays through FMOD core (1024 channels, 3D right-handed, sound/channel groups, custom file system callbacks, compressor, multiband EQ, delay, reverb and convolution-reverb DSPs), not OpenAL.[^ksa-audio] The PoC therefore only needs "decode a file, play it with gain, optionally positioned"; fidelity to BRUTAL's `Brutal.OpenAlApi` is a nice-to-have, FMOD fidelity is a later decision (see [recommendation](/io-audio/recommendation-io-audio.md)).

# Option A: Emscripten's OpenAL (`-lopenal`, `library_openal.js`)

## What it provides

- Device and context: device name "Emscripten OpenAL", capture "Emscripten OpenAL capture"; each ALC context owns a Web Audio `AudioContext` and a master `GainNode`.[^emsc-libopenal]
- Advertised extensions: ALC `ALC_EXT_capture`, `ALC_SOFT_pause_device`, `ALC_SOFT_HRTF`; AL `AL_EXT_float32`, `AL_SOFT_loop_points`, `AL_SOFT_source_length`, `AL_EXT_source_distance_model`, `AL_SOFT_source_spatialize`. A source file comment notes `ALC_EXT_EFX` is a TODO.[^emsc-libopenal] (Emscripten 3.1.56 lists the same minus `ALC_EXT_capture`.)[^emsc-libopenal-3156]
- Buffer formats: `alBufferData` accepts MONO8, MONO16, STEREO8, STEREO16, MONO_FLOAT32 and STEREO_FLOAT32 only, creating `AudioBuffer`s via `createBuffer`.[^emsc-libopenal]
- Playback model: each queued OpenAL buffer becomes an `AudioBufferSourceNode`; a `setInterval` every 25 ms (`QUEUE_INTERVAL`) schedules audio up to 100 ms ahead (`QUEUE_LOOKAHEAD`). Static looping sources use native looping; buffer queues are stitched by the scheduler, so streaming depends on the main thread getting a timer slot at least every ~100 ms.[^emsc-libopenal]
- 3D: a `PannerNode` is created only for sources with `AL_SOURCE_SPATIALIZE_SOFT` true, or auto and mono; model `HRTF` when the context asked for it, otherwise `equalpower`; distance models map AL inverse/inverse-clamped to Web Audio `inverse`, linear to `linear`, exponent to `exponential` (clamped and unclamped variants collapse together); `AL_NONE` becomes `inverse` with `refDistance = FLT_MAX`.[^emsc-libopenal] The listener uses `positionX/Y/Z` and `forwardX/.../upX/...` AudioParams.
- Capture: via `ScriptProcessorNode` on a shared capture `AudioContext`.[^emsc-libopenal]
- Autoplay: `alcCreateContext` calls `autoResumeAudioContext(ac)`, which registers one-shot `keydown`, `mousedown` and `touchstart` listeners on `document` and the element with id `canvas` and calls `ctx.resume()` if suspended.[^emsc-libcore] The HTML standard's activation-triggering events are `keydown` (except Escape), `mousedown`, `pointerdown` (mouse), `pointerup` (non-mouse) and `touchend`; `touchstart` is not one, so on touch devices the resume only takes effect via the synthesised mouse events or a later `touchend`.[^html-activation] UNVERIFIED on real iOS.
- Maintenance: recent commits to the file are refactors and bug fixes (for example "Remove all references to webkitAudioContext" 2026-07-30, "Fix sourceSeek crash on stopped sources" 2026-07-29, `const`/strict-equality cleanups to 2026-10-14); no feature work.[^emsc-libopenal]

## Fit with `Brutal.OpenAl`

`Brutal.OpenAl` binds 126 core entry points. `libopenal.js` lacks 35 of them: all 33 EFX effect/filter/auxiliary-slot functions plus `alGetProcAddress` and `alcGetProcAddress`.[^gap-script]

- Core device/context/buffer/source/listener calls used by a basic game are present, so `Alc.OpenDevice` -> `CreateContext` -> `MakeCurrent` -> `GenSource`/`GenBuffer` -> `SetData` -> `Play` would work.[^brutal-openal]
- Brutal.OpenAl resolves its 50 extension entry points (`AL_SOFT_source_latency`, events, callback buffers, deferred updates, loopback, device clock, reopen device, `AL_EXT_debug`, ...) lazily through `alcGetProcAddress`; with Emscripten that symbol does not exist and none of those wrappers can work.[^brutal-openal]
- The loader needs changes: `NativeLoader` is a `[ModuleInitializer]` that maps logical name `openal` to a platform file name via `Platform.CurrentFamily`, and `Platform`'s static constructor throws `PlatformNotSupportedException` unless the OS is Windows, Linux or macOS.[^brutal-openal-loader][^brutal-platform]
- Linking needs the net10 `wasm-tools` workload (not installed), `EmccExtraLDFlags=-lopenal` (the 3.1.56 cache includes the OpenAL library, per the sibling note), and `WasmAllowUndefinedSymbols` or stubs for the 35 missing symbols; see [Emscripten GLFW options](/io-audio/emscripten-glfw-options.md) for the shared mechanics and caveats.[^sib-native-linking]
- Brutal.OpenAl's `NativeLoader` resolver also has to be reconciled with Mono's static P/Invoke tables: the sibling analysis notes that `NativeLibrary.Load` only succeeds for statically linked module names and that foreign handles must not be returned from a resolver.[^sib-fnptr]

## Verdict for A

Works for plain buffers/sources/3D with a loader shim and stubs. It still decodes nothing: the application must supply PCM (`alBufferData`), so Ogg Vorbis decoding is a separate problem (managed NVorbis, below). All of Web Audio's autoplay and scheduling behaviour is inherited, with a fixed 25 ms main-thread timer.

# Option B: managed Web Audio through `[JSImport]`

## Minimal API for the PoC

Shape (JS module registered as `brutal-browser-audio`, C# bindings via `[JSImport]`):

```js
let ctx;
const sounds = new Map();
export function create() {                      // call inside a user gesture, or call resume() later
  ctx ??= new AudioContext({ latencyHint: 'interactive', sampleRate: 48000 });
  return ctx.state;                             // 'running' | 'suspended'
}
export async function resume() { if (ctx.state !== 'running') await ctx.resume(); return ctx.state; }
export async function loadUrl(id, url) {
  const bytes = await (await fetch(url)).arrayBuffer();
  sounds.set(id, await ctx.decodeAudioData(bytes));   // throws EncodingError if the codec is unsupported
}
export function play(id, gain, loop) {
  const src = ctx.createBufferSource(); src.buffer = sounds.get(id); src.loop = loop;
  const g = ctx.createGain(); g.gain.value = gain;
  src.connect(g).connect(ctx.destination); src.start(); return src;
}
```

```csharp
internal static partial class WebAudio {
    [JSImport("create", "brutal-browser-audio")] public static partial string Create();
    [JSImport("resume", "brutal-browser-audio")] public static partial Task<string> ResumeAsync();
    [JSImport("loadUrl", "brutal-browser-audio")] public static partial Task LoadUrlAsync(string id, string url);
    [JSImport("play", "brutal-browser-audio")] public static partial JSObject Play(string id, double gain, bool loop);
}
```

Notes grounded in the specs:

- `decodeAudioData` only accepts complete files, resamples the result to the context's sample rate, returns a Promise, detaches the passed `ArrayBuffer` and rejects with `EncodingError` when it cannot decode; decoding runs on a dedicated decoding thread, not the control or rendering thread.[^mdn-decode][^webaudio-spec] Creating the context at 48 kHz (the game's asset rate) avoids resampling. Whole-file decoding means long music is held as PCM (a 3.4 s stereo clip is 164k frames); use `MediaElementAudioSourceNode` or an AudioWorklet for long tracks.
- Autoplay: an `AudioContext` created or started outside a user activation is subject to autoplay rules and starts `suspended`; call `resume()` from a user-activation event, and `navigator.getAutoplayPolicy(ctx)` (`allowed`, `allowed-muted`, `disallowed`) tells whether to wait.[^mdn-autoplay] Chrome documents that a context is resumed automatically when the user has interacted with the page and `start()` is called on a source, and its helper snippet resumes on click, contextmenu, auxclick, dblclick, mousedown, mouseup, pointerup, touchend, keydown and keyup.[^chrome-autoplay] Firefox gates it by sticky activation via `media.autoplay.block-webaudio` (default true).[^mdn-autoplay] Design: show a "click to start" overlay (also needed for pointer lock and fullscreen) that calls `create()` and `resume()` in the same handler.
- Tab throttling: timers in background tabs are throttled, but the throttle is waived (Firefox) or reduced (Chrome "minimal throttling") for pages playing sound, so a playing `AudioContext` also keeps the game's timers alive.[^mdn-settimeout]
- 3D: `PannerNode` uses right-handed Cartesian coordinates; `positionX/Y/Z` and `orientationX/Y/Z` are `AudioParam`s (defaults 0,0,0 and 1,0,0), `distanceModel` is `linear`, `inverse` (default) or `exponential`, and `coneOuterGain` defaults to 0.[^mdn-panner] KSA's FMOD uses a right-handed 3D setup, which maps directly; FMOD rolloff curves and OpenAL's clamped models do not have exact Web Audio equivalents (same collapse as Emscripten's mapping above).[^ksa-audio][^emsc-libopenal]
- Sample-accurate scheduling: `AudioBufferSourceNode.start(when)` with `ctx.currentTime`, and `AudioParam` automation for fades; loop points via `loopStart`/`loopEnd` (FMOD loop bounds, `AL_SOFT_loop_points`).
- Streaming/generated audio: `AudioWorklet` runs processors on the audio rendering thread in 128-frame quanta, needs a secure context (HTTPS or `http://localhost`); `ScriptProcessorNode` is deprecated.[^chrome-audioworklet] Emscripten's Wasm Audio Worklets (`-sAUDIO_WORKLET`) exist for native code but are irrelevant to a managed design.[^emsc-libwebaudio]
- Interop cost and types: passing PCM from C# needs bytes, not `float[]`: in .NET 10 `Array of` marshalling covers Byte, Int32 and Double (Single is listed as not supported), and `Span<byte|int|double>` becomes a zero-copy `MemoryView` valid only for the call. `float[]`, `Span<float>` and `ArraySegment<float>` arrive only with .NET 11 (the sibling note read `JSMarshalerArgument.Single.cs` on `release/10.0` and `main`). Send the decoded floats as `Span<byte>`/`byte[]` and wrap them with `new Float32Array(...)` in JS.[^ms-jsinterop][^sib-jsinterop] UNVERIFIED in practice.

## BRUTAL fidelity of option B

Two layers are possible and compatible:

1. A small engine-neutral API (`Brutal.Browser.Audio`: `AudioEngine`, `Sound`, `Voice`) used directly by the PoC; no BRUTAL audio types involved.
2. Later, adapters that implement `Brutal.OpenAlApi` (swap `Al.PInvoke`/`Alc.PInvoke` with managed Web Audio code, see [API surface](/io-audio/brutal-glfw-and-openal-api-surface.md)) and/or an FMOD-shaped facade. Rough mapping of the FMOD features KSA uses (design inference, not sourced beyond KSA's own types):[^ksa-audio]

| FMOD feature in KSA | Web Audio building block |
| --- | --- |
| `System.Init(1024, 3dRightHanded)`, `Update()` | one `AudioContext`, voice pool, per-frame listener update |
| `CreateSound(OpenMemory)` / `CreateStream` | `decodeAudioData` (whole file) / `MediaElementAudioSourceNode` or worklet streaming |
| `ChannelGroup` (volume, pitch, mute) | `GainNode` tree, `playbackRate`/`detune` |
| 3D attributes, `Set3dSettings` | `PannerNode` + `AudioListener` |
| loop bounds | `loopStart`/`loopEnd` |
| Compressor, MultibandEq, Delay, DigitalReverb, ConvolutionReverb DSPs | `DynamicsCompressorNode`, `BiquadFilterNode` chain, `DelayNode`, `ConvolverNode` (reverb needs an impulse response or a worklet) |
| `SetFileSystem` callbacks | managed virtual file system over `fetch` |

# Ogg Vorbis decode matrix (2026-10-16)

| Browser | `<audio>`/format support | `decodeAudioData` of Ogg Vorbis | Evidence and caveats |
| --- | --- | --- | --- |
| Chrome / Edge (Chromium) | Vorbis supported for years (MDN: Chrome 4, Edge 17)[^mdn-codecs] | **Yes - verified here**: Edge 155 headless on macOS decoded the PoC file to 2 channels, 48,000 Hz, 163,520 frames, 3.4067 s[^edge-test] | `canPlayType('audio/ogg; codecs="vorbis"')` returned "probably"[^edge-test] |
| Firefox | Vorbis since 3.5 (MDN row: Firefox 3.5)[^mdn-codecs] | Yes per secondary source; **not tested here** (headless Firefox could not run inside the tool sandbox)[^cog-375] | UNVERIFIED locally |
| Safari macOS 15.4+ / Safari 18.4+ and iOS/iPadOS 18.4+ | Can I use lists Ogg Vorbis as supported from Safari 18.4 (macOS) and iOS 18.4, "partial" in 14.1-18.3 (macOS) and 17.4-18.3 (iOS); the note says it covers use in the `audio` element only[^caniuse-ogg]; WebKit's 18.4 post announces "Ogg container support for both Opus and Vorbis audio" on macOS Sequoia 15.4 and iOS 18.4[^webkit-184] | **UNVERIFIED**: a secondary source says decoding depends on the OS version (macOS 15.4+, iOS/iPadOS 18.4+, not the Safari version), and that a WebKit `main` rewrite broke Ogg decoding (bug 316172, fixed 2026-06-03), so only Safari 27 betas were affected; it ran no browser tests[^cog-375] | the WebKit post does not mention Web Audio or `decodeAudioData`[^webkit-184] |
| Safari macOS < 15.4, iOS < 18.4 | MDN's older table says Safari "No" for Vorbis[^mdn-codecs]; the "partial" ranges are Vorbis-in-WebM, not Ogg (per the secondary source)[^cog-375] | **No**: `decodeAudioData(oggVorbisBuffer)` is documented as unsupported in Safari/iOS by the oggmented project (undated)[^oggmented] | needs the managed fallback |

Policy: never sniff the user agent. At start-up try `decodeAudioData` on the real asset (or a tiny embedded clip) and, on `EncodingError`, fall back to a managed decode. This also makes Safari-version uncertainty irrelevant. `Safari`/`iOS` testing of the exact PoC file remains an open item.

# Managed decoding fallbacks

| Option | Notes |
| --- | --- |
| **NVorbis** (C#) | MIT; "designed to run in partial trust environments and does not require P/Invoke or unsafe code"; MDCT and Huffman generator adapted from public-domain stb_vorbis; API `new VorbisReader(path/stream)`, `Channels`, `SampleRate`, `ReadSamples(float[], offset, count)` returns interleaved floats; stable 0.10.5 (NuGet last updated 2022-10-16), pre-release 1.0.0-rc.2 dated 2026-07-13; targets netstandard2.0 / .NET Framework 4.5 with `System.Memory` and `System.ValueTuple` dependencies.[^nvorbis-gh][^nvorbis-nuget] Runs on any .NET target including the Mono wasm interpreter; speed there is **not measured** (interpreted C# is slower than AOT; decode only once per asset and cache PCM). A 3.4 s clip is small; the 37 KSA sounds should be decoded lazily or pre-decoded. |
| **stb_vorbis / libvorbis compiled to a standalone wasm module** | What oggmented does (an MIT-licensed package that patches `decodeAudioData` and uses an Emscripten build of libogg 1.3.4 and libvorbis 1.3.6);[^oggmented] needs no .NET native relink if shipped as a separate wasm module called from JS, but adds a toolchain (emcc, not installed here) and a second wasm. The same stb_vorbis decoder is already inside the SDL_mixer build that Brutal.Sdl.Mixer ships.[^brutal-sdl-mixer-notices] |
| **WebCodecs `AudioDecoder`** (`vorbis`) | Per the secondary source shipped in Chrome 94, Firefox 130, Safari 26 (secure context), but likely shares Safari's OS codec so adds no Safari coverage; needs Ogg demuxing and Vorbis header `description` handling.[^cog-375] UNVERIFIED. |
| **Transcode assets at build time** | Converting to a container/codec every target browser decodes (for example AAC in MP4/M4A, or MP3) removes the decoder question but is a lossy re-encode of already lossy audio and changes the asset pipeline; Opus in Ogg has the same Safari container dependency as Vorbis in Ogg. UNVERIFIED for exact Safari versions. |

# FMOD HTML5 (note only)

- FMOD publishes an HTML5 platform build for Emscripten applications. As summarised from FMOD 2.02's platform page: built with Emscripten 3.1.67; needs a web server; minimum Firefox 79, Safari 14.1.0, Chrome 85; without SharedArrayBuffer there are no threads, so mixing, streaming and loading all run on the game thread; no sockets (no Live Update/profiling). The page itself is JavaScript-rendered and was not directly readable, so treat these as UNVERIFIED.[^fmod-html5]
- `Brutal.Fmod` binds the C API (`[LibraryImport("fmod", EntryPoint = "FMOD5_...")]`, header version 2.03.08) and `FmodStudio` loads `fmod` first; making those P/Invokes resolve in .NET wasm would need FMOD's static HTML5 library linked into `dotnet.native.wasm` (native relink, Emscripten version compatibility between FMOD's 3.1.67 and .NET's pin) or a JS-API-based replacement. See [API surface](/io-audio/brutal-glfw-and-openal-api-surface.md).
- Licence: proprietary (FMOD EULA); search results report a free indie tier gated by revenue and budget (figures conflict between sources: under $200k revenue historically; the current page lists an Indie tier "Under $600k") and do not mention HTML5 specifically. Verify with Firelight Technologies before any use. UNVERIFIED.[^fmod-licensing]
- KSA's FMOD use (DSP chains, custom file system, 1024 channels) is far larger than the PoC needs, which argues for a Web Audio facade rather than porting FMOD.[^ksa-audio]

# SDL audio (note only)

SDL3's Emscripten audio driver uses a `ScriptProcessorNode` and a silence callback until the user activates the page, then `resume()`s; details and the Brutal.Sdl angle are in [the SDL3 note](/io-audio/sdl3-emscripten-alternative.md).[^sdl-emaudio]

# Comparison

| Criterion | A: Emscripten OpenAL | B: managed Web Audio |
| --- | --- | --- |
| Needs wasm-tools workload / native relink | yes | no |
| Needs loader change in Brutal.OpenAl | yes (+ stubs for 35 symbols) | none (new code) |
| Ogg Vorbis | not solved (PCM only) | `decodeAudioData` + NVorbis fallback |
| Extension APIs (`alcGetProcAddress` users) | unavailable | can be implemented selectively |
| 3D | PannerNode with lossy distance-model mapping | PannerNode, controllable |
| Streaming | 25 ms main-thread timer queue | AudioWorklet possible |
| Effort for the PoC | medium-high (toolchain) | low |

## Open questions

- UNVERIFIED: Safari 18.4+/macOS 15.4+ `decodeAudioData` of Ogg Vorbis (needs a run on real Safari, plus the Safari 27 regression status).
- UNVERIFIED: Firefox decode of the PoC file (not run here).
- UNVERIFIED: NVorbis throughput on the Mono interpreter vs AOT.
- UNVERIFIED: FMOD HTML5 licensing terms and linkability into `dotnet.native.wasm`.
- Which BRUTAL audio API do target games use (OpenAL, FMOD or SDL_mixer), since only KSA's FMOD use was inspected?

[^emsc-libopenal]: Emscripten src/lib/libopenal.js (main, fetched 2026-10-16)
[^emsc-libopenal-3156]: Emscripten 3.1.56 src/library_openal.js
[^emsc-libcore]: Emscripten src/lib/libcore.js ($autoResumeAudioContext)
[^emsc-libwebaudio]: Emscripten src/lib/libwebaudio.js (Wasm Audio Worklets)
[^html-activation]: HTML Standard - activation triggering input event
[^mdn-autoplay]: MDN Autoplay guide (Web Audio section, getAutoplayPolicy)
[^chrome-autoplay]: Chrome - Web Audio autoplay policy
[^chrome-audioworklet]: Chrome - Enter Audio Worklet
[^mdn-decode]: MDN BaseAudioContext.decodeAudioData()
[^webaudio-spec]: Web Audio API spec - decodeAudioData
[^mdn-panner]: MDN PannerNode
[^mdn-codecs]: MDN Web audio codec guide (Vorbis browser support row)
[^caniuse-ogg]: Can I use - Ogg Vorbis audio format
[^webkit-184]: WebKit blog - WebKit Features in Safari 18.4
[^cog-375]: dvoyni/cog issue 375 - Web Audio Ogg Vorbis across browsers (secondary source, no browser tests by its author)
[^oggmented]: jfrancos/oggmented - decodeAudioData(oggVorbisBuffer) in any browser
[^nvorbis-gh]: NVorbis GitHub repository (README)
[^nvorbis-nuget]: NuGet - NVorbis
[^ms-jsinterop]: JavaScript JSImport/JSExport interop in .NET WebAssembly (aspnetcore-10.0)
[^fmod-html5]: FMOD Engine 2.02 - Platform Details HTML5 (JavaScript-rendered page; facts taken from a search summary)
[^fmod-licensing]: FMOD licensing (JavaScript-rendered page; facts taken from a search summary)
[^sdl-emaudio]: SDL release-3.4.x src/audio/emscripten/SDL_emscriptenaudio.c
[^mdn-settimeout]: MDN Window.setTimeout() (timer throttling and AudioContext exemption)
[^brutal-openal]: BRUTAL Brutal.OpenAl source
[^brutal-sdl-mixer-notices]: Brutal.Sdl.Mixer third-party notices (SDL_mixer 3.2.4 with stb_vorbis, dr_flac, dr_mp3)
[^brutal-openal-loader]: Brutal.OpenAl NativeLoader.cs
[^brutal-platform]: Brutal.Core Platform.cs
[^ksa-audio]: KSA KSA/GameAudio.cs (FMOD use, reference only)
[^ogg-probe]: PoC audio asset (file, ffprobe, afinfo probes run 2026-10-16)
[^edge-test]: Local decodeAudioData test in Edge 155
[^ksa-linux]: KSA Linux build Content/Core (file probes of Sounds/Music, read-only)
[^gap-script]: OpenAL entry-point gap analysis
[^sib-native-linking]: Native linking in .NET 10 wasm builds, Emscripten 3.1.56, ports (sibling note)
[^sib-fnptr]: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (sibling note)
[^sib-jsinterop]: JS interop on .NET 10 browser-wasm (sibling note)
