# exp-02-shader-xcompile

Experiments for the BRUTAL-in-the-browser PoC: **how do Vulkan shaders (GLSL via shaderc, or Slang) become WGSL**, which is the only shader language WebGPU accepts. The write-up lives in `../../docs/research/shaders/` (start with `experiment-results-exp-02.md`).

## Reproduce

```sh
cd projects/exp-02-shader-xcompile
npm install                       # naga-wasm 30.2.0 (project-local, no global installs)
./run.sh                          # core experiments; logs in out/logs/NN-*.txt, artifacts in out/
```

Optional environment (all outside this folder, nothing global):

| Variable | Meaning |
|---|---|
| `NAGA_CLI=<path>` | naga CLI (`cargo install naga-cli --root <dir>`), enables CLI parity and the un-flipped Y variant |
| `SWT=<path>` | `spv_webgpu_transform` (`cargo install spirv-webgpu-transform --root <dir>`) |
| `SLANGC26=<path>` | slangc 2026.19 from the GitHub release (the installed `/usr/local/bin/slangc` is 2025.17.2) |
| `BROWSER_CHECK=1` | also run headless Edge/Chrome: Tint compiles every WGSL, render/winding test, pipeline-layout tests, std140 GPU probe, naga-wasm inside the browser (`BROWSER=` overrides the binary; needs Chromium >= 149 for `var<immediate>`) |
| `KSA=1` | also survey the KSA reference shaders (read-only; outputs go to `$KSA_OUT`, default `$TMPDIR/exp-02-ksa`; only per-shader status summaries are copied into `out/`) |
| `VULKAN_SDK_BIN` | directory with `glslc`, `spirv-opt`, `spirv-val`, `spirv-dis`, `spirv-cross` (default `~/VulkanSDK/1.4.328.1/macOS/bin`) |

Tested on macOS (arm64), Node 24.18, glslc (shaderc/SDK 1.4.328), naga 30.0.1, Microsoft Edge 155.

## Layout

| Path | Content |
|---|---|
| `shaders/` | `mesh.vert`, `mesh.frag` (the PoC pair), `mesh_combined.frag`, `mesh_push.{vert,frag}`, `mesh_push_array.vert`, `tri.vert`, Slang sources (`mesh.slang`, `mesh_push.slang`, `mesh_push_portable.slang`, `probe_*.slang`), `probes/*` (feature probes) |
| `src/spv2wgsl.mjs` | SPIR-V to WGSL with naga-wasm (timings, formatted errors) |
| `src/spv-prepare.mjs` | SPIR-V pre-pass: split combined image samplers (+ sampler binding offset 500), strip `NonReadable` on SSBO members |
| `src/spv-layout-lint.mjs` | std140 stride hazards (naga ignores `ArrayStride`/`MatrixStride`) |
| `src/patch-wgsl.mjs` | WGSL patches: Y-flip wrapper, push constants to uniform/storage/immediate, module directives, binding reflection |
| `src/build-shaders.mjs` | prototype build pipeline: spv + wgsl + `manifest.json` keyed by sha256 of the SPIR-V |
| `src/validate-wgsl.mjs`, `src/glsl2wgsl.mjs`, `src/bench*.mjs`, `src/pushsize.mjs` | helpers |
| `src/browser-check*.mjs`, `src/browser-naga.mjs`, `run-browser-*.sh` | headless-browser validation (Tint) and naga-wasm-in-browser |
| `ksa-survey.sh`, `ksa-named.sh`, `run-ksa-browser-check.sh`, `run-probes.sh` | KSA gap characterisation, feature probes |
| `out/` | generated artifacts: `logs/`, `wasm/` (naga-wasm WGSL), `cli/`, `slang/`, `slang26/`, `patched/`, `probes/`, `tri/`, `web-shaders/`, `browser-*.result.json`, `ksa-*` summaries |

## Headline results

See `out/logs/` and the research notes. In short: naga-wasm translates the PoC shaders and Tint accepts the output; naga flips Y by default; `sampler2D` and `layout(push_constant)` need handling (pre-pass, WGSL patch); the installed slangc 2025.17.2 emits invalid WGSL in two cases fixed by 2026.19.
