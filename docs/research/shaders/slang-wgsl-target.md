---
type: Technology Assessment
title: Slang as shader source for both Vulkan and the web (slangc -target wgsl, bindings, push constants, wasm)
description: Hands-on results for slangc 2025.17.2 and 2026.19 targeting SPIR-V and WGSL from one Slang source, including binding parity, ParameterBlock groups, the invalid push-constant WGSL, combined-sampler splitting, Y-flip behaviour, version bugs, reflection JSON, and the Slang wasm build.
tags: [shaders, slang, wgsl, spirv, push-constants, bindings, reflection, wasm]
sources:
  - id: slang-wgsl-doc
    resource: https://docs.shader-slang.org/en/latest/external/slang/docs/user-guide/a2-03-wgsl-target-specific.html
    title: Slang user guide, WGSL-specific functionalities (explicit binding, spec constants, unsupported features)
  - id: slang-wgsl-doc-mirror
    resource: https://shader-slanggithubio-aidanfnv-wip.readthedocs.io/en/latest/external/slang/docs/user-guide/a2-03-wgsl-target-specific.html
    title: Mirror of the Slang WGSL-specific page (address-space mapping, ParameterBlock groups, system values)
  - id: slang-release
    resource: https://github.com/shader-slang/slang/releases/tag/v2026.19
    title: "Slang v2026.19 release (2026-09-29): assets incl. slang-2026.19-wasm.zip (10.17 MB zipped), macOS aarch64 build used here"
  - id: slang-wasm-dts
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r3/dl/slang-wasm/x/interface.d.ts
    title: slang-wasm interface.d.ts extracted from slang-2026.19-wasm.zip (GlobalSession.createSession, getTargetCode, reflection classes)
  - id: slang-playground
    resource: https://github.com/shader-slang/slang-playground
    title: shader-slang/slang-playground repository ("Web application allowing you to try out Slang in your browser")
  - id: slang-loader
    resource: https://github.com/AbdBarho/slang-loader
    title: slang-loader (community bundler loader that compiles Slang to WGSL at build time)
  - id: slangc-help
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/logs/05-slang.txt
    title: exp-02 step 05 log (every slangc command and result, 2025.17.2 and 2026.19)
  - id: exp02-shaders
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/shaders
    title: exp-02 shader sources (mesh.slang, mesh_push.slang, mesh_push_portable.slang, probe_*.slang)
  - id: exp02-browser
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/browser-check.result.json
    title: exp-02 Tint results (headless Edge 155)
  - id: brutal-slang-enums
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Slang/Generated/Enumerations.gen.cs
    title: Brutal.Slang CompileTarget enum (Wgsl = 28, WgslSpirv = 30)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:38:44Z }
status: draft
---

# Verdict

`slangc -target wgsl` **works locally** and produces WGSL that a real browser accepts (Tint in Edge 155) for ordinary vertex/fragment shaders. With explicit `[[vk::binding(b, s)]]` the SPIR-V and WGSL targets get **identical `(set, binding)` numbers**. But the same Slang source is not fully portable across the two targets without care: push constants emit invalid WGSL, `Sampler2D` changes the binding count, clip-space Y is not flipped in WGSL, and **the locally installed slangc 2025.17.2 has two WGSL bugs that 2026.19 fixes**. Recommended use for the PoC: author in GLSL (or Slang) but let **one SPIR-V** (Slang `-target spirv -profile spirv_1_3`) flow through naga; use `-target wgsl` only as a cross-check. See [build-time vs runtime](/shaders/build-time-vs-runtime-cross-compilation.md).

# Commands that worked (exact)

```
slangc shaders/mesh.slang -target spirv -profile spirv_1_5 -entry vsMain -stage vertex -entry fsMain -stage fragment -o out/slang/mesh.slang.spv
slangc shaders/mesh.slang -target wgsl                      -entry vsMain -stage vertex -entry fsMain -stage fragment -o out/slang/mesh.slang.wgsl
```

(Sources: `shaders/mesh.slang`, `mesh_push.slang`, `mesh_push_portable.slang`, `probe_*.slang`.[^exp02-shaders] Entry attributes `[shader("vertex")]` are in the source; the explicit `-entry/-stage` flags are optional.) Both entry points land in **one** module: one `.spv` with two `OpEntryPoint`s (`vsMain`, `fsMain`), one `.wgsl` with `@vertex fn vsMain` and `@fragment fn fsMain`.[^slangc-help] Versions: `/usr/local/bin/slangc` is **2025.17.2**; **2026.19** (release 2026-09-29, macOS aarch64) was downloaded to a scratch directory to compare.[^slang-release]

# Binding and interface conventions (observed)

| Feature | SPIR-V target | WGSL target | Parity |
|---|---|---|---|
| `[[vk::binding(b, s)]] ConstantBuffer<T> u` | `DescriptorSet s`, `Binding b`, uniform block | `@binding(b) @group(s) var<uniform> u : T_std140_0` | identical numbers |
| `Texture2D`, `SamplerState` with explicit `[[vk::binding]]` | separate image and sampler variables | `texture_2d<f32>` and `sampler` at the same numbers | identical (mesh.slang: 0 UBO, 1 texture, 2 sampler) |
| `register(b0, space0)` without `[[vk::binding]]` | binding = register index (warning 39029 asks for `[[vk::binding]]` or `-fvk-*-shift`) | same numbers | identical |
| `ParameterBlock<T>` | its own `DescriptorSet` (1 in the probe); inside: uniform at 0, then texture 1, sampler 2 | its own `@group` (1), same inner numbers | identical |
| `[[vk::location(n)]]` on varyings | `Location n` | `@location(n)` | identical (2026.19: also when `SV_Position` is the first member; see bugs) |
| `[vk::constant_id(7)] const int k` | `OpSpecConstant`, `SpecId 7` | `@id(7) override kMode_0 : i32 = i32(2);` | identical id |
| `-fvk-b/t/s-shift N set` | shifts bindings (e.g. 10, 21, 32) | 2026.19: honoured (`@binding(10/21/32)`); **2025.17.2: resources lose `@binding/@group`, invalid WGSL** | 2026 only |
| `-fvk-use-entrypoint-name` | single-entry module otherwise renames the entry to `main`; flag keeps `vsMain` | WGSL always keeps source names | n/a |
| Matrix storage | default is column-major with std140 `_MatrixStorage_float4x4_ColMajorstd140` wrappers (array of 4 `vec4`), identical to `-matrix-layout-column-major`; matches GLSL `mat4` memory | same wrapper structs with `@align(16)`; multiplication order is reversed in the emitted WGSL so results are preserved | CPU data layout identical to GLSL |
| Entry I/O | struct inputs/outputs flattened | inputs packed into a `vertexInput_0` / `pixelInput_0` struct; `SV_Target` becomes `@location(0)` | fine |
| `SV_VertexID` / `SV_InstanceID` | SPIR-V gains `OpCapability DrawParameters` (VertexIndex minus BaseVertex), **naga rejects** (`UnsupportedCapability(DrawParameters)`) | `@builtin(vertex_index / instance_index)` | use `SV_VulkanVertexID` / `SV_VulkanInstanceID`: no DrawParameters, naga OK |

The Slang documentation confirms the mapping: `[vk::binding(index, set)]` becomes `@binding(index) @group(set)`, `register` is used when absent, `[vk::location(N)]` is honoured, `[SpecializationConstant]`/`[vk::constant_id]` become `@id(N) override`, `ConstantBuffer` maps to the `uniform` address space and **each `ParameterBlock` gets its own bind group**.[^slang-wgsl-doc][^slang-wgsl-doc-mirror]

# Where one source diverges between targets

1. **Push constants.** `[[vk::push_constant]] ConstantBuffer<Push> pc;` is correct in SPIR-V (`PushConstant` storage class). For WGSL slangc 2025.17.2 **and 2026.19** emit `var<uniform> pc_0 : Push_std430_0;` with **no `@group/@binding`**, which is invalid (naga: `Binding decoration is missing or not applicable`). The Slang WGSL page does not cover push constants.[^slang-wgsl-doc] Portable pattern (validated by naga and by Tint on 2026.19):

   ```slang
   #ifdef TARGET_WGSL
   [[vk::binding(0, 3)]] ConstantBuffer<Push> pc;     // reserved group 3 = emulated push constants
   #else
   [[vk::push_constant]] ConstantBuffer<Push> pc;
   #endif
   ```
   compiled with `-DTARGET_WGSL -target wgsl`. The convention matches [the layer contract](/shaders/wgsl-conventions-for-vulkan-layer.md). Note the WGSL struct is `std140` (4 B array strides would be padded), the SPIR-V one is `std430`; for vec4/mat4-only blocks the byte layout is the same.
2. **`Sampler2D` (combined).** SPIR-V keeps a single combined binding (b). WGSL splits it into `tex_texture_0` at b and `tex_sampler_0` at the **next free binding** (b+1 or whatever Slang allocates), so the two targets disagree on binding counts and the sampler may collide with an explicit binding. naga cannot read the combined form either. Use separate `Texture2D` + `SamplerState` with explicit bindings.
3. **Clip-space Y.** Slang's WGSL output has **no Y flip** (the shader is emitted for WGSL's Y-up NDC); `-fvk-invert-y` negates `SV_Position.y` in the **SPIR-V** target only (WGSL output was byte-identical with and without the flag). A Vulkan-convention Slang shader compiled to WGSL directly therefore renders upside down unless `patch-wgsl.mjs --y-flip` wraps the vertex entry (verified on a GPU, see [conventions](/shaders/wgsl-conventions-for-vulkan-layer.md)). The SPIR-V target for desktop Vulkan must **not** use `-fvk-invert-y` if the SPIR-V is later flipped by naga (double flip).
4. **Output size and shape.** The direct WGSL is verbose (`i32(0)` indices, per-element matrix reconstruction, 3.4 KB for the PoC pair); the naga route from the same Slang SPIR-V gives 2.4 to 3.5 KB. No functional difference observed.

# Bugs in the locally installed 2025.17.2 (fixed in 2026.19)

| Bug | Symptom | Evidence |
|---|---|---|
| Missing `@location` on vertex-output members when `SV_Position` is the first member of the struct | WGSL: `uv_0 : vec2<f32>` without attribute; Tint and naga reject (`missing entry point IO attribute` / `Struct member 1 is missing a binding`) even with explicit `[[vk::location]]`; the fragment input struct still gets locations | `probe_registers`, `mesh_push_portable` in the step 05 log and the browser result[^slangc-help][^exp02-browser] |
| `-fvk-b/t/s-shift` | WGSL resources lose `@binding(...) @group(...)` entirely | `probe_shift` |

Recommendation: pin Slang **>= 2026.19** for any WGSL use. Brutal.Slang is generated against a local `slang` library and already exposes `CompileTarget.Wgsl = 28` and `WgslSpirv = 30`, so a BRUTAL desktop tool can drive the same compiler in-process.[^brutal-slang-enums]

# Reflection

`-reflection-json out.json` works: it lists each parameter with `binding: { kind: "descriptorTableSlot", index: N }`, type kind (`constantBuffer`, `resource`, `samplerState`), struct fields with `offset`/`size`, and per-entry-point `varyingInput` counts (14.7 KB for the PoC with 2025.17.2, 33.9 KB with 2026.19).[^slangc-help] That is enough to generate bind-group layouts and vertex layouts at build time. See [other routes and reflection](/shaders/glsl-direct-routes-spirv-cross-and-reflection.md).

# Slang in the browser

- The v2026.19 release publishes `slang-2026.19-wasm.zip` (10.17 MB zipped); it contains `slang-wasm.js` (154 KB), `slang-wasm.wasm` (**24,518,850 bytes**) and `interface.d.ts` with `GlobalSession.createSession`, `getTargetCode`, `getEntryPointCode` and the reflection classes.[^slang-release][^slang-wasm-dts]
- There is **no npm package** under `@shader-slang/slang-wasm` or `slang-wasm` (both 404 from `npm view`); the playground repository is the consumer and the community `slang-loader` compiles Slang to WGSL at build time.[^slang-playground][^slang-loader]
- Shipping a 24.5 MB wasm for runtime compilation is far heavier than naga-wasm (2.05 MB), so Slang belongs **in the build pipeline**, not in the page.

# Compared with GLSL as the PoC language

See the decision table in [build-time vs runtime](/shaders/build-time-vs-runtime-cross-compilation.md). In short: Slang gives true multi-target output and reflection but forces the shader team to learn it and to avoid the divergence points above; GLSL matches BRUTAL/KSA today. For a hand-written PoC either works; for the KSA reference shaders GLSL is the only source that exists.

## Open questions

- UNVERIFIED: whether Slang's `-emit-spirv-via-glsl`, `-target wgsl-spirv` or the Tint pass-through (`slang-tint`, win-x64 only in Brutal.Slang) change any of the above; not run.
- UNVERIFIED: Slang's handling of bindless (`DescriptorHandle`, unbounded arrays) for WGSL; the user guide lists only a subset of system values and no bindless, so assume unsupported.
- UNVERIFIED: whether Slang 2026.x fixes the push-constant WGSL (it did not in 2026.19).
- UNVERIFIED: how Slang's multi-target single invocation (`-target spirv -o a.spv -target wgsl -o a.wgsl`) behaves with per-target `-D`; two invocations were used.

[^slang-wgsl-doc]: Slang user guide, WGSL-specific functionalities (explicit binding, spec constants, unsupported features)
[^slang-wgsl-doc-mirror]: Mirror of the Slang WGSL-specific page (address-space mapping, ParameterBlock groups, system values)
[^slang-release]: Slang v2026.19 release (2026-09-29): assets incl. slang-2026.19-wasm.zip (10.17 MB zipped), macOS aarch64 build used here
[^slang-wasm-dts]: slang-wasm interface.d.ts extracted from slang-2026.19-wasm.zip (GlobalSession.createSession, getTargetCode, reflection classes)
[^slang-playground]: shader-slang/slang-playground repository
[^slang-loader]: slang-loader (community bundler loader that compiles Slang to WGSL at build time)
[^exp02-shaders]: exp-02 shader sources (mesh.slang, mesh_push.slang, mesh_push_portable.slang, probe_*.slang)
[^slangc-help]: exp-02 step 05 log (every slangc command and result, 2025.17.2 and 2026.19)
[^exp02-browser]: exp-02 Tint results (headless Edge 155)
[^brutal-slang-enums]: Brutal.Slang CompileTarget enum (Wgsl = 28, WgslSpirv = 30)
