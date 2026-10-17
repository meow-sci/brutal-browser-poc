---
type: Reference
title: Format tables — VkFormat to GPUTextureFormat / GPUVertexFormat / index and surface formats
description: Translation tables from VkFormat to WebGPU texture, depth-stencil, compressed and vertex formats, with the VkFormatFeatureFlags an ICD should report for each (derived from the WebGPU texture-format capability tables), the Vulkan formats with no WebGPU equivalent and their substitutes, plus index and swapchain formats.
tags: [webgpu, vulkan, formats, vertex-formats, depth, compressed-textures, icd]
sources:
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026 (§26.1 Texture Format Capabilities, §10.3.7.1 Vertex Formats)
  - id: dawn-texturevk
    resource: https://github.com/google/dawn/blob/main/src/dawn/native/vulkan/TextureVk.cpp
    title: Dawn Vulkan backend TextureVk.cpp (VulkanImageFormat — the inverse table)
  - id: dawn-pipelinevk
    resource: https://github.com/google/dawn/blob/main/src/dawn/native/vulkan/RenderPipelineVk.cpp
    title: Dawn Vulkan backend RenderPipelineVk.cpp (VulkanVertexFormat — the inverse table)
  - id: wgpu-hal
    resource: https://github.com/gfx-rs/wgpu/blob/trunk/wgpu-hal/src/vulkan/conv.rs
    title: wgpu-hal Vulkan conv.rs (map_texture_format / map_vertex_format)
  - id: brutal-vertexinput
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/VertexInput.cs
    title: Brutal.Vulkan.Abstractions VertexInput.cs (C# type → VkFormat map)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# How to read these tables

- The texture tables invert Dawn's `VulkanImageFormat()` (WebGPU → Vulkan) and are cross-checked against the WebGPU capability tables in §26.1; wgpu-hal's `map_texture_format` is an independent second inverse table.[^dawn-texturevk][^wgpu-hal][^webgpu-spec]
- Feature letters are the `VkFormatFeatureFlags` the ICD should report in `optimalTilingFeatures` (`linearTilingFeatures` = 0 for everything; `bufferFeatures` see vertex table):
  **S** `SAMPLED_IMAGE`, **L** `SAMPLED_IMAGE_FILTER_LINEAR` (WebGPU sample type `"float"`), **C** `COLOR_ATTACHMENT`, **B** `COLOR_ATTACHMENT_BLEND`, **W** `STORAGE_IMAGE` (WGSL write-only/read-only), **RW** read-write storage (needs `texture-formats-tier2` + WGSL `read_write`), **D** `DEPTH_STENCIL_ATTACHMENT`, **T** `TRANSFER_SRC|DST`, **X** `BLIT_SRC` (= S, emulated by a draw) / `BLIT_DST` (= C, emulated by a draw), **4×** 4-sample MSAA supported, **Rv** resolvable.
- Condition tags: **[T1]** needs `texture-formats-tier1`, **[T2]** `texture-formats-tier2`, **[core]** missing in compatibility mode, **[F32F]** `float32-filterable`, **[F32B]** `float32-blendable`, **[RG11]** `rg11b10ufloat-renderable`, **[BGRAS]** `bgra8unorm-storage`.[^webgpu-spec]
- All plain colour formats support `COPY_SRC`, `COPY_DST`, `TEXTURE_BINDING` and the `"3d"` dimension.[^webgpu-spec]

# Plain colour formats

| VkFormat | GPUTextureFormat | Feature letters to report | Notes |
| --- | --- | --- | --- |
| `R8_UNORM` | `r8unorm` | S L C B 4× Rv T X; W [T1]; RW [T2] | |
| `R8_SNORM` | `r8snorm` | S L T; C B 4× Rv [T1]; W [T1] | not renderable without tier1 |
| `R8_UINT` / `R8_SINT` | `r8uint` / `r8sint` | S C T; 4× [core]; W [T1]; RW [T2] | integer: no L/B/Rv |
| `R8G8_UNORM` | `rg8unorm` | S L C B 4× Rv T X; W [T1] | |
| `R8G8_SNORM` | `rg8snorm` | S L T; C B 4× Rv [T1]; W [T1] | |
| `R8G8_UINT` / `R8G8_SINT` | `rg8uint` / `rg8sint` | S C T; 4× [core]; W [T1] | |
| `R8G8B8A8_UNORM` | `rgba8unorm` | S L C B 4× Rv W T X; RW [T2] | the workhorse |
| `R8G8B8A8_SRGB` | `rgba8unorm-srgb` | S L C B 4× Rv T X | no storage |
| `R8G8B8A8_SNORM` | `rgba8snorm` | S L W T; C B 4× Rv [T1] | |
| `R8G8B8A8_UINT` / `_SINT` | `rgba8uint` / `rgba8sint` | S C W T; 4× [core]; RW [T2] | |
| `B8G8R8A8_UNORM` | `bgra8unorm` | S L C B 4× Rv T X; W [BGRAS] | preferred canvas format on many desktops |
| `B8G8R8A8_SRGB` | `bgra8unorm-srgb` | S L C B 4× Rv T X [core] | |
| `R16_UNORM` / `R16_SNORM` | `r16unorm` / `r16snorm` | **exists only with [T1]**: S C B 4× W T | sample type `unfilterable-float` → **no L** |
| `R16_UINT` / `R16_SINT` | `r16uint` / `r16sint` | S C T; 4× [core]; W [T1]; RW [T2] | |
| `R16_SFLOAT` | `r16float` | S L C B 4× Rv T X; W [T1]; RW [T2] | |
| `R16G16_UNORM` / `_SNORM` | `rg16unorm` / `rg16snorm` | [T1] only: S C B 4× W T | no L |
| `R16G16_UINT` / `_SINT` | `rg16uint` / `rg16sint` | S C T; 4× [core]; W [T1] | |
| `R16G16_SFLOAT` | `rg16float` | S L C B 4× Rv T X; W [T1] | |
| `R16G16B16A16_UNORM` / `_SNORM` | `rgba16unorm` / `rgba16snorm` | [T1] only: S C B 4× W T | no L |
| `R16G16B16A16_UINT` / `_SINT` | `rgba16uint` / `rgba16sint` | S C W T; 4× [core]; RW [T2] | |
| `R16G16B16A16_SFLOAT` | `rgba16float` | S L C B W T X; 4× Rv [core]; RW [T2] | typical HDR target |
| `R32_UINT` / `R32_SINT` | `r32uint` / `r32sint` | S C W RW T | only formats with core read-write storage + atomics |
| `R32_SFLOAT` | `r32float` | S C W RW T; L [F32F]; B [F32B]; 4× [core] | |
| `R32G32_UINT` / `_SINT` | `rg32uint` / `rg32sint` | S C T; W [core] | |
| `R32G32_SFLOAT` | `rg32float` | S C T; L [F32F]; B [F32B]; W [core] | |
| `R32G32B32A32_UINT` / `_SINT` | `rgba32uint` / `rgba32sint` | S C W T; RW [T2] | 16 B/px counts heavily against `maxColorAttachmentBytesPerSample` (32) |
| `R32G32B32A32_SFLOAT` | `rgba32float` | S C W T; L [F32F]; B [F32B]; RW [T2] | |
| `A2B10G10R10_UNORM_PACK32` | `rgb10a2unorm` | S L C B 4× Rv T X; W [T1] | bit layout identical (R in low bits)[^dawn-texturevk] |
| `A2B10G10R10_UINT_PACK32` | `rgb10a2uint` | S C T; 4× [core]; W [T1] | |
| `B10G11R11_UFLOAT_PACK32` | `rg11b10ufloat` | S L T; C B 4× Rv [RG11] (implied by T1); W [T1] | |
| `E5B9G9R9_UFLOAT_PACK32` | `rgb9e5ufloat` | S L T | never renderable[^webgpu-spec] |

# Depth / stencil formats

| VkFormat | GPUTextureFormat | Report | Copy rules (WebGPU) | Notes |
| --- | --- | --- | --- | --- |
| `D16_UNORM` | `depth16unorm` | D S T 4× | copy src ✓ dst ✓ | only depth format with full copy support |
| `X8_D24_UNORM_PACK32` | `depth24plus` | D S 4× (no T) | **no depth copies at all** | may be 24-bit or 32F underneath; Dawn maps it to `D32_SFLOAT`[^dawn-texturevk] |
| `D24_UNORM_S8_UINT` | `depth24plus-stencil8` | D S 4× (stencil aspect T only) | depth aspect not copyable; stencil copyable | most common Vulkan desktop depth format; maps cleanly for rendering, not for copies |
| `D32_SFLOAT` | `depth32float` | D S 4×; TRANSFER_SRC only | copy src ✓; dst only from same-format texture | uploading depth from a buffer requires a fragment shader writing `frag_depth`[^webgpu-spec] |
| `D32_SFLOAT_S8_UINT` | `depth32float-stencil8` | as above + stencil | needs feature `depth32float-stencil8` | |
| `S8_UINT` | `stencil8` | D S T 4× | copy ✓ | may be implemented as D24S8 with hidden depth |
| `D16_UNORM_S8_UINT` | — | none | — | not available; substitute `depth24plus-stencil8` |

Depth textures are never filterable (`"depth"`/`"unfilterable-float"` sample types) — Vulkan `SAMPLED_IMAGE_FILTER_LINEAR` must **not** be reported for depth, but comparison samplers with linear filtering (PCF) work.[^webgpu-spec] Sampling the depth aspect through a non-depth binding returns (D, X, X, X) unless `texture-component-swizzle` is enabled (then (D, 0, 0, 1)).[^webgpu-spec]

# Compressed formats (all: S L T; never C/W/4×; "3d" only with the `-sliced-3d` feature)

| Family | VkFormat range | GPUTextureFormat | Feature |
| --- | --- | --- | --- |
| BC1 | `BC1_RGBA_UNORM/SRGB_BLOCK` | `bc1-rgba-unorm[-srgb]` | `texture-compression-bc` |
| BC1 RGB | `BC1_RGB_UNORM/SRGB_BLOCK` | — (use the RGBA variant; alpha decodes as 1 for 3-colour blocks only, so data must be RGBA-encoded) | — |
| BC2 / BC3 | `BC2_*`, `BC3_*` | `bc2-rgba-unorm[-srgb]`, `bc3-rgba-unorm[-srgb]` | bc |
| BC4 / BC5 | `BC4_UNORM/SNORM`, `BC5_UNORM/SNORM` | `bc4-r-unorm/snorm`, `bc5-rg-unorm/snorm` | bc |
| BC6H | `BC6H_UFLOAT/SFLOAT_BLOCK` | `bc6h-rgb-ufloat`, `bc6h-rgb-float` | bc |
| BC7 | `BC7_UNORM/SRGB_BLOCK` | `bc7-rgba-unorm[-srgb]` | bc |
| ETC2 / EAC | `ETC2_R8G8B8_*`, `ETC2_R8G8B8A1_*`, `ETC2_R8G8B8A8_*`, `EAC_R11_*`, `EAC_R11G11_*` | `etc2-rgb8unorm[-srgb]`, `etc2-rgb8a1unorm[-srgb]`, `etc2-rgba8unorm[-srgb]`, `eac-r11unorm/snorm`, `eac-rg11unorm/snorm` | `texture-compression-etc2` |
| ASTC LDR | `ASTC_{4x4…12x12}_UNORM/SRGB_BLOCK` | `astc-{4x4…12x12}-unorm[-srgb]` | `texture-compression-astc` |
| ASTC HDR, PVRTC | `ASTC_*_SFLOAT_BLOCK`, `PVRTC*_IMG` | — | not available |

Two WebGPU-specific traps: (1) without `texture-compression-unaligned`, mip 0 width/height must be multiples of the block size (Vulkan allows any size);[^webgpu-spec] (2) desktop browsers generally expose BC but not ETC2/ASTC, mobile the reverse — a BRUTAL game shipping KTX2 should transcode (Basis/UASTC) at load time to whatever `GPUAdapter.features` offers, and the ICD should report format support honestly so `vkGetPhysicalDeviceFormatProperties`-driven selection works.

# Vulkan colour formats with no WebGPU equivalent

| VkFormat(s) | Why missing | Substitute |
| --- | --- | --- |
| `R8G8B8_*`, `B8G8R8_*`, `R16G16B16_*`, `R32G32B32_*` (as images) | no 3-component texel formats | expand to 4 components on upload (ICD can do it in `vkCmdCopyBufferToImage` via a compute pass, but better at asset-build time) |
| `R5G6B5_UNORM_PACK16`, `B5G6R5`, `R5G5B5A1`, `A1R5G5B5`, `R4G4B4A4`, `B4G4R4A4`, `A4R4G4B4` | no packed 16-bit formats | expand to `rgba8unorm` |
| `A2R10G10B10_*` (BGR order), `A2B10G10R10_SNORM/USCALED/SSCALED` | only RGB-order unorm/uint exist | swizzle on upload |
| `*_USCALED`, `*_SSCALED` (images) | no scaled formats | uint/sint + shader conversion |
| `R64*` | no 64-bit formats | — |
| `B8G8R8A8_SNORM/UINT/SINT`, `R8G8B8A8_*` already covered | — | RGBA variants |
| multi-planar / YCbCr (`G8_B8R8_2PLANE_420_UNORM` etc.) | not exposed to web content (Dawn has internal biplanar formats) | `importExternalTexture` for video only |
| `D16_UNORM_S8_UINT` | — | `depth24plus-stencil8` |
| view-format reinterpretation (`MUTABLE_FORMAT` + `R32_UINT` view of `RGBA8`) | `viewFormats` may only differ by `-srgb` | copy through a buffer, or compute |

# Vertex formats (`VkVertexInputAttributeDescription.format` → `GPUVertexFormat`)

Inverse of Dawn's `VulkanVertexFormat()`.[^dawn-pipelinevk] `bufferFeatures` should report `VERTEX_BUFFER` exactly for these VkFormats.

| VkFormat | GPUVertexFormat | VkFormat | GPUVertexFormat |
| --- | --- | --- | --- |
| `R8_UINT` | `uint8` | `R16_UINT` | `uint16` |
| `R8G8_UINT` | `uint8x2` | `R16G16_UINT` | `uint16x2` |
| `R8G8B8A8_UINT` | `uint8x4` | `R16G16B16A16_UINT` | `uint16x4` |
| `R8_SINT` | `sint8` | `R16_SINT` | `sint16` |
| `R8G8_SINT` | `sint8x2` | `R16G16_SINT` | `sint16x2` |
| `R8G8B8A8_SINT` | `sint8x4` | `R16G16B16A16_SINT` | `sint16x4` |
| `R8_UNORM` | `unorm8` | `R16_UNORM` | `unorm16` |
| `R8G8_UNORM` | `unorm8x2` | `R16G16_UNORM` | `unorm16x2` |
| `R8G8B8A8_UNORM` | `unorm8x4` | `R16G16B16A16_UNORM` | `unorm16x4` |
| `R8_SNORM` | `snorm8` | `R16_SNORM` | `snorm16` |
| `R8G8_SNORM` | `snorm8x2` | `R16G16_SNORM` | `snorm16x2` |
| `R8G8B8A8_SNORM` | `snorm8x4` | `R16G16B16A16_SNORM` | `snorm16x4` |
| `R32_SFLOAT` | `float32` | `R16_SFLOAT` | `float16` |
| `R32G32_SFLOAT` | `float32x2` | `R16G16_SFLOAT` | `float16x2` |
| `R32G32B32_SFLOAT` | `float32x3` | `R16G16B16A16_SFLOAT` | `float16x4` |
| `R32G32B32A32_SFLOAT` | `float32x4` | `A2B10G10R10_UNORM_PACK32` | `unorm10-10-10-2` |
| `R32_UINT` … `R32G32B32A32_UINT` | `uint32` … `uint32x4` | `A2B10G10R10_SNORM_PACK32` | `snorm10-10-10-2` (Chrome 155–156) |
| `R32_SINT` … `R32G32B32A32_SINT` | `sint32` … `sint32x4` | `B8G8R8A8_UNORM` | `unorm8x4-bgra` |

Vertex formats Vulkan has and WebGPU lacks — all reachable from BRUTAL's `VertexInput` type map:[^brutal-vertexinput]

| Vulkan | Produced by `VertexInput` for | WebGPU fix |
| --- | --- | --- |
| `R8G8B8_*`, `R16G16B16_*` (3-component 8/16-bit) | `sbyte3`, `byte3`, `short3`, `ushort3` | pad to x4 (vertex struct change) — **forbidden in the profile** |
| `*_USCALED`, `*_SSCALED` | any integer type with `Behavior.Scale` | use `uint*`/`sint*` + `f32()` conversion in the shader |
| `R16G16B16_SFLOAT` | (not produced) | pad to x4 |
| `R64*` | (not produced) | — |
| `A2R10G10R10_*`, `B10G11R11_UFLOAT_PACK32` | (not produced) | — |

Other vertex-input rules: `arrayStride` multiple of 4 and ≤ 2048 (portability-subset `minVertexInputBindingStrideAlignment = 4`); attribute `offset` multiple of min(4, format size); `offset + size ≤ arrayStride` unless stride is 0 (portability `vertexAttributeAccessBeyondStride = false`); stride 0 is allowed (then `offset + size ≤ maxVertexBufferArrayStride`); per-instance divisors (`VK_EXT_vertex_attribute_divisor`) unsupported; WGSL shader input type must match the format's base type (float/uint/sint).[^webgpu-spec]

# Index formats

| Vulkan | WebGPU | Notes |
| --- | --- | --- |
| `VK_INDEX_TYPE_UINT16` | `"uint16"` | offset must be a multiple of 2 |
| `VK_INDEX_TYPE_UINT32` | `"uint32"` | offset multiple of 4; `fullDrawIndexUint32 = true` |
| `VK_INDEX_TYPE_UINT8` (`VK_KHR_index_type_uint8`) | — | expand to uint16 on upload (or reject) |
| `VK_INDEX_TYPE_NONE_KHR` | — | ray tracing only |

Primitive restart: for strip topologies WebGPU needs `stripIndexFormat` at pipeline creation and the restart value is always 0xFFFF / 0xFFFFFFFF; it cannot be disabled for indexed strip draws.[^webgpu-spec]

# Surface (canvas) formats

| `VkSurfaceFormatKHR` reported | Canvas `configure()` | Swapchain image view format |
| --- | --- | --- |
| `B8G8R8A8_UNORM` / `SRGB_NONLINEAR` (if preferred is `bgra8unorm`) | `format: "bgra8unorm"` | `bgra8unorm` |
| `B8G8R8A8_SRGB` / `SRGB_NONLINEAR` | `format: "bgra8unorm", viewFormats: ["bgra8unorm-srgb"]` | `bgra8unorm-srgb` |
| `R8G8B8A8_UNORM` / `R8G8B8A8_SRGB` (if preferred is `rgba8unorm`) | same pattern with `rgba8unorm` | |
| `R16G16B16A16_SFLOAT` / `EXTENDED_SRGB_LINEAR_EXT` (optional) | `format: "rgba16float", colorSpace: "srgb-linear"?, toneMapping: {mode: "extended"}` | HDR (UNVERIFIED cross-browser) |

Reporting the non-preferred 8-bit format is legal (all three are "supported context formats"), but may cost a blit inside the browser compositor.[^webgpu-spec]

# Open questions

- UNVERIFIED: which compressed-format features each browser/OS exposes in practice (BC on all desktops incl. Apple Silicon? ETC2/ASTC on Apple Silicon Chrome?).
- Resolved: attribute offsets must be a multiple of `min(4, byteSize(format))` and `offset + byteSize ≤ arrayStride` (or ≤ `maxVertexBufferArrayStride` when stride is 0) per §10.3.7 (validating GPUVertexBufferLayout).

[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026 (§26.1 Texture Format Capabilities, §10.3.7.1 Vertex Formats)
[^dawn-texturevk]: Dawn Vulkan backend TextureVk.cpp (VulkanImageFormat — the inverse table)
[^dawn-pipelinevk]: Dawn Vulkan backend RenderPipelineVk.cpp (VulkanVertexFormat — the inverse table)
[^wgpu-hal]: wgpu-hal Vulkan conv.rs (map_texture_format / map_vertex_format)
[^brutal-vertexinput]: Brutal.Vulkan.Abstractions VertexInput.cs (C# type → VkFormat map)
