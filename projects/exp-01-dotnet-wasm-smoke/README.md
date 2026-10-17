# exp-01 — .NET 10 browser-wasm smoke test for BRUTAL

This experiment answers concrete feasibility questions about running the open-source BRUTAL C# framework
(the `Ahwoo.BRUTAL.*` NuGet packages) inside a browser with .NET 10's `browser-wasm` (Mono interpreter).
It deliberately runs **without** the `wasm-tools` workload, i.e. on the prebuilt `dotnet.native.wasm`
(no native relink, no AOT).

Results: [RESULTS.md](RESULTS.md). Research write-up (OKF):
[`docs/research/experiments/exp-01-dotnet-wasm-smoke.md`](../../docs/research/experiments/exp-01-dotnet-wasm-smoke.md).

## What it validates

| Area | Tests |
|---|---|
| Runtime environment (wasm32, SIMD, timers, GC limits) | T01 |
| BRUTAL pure-managed packages: Core `Platform`, Numerics/Maths, Collections, Logging, Memory | T02, T03, T03b–d, T04, T04b |
| Brutal.Vulkan static loader (`static Vulkan()`), and Vulkan types usable without it | T05, T05b |
| `NativeLibrary`, `[DllImport]`, BRUTAL's own `[LibraryImport("vulkan")]`, `SetDllImportResolver` | T06, T06b–d |
| Function pointers: `[UnmanagedCallersOnly]` (UCO) targets, the "managed ICD" shape, BRUTAL `VkInstance.FunctionTable` | T07a, T07, T08, T08b |
| Function pointers implemented in JS (`Module.addFunction`), incl. a JS hop back into C# `[JSExport]` | T08c, T08d, T08e, T09b |
| Mixed-type arguments, `Marshal.GetFunctionPointerForDelegate` | T09, T10 |
| JS interop: `[JSImport]` (bool, `Span<byte>` as MemoryView, raw pointer + `localHeapViewU8()`, `Task`/Promise), WebGPU adapter | T11a–e |
| `[JSExport]` driven from `requestAnimationFrame` after `Main` returns | T12 |
| Call-cost micro-benchmarks (interpreter) | T13a–f |
| AOT / native relink without the workload | T14 (build-time only, see RESULTS.md) |
| Which of BRUTAL's 45 distinct wasm32 Vulkan call signatures can be called from interpreted C# | P01–P45 (generated), P46–P49 |

Every test prints one line, `TEST <id> <name>: PASS <details>` or `TEST <id> <name>: FAIL <Type>: <Message> | inner ...`,
to the browser console. Some failures are fatal WebAssembly traps, which kill the runtime (they are not .NET exceptions).
To handle that, the C# harness prints `BEGIN <id>` before each test. The driver then:

1. re-runs the page and skips the tests that crashed,
2. re-runs each crashed test alone (`?tests=<id>`) to capture its own output.

## Layout

```
WasmSmoke/                 the app (scaffolded with `dotnet new wasmbrowser`, then modified)
  WasmSmoke.csproj         + BRUTAL PackageReferences
  Program.cs               harness + tests T01..T13 (C#)
  SigProbes.g.cs           generated: one probe per distinct wasm32 signature of BRUTAL's 814 Vulkan fnptr structs
  ExtraProbes.cs           P46..P49 follow-up probes (i64 / 64-bit enum / float)
  wwwroot/main.js          host: JS imports, addFunction trampolines, runMain, rAF tick loop (T12), T13e
  wwwroot/index.html
test/                      headless driver (puppeteer-core 25.12.0, project-local) + captured console logs
  run-smoke.mjs            multi-pass / crash-isolating driver, --probe-sigs mode, --query mode
  serve.mjs                tiny static server with correct MIME types (application/wasm)
  console*.log             raw captured output of the runs reported in RESULTS.md
tools/
  vk_sigs.py               computes the wasm32 signature of every BRUTAL Vulkan fnptr struct
  gen_sig_probes.py        generates WasmSmoke/SigProbes.g.cs
out/release-verbose/       (generated) Release publish with full exception messages
```

## Prerequisites

- .NET SDK 10.0.100 (no `wasm-tools` workload needed or used).
- Node 24 + npm (for the driver only).
- A Chromium-based browser. Google Chrome is not installed on this machine, so the driver defaults to Playwright's
  cached *Chrome for Testing* 151:
  `~/Library/Caches/ms-playwright/chromium-1234/chrome-mac-arm64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing`.
  Override it with `--browser <exe>` or `CHROME_PATH`. Microsoft Edge 155 was also used as a cross-check.

## Build

```bash
cd WasmSmoke
dotnet build -c Release                       # dev layout in bin/Release/net10.0/wwwroot/_framework (served via static web assets)
dotnet publish -c Release                     # self-contained static site: bin/Release/net10.0/publish/wwwroot
# Same, but keep full exception messages (the default trimmed publish strips them to resource keys).
# The separate IntermediateOutputPath is required: otherwise ILLink's incremental output is reused and the flag has no effect.
dotnet publish -c Release -p:UseSystemResourceKeys=false \
  -p:IntermediateOutputPath=obj/ReleaseVerbose/ -p:OutputPath=bin/ReleaseVerbose/ -o ../out/release-verbose
```

Regenerate the signature probes after a BRUTAL upgrade:

```bash
python3 -I tools/gen_sig_probes.py /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan WasmSmoke/SigProbes.g.cs
```

## Run

Interactive: `cd WasmSmoke && dotnet run -c Release`. This starts `WasmAppHost --use-staticwebassets` at
`http://localhost:5016/` (and `https://localhost:7136/`). Open it and read the console; append `?tests=T01,T03` or
`?skip=T07` to select tests.

Headless, from `test/`:

```bash
npm install                                    # project-local puppeteer-core (no browser download)
node run-smoke.mjs                             # default Release publish -> console.log (all passes + isolation runs)
node run-smoke.mjs --dir ../out/release-verbose/wwwroot --out console.release-verbose.log
node run-smoke.mjs --dir ../out/release-verbose/wwwroot --probe-sigs --out console.sigprobes.log --timeout 30000
node run-smoke.mjs --dir ../out/release-verbose/wwwroot --query "tests=P47" --out console.p47.log
node run-smoke.mjs --dir ../out/release-verbose/wwwroot --browser "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge" \
  --query "skip=T07,T08,T08b,T09,T09b,T10,T13b" --out console.edge.log
```

The driver serves the folder on a random `127.0.0.1` port and launches the browser with `--headless=new`.
It collects every console message, page error and failed request until `TICKS DONE` (or a crash, or the timeout),
then writes the log file.
