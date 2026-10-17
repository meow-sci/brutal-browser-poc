# exp-03: C# → WebGPU via `[JSImport]` (.NET 10 browser-wasm)

A minimal C# WebGPU binding prototype. A spinning triangle is drawn by C# code through `[JSImport]` calls
into a JS handle table (`webgpu-glue.js`). The same frame is also drawn through a binary command stream that is
replayed with one interop call. Benchmarks measure interop costs to inform the design of a C# "Vulkan-on-WebGPU"
layer. The report is `docs/research/experiments/exp-03-webgpu-jsimport.md`, and the raw numbers are in `RESULTS.md`.

The project runs in **interpreter mode**: the `wasm-tools` workload is not installed, so there is no AOT. The
jiterpreter is on by default. One run turns it off with `?nojiterp=1`.

## Layout

| Path | What |
|---|---|
| `WebGpuTriangle/WebGpuTriangle.csproj` | `wasmbrowser` template (`Microsoft.NET.Sdk.WebAssembly`, net10.0, `AllowUnsafeBlocks`) |
| `WebGpuTriangle/WebGpu.cs` | `[JSImport(..., "webgpu")]` declarations, enums (format, buffer usage, load/store ops) |
| `WebGpuTriangle/CommandStream.cs` | Binary command stream in `NativeMemory` (u32 opcodes + args) |
| `WebGpuTriangle/Program.cs` | `App` `[JSExport]`s: `Init`, `Frame`, `FrameStream`, `FrameManyDraws`, benchmarks |
| `WebGpuTriangle/wwwroot/webgpu-glue.js` | Handle table, heap reads, pipeline-JSON resolver, `replayCommandStream` |
| `WebGpuTriangle/wwwroot/main.js` | WebGPU init (before .NET), frame loops, pixel checks, benchmark driver |
| `test/serve.mjs` | Static server with no dependencies (`application/wasm`, COOP/COEP for a 5 µs `performance.now()`) |
| `test/run.mjs` | puppeteer-core driver: screenshot, pixel checks, console capture, `results.json` |
| `test/probe/` | Matrix of headless WebGPU flags (`probe.mjs` + `index.html`) |
| `test/summarize.mjs` | Builds a Markdown table from one or more `results.json` files |
| `test/out/` | Primary run (Edge) output: `console.log`, `triangle.png`, `triangle-canvas.png`, `many-draws.png`, `results.json`, `summary.md`, `probe-results.json`; `extra/<config>/` holds the other runs |

## Build

```sh
cd WebGpuTriangle
dotnet publish -c Release          # ~7 s; output: bin/Release/net10.0/publish/wwwroot (12 MB incl. .gz/.br, 7.0 MB raw)
```

`dotnet publish` prints `Publishing without optimizations ... we strongly recommend using wasm-tools workload`. The
warning is expected and harmless here. JS files are fingerprinted (`main.<hash>.js`, `webgpu-glue.<hash>.js`), and the
generated import map in `index.html` resolves `./webgpu-glue.js` to its hashed file. Serve the **publish**
`wwwroot` folder, not the build output.

## Serve manually

```sh
node test/serve.mjs                              # serves publish/wwwroot at http://localhost:8083/
# open http://localhost:8083/index.html   (add ?frames=N, ?nojiterp=1, ?gate=1)
```

## Test (headless)

```sh
cd test
npm install                                      # project-local puppeteer-core only; no browser download
node run.mjs                                     # Edge 155, --headless=new, no extra flags  -> out/
node run.mjs --browser cft --out out/extra/cft   # Chrome for Testing 151 (Playwright cache)
node run.mjs --browser edge --query nojiterp=1 --out out/extra/edge-nojiterp
node run.mjs --browser edge --out out/extra/edge-swiftshader \
  --flags "--enable-unsafe-webgpu --use-webgpu-adapter=swiftshader --enable-unsafe-swiftshader"
node run.mjs --browser shell --flags "--use-angle=metal" --out out/extra/shell-angle-metal
node probe/probe.mjs                             # flag matrix -> out/probe-results.json
node summarize.mjs "Edge=out/results.json" "CfT=out/extra/cft/results.json" > out/summary.md
```

`run.mjs` exits with 0 only when the in-page `PIXEL CHECK PASS` **and** the screenshot pixel check both pass.
Set `E3_SCRATCH=<dir>` to put the temporary Chromium profiles somewhere other than `os.tmpdir()`.

## Flags that worked (macOS 27, Apple M4 Pro)

| Browser | Mode | Flags | Adapter |
|---|---|---|---|
| Microsoft Edge 155.0.4283.45 | `--headless=new` | none | `apple` / `metal-3` (hardware) |
| Chrome for Testing 151.0.7922.34 | `--headless=new` | none | `apple` / `metal-3` |
| chrome-headless-shell 151 | `--headless` (old) | none | **no adapter** (`requestAdapter()` → null) |
| chrome-headless-shell 151 | `--headless` | `--enable-unsafe-webgpu` | `google` / `swiftshader` (fallback) |
| chrome-headless-shell 151 | `--headless` | `--use-angle=metal` | `apple` / `metal-3` |
| any of the above | any | `--enable-unsafe-webgpu --use-webgpu-adapter=swiftshader --enable-unsafe-swiftshader` | `google` / `swiftshader` (fallback) |

puppeteer-core 25.14 `headless: true` passes `--headless=new`, and `headless: 'shell'` passes `--headless`. puppeteer
also adds `--force-color-profile=srgb`.

## Console markers

`FRAMES DONE`, `PIXEL CHECK PASS|FAIL`, `GATE WAITING` (only with `?gate`), `STREAM FRAMES DONE`,
`STREAM PIXEL CHECK PASS|FAIL`, `BENCH <name> ...`, `BENCH_JSON {...}`, `ALL DONE`, `FATAL ...`, `WEBGPU ERROR ...`.
The only non-benchmark noise is a 404 for `/favicon.ico` and Canvas2D `willReadFrequently` warnings from the
pixel decoders. Both are harmless.
