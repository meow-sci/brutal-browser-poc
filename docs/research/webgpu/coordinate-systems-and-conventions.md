---
type: Research Note
title: Coordinate systems and conventions — Vulkan vs WebGPU (Y-flip, winding, depth, viewport)
description: Derives, from both specifications, exactly how Vulkan and WebGPU map clip space to the framebuffer, proves that a vertex-shader Y negation reproduces Vulkan's image with an unchanged frontFace, and lists the viewport, depth-range, texture-origin and builtin conventions an ICD must handle.
tags: [webgpu, vulkan, coordinates, y-flip, winding, viewport, depth]
sources:
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026 (§3.3 Coordinate Systems, §23.2.5 Rasterization, setViewport)
  - id: vk-primsrast
    resource: https://docs.vulkan.org/spec/latest/chapters/primsrast.html
    title: Vulkan specification, Rasterization chapter (Basic Polygon Rasterization)
  - id: vk-vertexpostproc
    resource: https://docs.vulkan.org/spec/latest/chapters/vertexpostproc.html
    title: Vulkan specification, Fixed-Function Vertex Post-Processing (viewport transform)
  - id: dawn-cmdbufvk
    resource: https://github.com/google/dawn/blob/main/src/dawn/native/vulkan/CommandBufferVk.cpp
    title: Dawn Vulkan backend CommandBufferVk.cpp (SetViewport with negative height)
  - id: dawn-pipelinevk
    resource: https://github.com/google/dawn/blob/main/src/dawn/native/vulkan/RenderPipelineVk.cpp
    title: Dawn Vulkan backend RenderPipelineVk.cpp (VulkanFrontFace 1:1)
  - id: wgpu-hal
    resource: https://github.com/gfx-rs/wgpu/blob/trunk/wgpu-hal/src/vulkan/conv.rs
    title: wgpu-hal Vulkan conv.rs (map_front_face 1:1)
  - id: naga-spv
    resource: https://github.com/gfx-rs/wgpu/blob/trunk/naga/src/front/spv/mod.rs
    title: Naga SPIR-V frontend Options.adjust_coordinate_space (default true)
  - id: moltenvk-config
    resource: https://github.com/KhronosGroup/MoltenVK/blob/main/Docs/MoltenVK_Configuration_Parameters.md
    title: MoltenVK configuration parameters (MVK_CONFIG_SHADER_CONVERSION_FLIP_VERTEX_Y)
  - id: angle-wgpu
    resource: https://github.com/google/angle/tree/main/src/libANGLE/renderer/wgpu
    title: ANGLE WebGPU backend (manual gl_Position.y flip via driver uniform)
  - id: brutal-imgui-vk
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/RendererBackends/VulkanBackend.cs
    title: BRUTAL ImGui Vulkan backend (positive-height viewport, scale/translate push constants)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# The two pipelines side by side

| Space | Vulkan | WebGPU |
| --- | --- | --- |
| Clip volume | −w ≤ x,y ≤ w, 0 ≤ z ≤ w | −w ≤ x,y ≤ w, 0 ≤ z ≤ w[^webgpu-spec] |
| NDC y direction | **+y points down** (y = −1 is the top row) | **+y points up** (y = +1 is the top row)[^webgpu-spec] |
| Viewport → framebuffer y | y_f = vp.y + ½(y_ndc + 1)·vp.height[^vk-vertexpostproc] | y_f = vp.y + ½(−y_ndc + 1)·vp.height[^webgpu-spec] |
| Framebuffer origin | top-left, y down | top-left, y down[^webgpu-spec] |
| Depth | z_f = minDepth + z_ndc·(maxDepth − minDepth); minDepth may exceed maxDepth | same formula, but **minDepth ≤ maxDepth required**, both in [0,1][^webgpu-spec] |
| Texture coordinates | (0,0) = first texel in memory = top-left | same |
| `gl_FragCoord` / `@builtin(position)` | framebuffer coords, pixel centre at .5, origin top-left | same (`position.xy` in framebuffer space, `.z` depth, `.w` = 1/w)[^webgpu-spec] |
| Polygon orientation | a = −½ Σ (x_i·y_{i+1} − x_{i+1}·y_i) in framebuffer coords; `COUNTER_CLOCKWISE` ⇔ a > 0[^vk-primsrast] | area = ½ Σ (x_{i+1}·y_i − x_i·y_{i+1}) in framebuffer coords; `"ccw"` ⇔ area > 0[^webgpu-spec] |

The two area formulas are algebraically identical (½ Σ (x_{i+1}y_i − x_i y_{i+1}) = −½ Σ (x_i y_{i+1} − x_{i+1} y_i)), and both are evaluated **in framebuffer coordinates**.

# Consequence: flip Y in the vertex shader, keep frontFace as is

1. A Vulkan vertex shader emits y_clip; Vulkan places it at y_f = vp.y + ½(y/w + 1)·h.
2. If the translated shader emits **−y_clip**, WebGPU places it at y_f = vp.y + ½(−(−y/w) + 1)·h = vp.y + ½(y/w + 1)·h — the identical framebuffer position.
3. Since every vertex lands on the same framebuffer pixel, the rendered image is identical, and since both APIs decide facing from the signed area in framebuffer space with the same sign convention, `VK_FRONT_FACE_COUNTER_CLOCKWISE` → `"ccw"` and `CLOCKWISE` → `"cw"` **without inversion**; cull mode maps 1:1.

This corrects a common rule of thumb ("flip Y ⇒ invert winding"). That rule applies when the *image* gets flipped (e.g. porting GL projection maths to Vulkan without changing the viewport); here the flip compensates an API convention and the image is unchanged. Independent confirmation from the inverse layers: Dawn implements WebGPU on Vulkan with `viewport.y = y + height; viewport.height = −height` and maps `CCW → VK_FRONT_FACE_COUNTER_CLOCKWISE` 1:1; wgpu-hal's `map_front_face` is also 1:1, and on drivers that ignore negative viewport heights it moves the flip into the shader (naga) without touching front face.[^dawn-cmdbufvk][^dawn-pipelinevk][^wgpu-hal] MoltenVK (Metal has WebGPU's convention) flips Y in the generated MSL by default (`MVK_CONFIG_SHADER_CONVERSION_FLIP_VERTEX_Y = 1`) and passes `frontFace` straight through.[^moltenvk-config]

Where the flip is implemented:

| Option | How | Cost | Notes |
| --- | --- | --- | --- |
| A. Translator flag | Naga's SPIR-V frontend does it by default (`adjust_coordinate_space = true` negates `BuiltIn::Position.y`)[^naga-spv]; other paths (Tint, SPIRV-Cross→WGSL, Slang) need an explicit pass | zero runtime | applies to the *last* pre-rasterization stage only (vertex in WebGPU) — see `/shaders/` |
| B. Driver uniform | `pos.y *= flipSign` with `flipSign` in an ICD-owned uniform/immediate | 1 multiply + 4 bytes | needed only if the app mixes positive- and negative-height viewports; ANGLE's WebGPU backend does exactly this with a `flipXY` driver uniform[^angle-wgpu] |

Recommendation: **A** for the PoC (always flip at build time), with B kept as the fallback for apps that use negative viewports.

# Viewport and scissor rules the ICD must enforce

| Vulkan usage | WebGPU rule | ICD translation |
| --- | --- | --- |
| Positive height viewport (BRUTAL ImGui backend: `Width/Height = framebuffer size`, `x = y = 0`)[^brutal-imgui-vk] | `0 ≤ width, height ≤ maxTextureDimension2D`; x, y ≥ −2·maxTextureDimension2D[^webgpu-spec] | pass through |
| **Negative height** (`VK_KHR_maintenance1` / core 1.1 "GL-style y-up" trick: y = y0 + H, height = −H) | height must be ≥ 0[^webgpu-spec] | rewrite to (y0, +H) and **disable** the shader flip for draws under this viewport (option B, or a pipeline variant compiled without the flip) — the app asked for y-up NDC, which is WebGPU's native convention |
| Reversed depth range (minDepth = 1, maxDepth = 0) | minDepth ≤ maxDepth required | rewrite to (0,1) and negate z in the vertex shader: z' = w − z (variant); simpler: forbid in the profile and tell apps to reverse via projection matrix + `GREATER` compare |
| Viewport extends outside the framebuffer | allowed within ±2× max dimension | pass through |
| Multiple viewports (`multiViewport`) | one viewport | feature not reported |
| Scissor | `setScissorRect(x,y,w,h)` must lie inside the attachment[^webgpu-spec] | clamp to the render area (Vulkan allows scissors larger than the framebuffer) |
| Viewport/scissor not set inside a pass | WebGPU defaults viewport to the full attachment and depth [0,1] at pass begin | Vulkan requires them to be set when dynamic; replay the last values after every `beginRenderPass` because WebGPU state does not persist across passes |
| `vkCmdSetViewport` before `vkCmdBeginRenderPass` | WebGPU viewport is pass-encoder state | ICD keeps the dynamic state in its own shadow and re-applies on each pass |

# Other conventions that line up (no work needed)

- Depth range [0,1] in both: no `z = (z + w)/2` fix-up (that is only needed for GL-style projection matrices).[^webgpu-spec]
- Texture memory order: row 0 is the top row in both APIs; `copyBufferToTexture` origin (0,0,0) = top-left = Vulkan `imageOffset` (0,0,0).
- `gl_FragCoord` vs `@builtin(position)`: same origin and half-pixel centres; no `origin_upper_left`/`pixel_center_integer` remapping.
- Point sprite coordinates: WebGPU has no `gl_PointCoord`/point size (points are always 1 px) — moot.
- Compute: no coordinate convention involved.
- `gl_VertexIndex` includes `firstVertex`/`vertexOffset` in Vulkan; WGSL `vertex_index` also includes `firstVertex`/`baseVertex`; `gl_InstanceIndex` includes `firstInstance` in Vulkan, and WGSL `instance_index` includes `firstInstance` as well. `gl_BaseVertex`/`gl_BaseInstance`/`gl_DrawID` (`shaderDrawParameters`) have no WGSL builtin (UNVERIFIED that no WGSL language extension added them by Oct 2026).

# Matrices and app-side maths

BRUTAL apps that already target Vulkan build projection matrices for Vulkan's y-down NDC and [0,1] depth. With option A they need **no change**. Apps must *not* additionally "fix" their matrices for WebGPU — doing so double-flips. Shaders that compute screen-space positions from `gl_Position` manually (e.g. `ndc.y * 0.5 + 0.5` for UVs) keep working because the flip is applied only to the final position output, after user code.

# Open questions

- UNVERIFIED: whether the chosen SPIR-V→WGSL tool chain (see `/shaders/`) exposes a reliable Y-flip switch other than Naga's; Tint's SPIR-V reader behaviour on `gl_Position` must be checked.
- UNVERIFIED: whether any WGSL extension exposes base vertex/base instance builtins (needed for `shaderDrawParameters` emulation; otherwise emulate via immediates holding firstInstance per draw).

[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026 (§3.3 Coordinate Systems, §23.2.5 Rasterization, setViewport)
[^vk-primsrast]: Vulkan specification, Rasterization chapter (Basic Polygon Rasterization)
[^vk-vertexpostproc]: Vulkan specification, Fixed-Function Vertex Post-Processing (viewport transform)
[^dawn-cmdbufvk]: Dawn Vulkan backend CommandBufferVk.cpp (SetViewport with negative height)
[^dawn-pipelinevk]: Dawn Vulkan backend RenderPipelineVk.cpp (VulkanFrontFace 1:1)
[^wgpu-hal]: wgpu-hal Vulkan conv.rs (map_front_face 1:1)
[^naga-spv]: Naga SPIR-V frontend Options.adjust_coordinate_space (default true)
[^moltenvk-config]: MoltenVK configuration parameters (MVK_CONFIG_SHADER_CONVERSION_FLIP_VERTEX_Y)
[^angle-wgpu]: ANGLE WebGPU backend (manual gl_Position.y flip via driver uniform)
[^brutal-imgui-vk]: BRUTAL ImGui Vulkan backend (positive-height viewport, scale/translate push constants)
