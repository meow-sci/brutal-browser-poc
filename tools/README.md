# tools/

Build-time tooling for brutal-browser. Today: the **shader cross-compilation pipeline**, so that the Vulkan GLSL shaders a game compiles for desktop are also available as WGSL in the browser.

```
GLSL 450 --glslc (vulkan1.1, -O)--> <name>.<ext>.spv --+--> embedded as Shaders/<name>.<ext>.spv   (desktop + browser, byte-identical)
          (tools/Shaders.targets)                      |
                                                       +--> build-shaders.mjs --> <name>.wgsl + manifest.json
                                                            (tools/Shaders.Web.targets, browser builds)   embedded as Shaders/manifest.json
```

The browser ICD hashes the SPIR-V bytes it receives in `vkCreateShaderModule` (SHA-256) and looks the WGSL up in the manifest, so the manifest key is **always the hash of the original `.spv`** that glslc wrote. Manifest format: `docs/architecture.md` section 6 (normative).

## Pipeline (`build-shaders.mjs`, per `.spv`)

1. `spirv-val --target-env vulkan1.1` on the original bytes; exactly one entry point (vertex / fragment / compute).
2. `spv-prepare.mjs`: if the module has combined image samplers (`sampler2D`), `spirv-opt --split-combined-image-sampler` and move each new sampler to **binding + 500**; strip `NonReadable` from storage-buffer blocks (`writeonly buffer`). Modules that need neither pass through byte-for-byte.
3. `spv-layout-lint.mjs`: **error** on std140 arrays of scalars / vec2, on matrices with 2-row columns (naga ignores `ArrayStride` / `MatrixStride`), and on 64-bit types.
4. naga-wasm: SPIR-V to WGSL. Y is flipped (naga's default), so `frontFace` / `cullMode` stay as the application sets them.
5. `patch-wgsl.mjs`: push constants (`var<immediate>` / `var<push_constant>`) to `@group(3) @binding(0) var<uniform>`, or `var<storage, read>` if the block only validates as std430 (e.g. `float[4]`); `diagnostic(off, derivative_uniformity);`; `enable subgroups;` when subgroup functions appear; strip `@coherent`.
6. naga re-parses and validates the final WGSL, then bindings / entry point / overrides are read from it.
7. Write `<out>/<name>.wgsl` and `<out>/manifest.json`. Any failing shader makes the run exit 1 (message names the shader and the step), and no manifest is left behind.
8. Unless `--no-browser-check`: `check-wgsl.mjs` compiles every module with the browser's real WGSL compiler (Tint).

## Conventions the shaders must follow

| Topic | Rule |
| --- | --- |
| Mapping | descriptor set = `@group`, binding = `@binding`, `location` = `@location`; no renumbering (except below) |
| Combined samplers | `sampler2D` at binding `b` becomes texture `b` + sampler `b + 500` (needs `b < 500`). Prefer separate `texture2D` + `sampler` |
| Push constants | block of at most **128 bytes** (the ICD's `maxPushConstantsSize`); becomes a uniform (or storage) buffer at group **3**, binding 0, so set 3 is reserved and a pipeline layout has at most 3 descriptor sets. `push.mode` is `uniform` or `storage`; `push.size` is the block size rounded up to 16 (the minimum WGSL binding size) |
| std140 UBOs | author arrays as `vec4[N]`; no `mat2` / `matCxR` with R = 2; no `double` / 64-bit ints |
| Not supported | `gl_PointSize` writes, binding arrays / bindless, buffer device address, `OpSpecConstantOp` (use spec constants directly), more than one entry point per file |
| Spec constants | `layout(constant_id = N) const T X` becomes `override`; manifest `overrides` is `{ "<N>": { "name": "<wgsl name>", "type": "f32" } }` (pipeline `constants` key = `"N"`) |
| Names | binding `name` is the WGSL identifier; glslc `-O` strips debug names, so expect `global`, `global_1`, ... |

## Usage

```sh
cd tools && npm ci                       # naga-wasm 30.2.0 (+ playwright 1.64.1 for the browser check); project-local, nothing global
node build-shaders.mjs --spv-dir <dir with *.spv> --out <dir> [--no-browser-check] [--glslc <path>] [--target-env vulkan1.1]
```

Prints `OK <name> sha256=<8> bindings=<n> push=<mode>` per shader (every `*.spv` in `--spv-dir`). `--glslc` is only used to record `glslc --version` in `generator.glslc`. Native tools (`glslc`, `spirv-val`, `spirv-opt`) are found via `PATH`, `$GLSLC` / `$SPIRV_VAL` / `$SPIRV_OPT`, `$VULKAN_SDK`, `~/VulkanSDK/*/macOS/bin` or `/usr/local/bin`.

The helpers also run on their own: `node spv-prepare.mjs in.spv out.spv`, `node spv-layout-lint.mjs a.spv ...` (exit 3 on a hazard), `node patch-wgsl.mjs in.wgsl out.wgsl --push storage --directives`, `node check-wgsl.mjs manifest.json`.

## MSBuild

A game project declares its shaders and imports `tools/Shaders.targets` (see the header of that file). For browser builds (`RuntimeIdentifier=browser-wasm`, or `-p:ShaderWebPipeline=true`) `Shaders.targets` imports `Shaders.Web.targets`, which adds:

- `EnsureToolsNodeModules`: when `tools/node_modules/naga-wasm` is missing, runs `ensure-node-modules.mjs`, which runs `npm ci --prefer-offline` in `tools/` under a lock file (`tools/.npm-ci.lock`, created exclusively, removed afterwards; a lock older than 10 minutes counts as stale). Several browser projects building in parallel (`dotnet build BrutalBrowser.slnx`) therefore install once; the others wait for the lock and then find `node_modules`.
- `TranslateWebShaders` (after `CompileShaders`, before `PrepareForBuild`; incremental on the `.spv` files and the scripts): runs `build-shaders.mjs ... --no-browser-check` into `$(ShaderWebOutputDir)`.
- an `EmbeddedResource` `Shaders/manifest.json` for `$(ShaderWebOutputDir)manifest.json`.

Optional properties: `ShaderToolsDir`, `ShaderNodeExe`, `ShaderNpmExe`, plus those of `Shaders.targets` (`GlslcPath`, `ShaderTargetEnv`, `ShaderIntermediateDir`, `ShaderWebOutputDir`). The `.spv` files default to `obj/<Configuration>/<TargetFramework>/shaders/`; that folder belongs to one project: `Shaders.targets` (`RemoveStaleShaders`, before `CompileShaders`) deletes every `*.spv` in it that no `ShaderSource` produces, and then the manifest, so a removed or renamed shader leaves no stale manifest entry.

## Adding a shader

1. Write Vulkan GLSL 450 following the conventions above and add it to the project's `<ShaderSource Include="..." />` (`.vert`, `.frag`, `.comp`).
2. Build the browser project. A shader the pipeline cannot translate fails the build with `FAIL <name>: <step>: <reason>`.
3. Optionally run `node build-shaders.mjs` by hand on `obj/.../shaders` to see the per-shader lines and the browser check.

## Tests

```sh
npm test                    # compiles test-fixtures/*.{vert,frag,comp} with glslc, runs the pipeline incl. the headless-browser check
SKIP_BROWSER=1 npm test     # same without the browser check
```

`test-fixtures/fixtures.sh` asserts: the valid fixtures translate; manifest keys equal `shasum -a 256` of the `.spv`; `mesh.frag` keeps texture@1 / sampler@2; `combined.frag` yields texture@1 + sampler@501; `push.vert` is `uniform` at group 3 (64 bytes) and `push_array.vert` falls back to `storage`; every `push.mode` is `none`, `uniform` or `storage`; `bad_layout.frag` (std140 `float[4]`) and `bad_push.vert` (144 bytes, over the 128-byte limit) are rejected; `check-wgsl.mjs` fails on broken WGSL.

`check-wgsl.mjs` uses Playwright's Chromium (`chromium.launch({ headless: true })`). If that exact revision is not installed it falls back to the newest Chromium in Playwright's cache, then to installed Chrome / Edge (first one with a WebGPU adapter wins; nothing is downloaded). `--channel chrome|msedge` / `--executable <path>` pin one.

## Limitations

- Combined samplers: binding `< 500`, arrays of combined samplers are untested.
- Push constants: at most 128 bytes (the constant `PUSH_MAX_BYTES` in `build-shaders.mjs`, equal to the ICD's `maxPushConstantsSize`); the pipeline emits `uniform` or `storage` only, never WebGPU immediates (`var<immediate>`).
- std140: only scalar / vec2 arrays and 2-row matrices are linted; arrays of structs and nested-struct padding are not verified. Storage buffers follow std430 as written.
- The manifest only works if the app passes **the build's** `.spv` bytes (same glslc, flags and macros).
- `CompileShaders` is incremental on the `ShaderSource` files only: after editing a file that a shader `#include`s, touch the shader (or rebuild).
- naga's WGSL parser does not know `enable subgroups;`; the final re-parse drops that line and Tint (`check-wgsl.mjs`) is the judge.
