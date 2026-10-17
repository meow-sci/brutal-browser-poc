# BRUTAL web Vulkan profiles

`VP_BRUTAL_web.json` is a [Vulkan Profiles](https://github.com/KhronosGroup/Vulkan-Profiles) file that documents the Vulkan subset the BRUTAL Vulkan-on-WebGPU ICD implements (rationale and tables: [`docs/research/webgpu/web-vulkan-profile-proposal.md`](../docs/research/webgpu/web-vulkan-profile-proposal.md)). `scripts/validate-web-profile.sh` runs a BRUTAL **desktop** app against it with the Khronos profiles and validation layers, so subset violations show up as VUIDs before the app is ever run in a browser.

All profiles are **non-conformant** Vulkan 1.3 (`api-version` 1.3.0, status ALPHA, the version the ICD advertises): several limits are below Vulkan's required minimums (`maxVertexInputBindings` 8, `maxFramebufferLayers` 1, `maxSamplerLodBias` 0) and 1.3-required features are missing (`bufferDeviceAddress`, descriptor indexing, `privateData`, `inlineUniformBlock`, `subgroupSizeControl`, ...). **Why 1.3**: BRUTAL calls the 1.3 core entry points (`vkCmdBeginRendering`, `vkCmdPipelineBarrier2`, ...), and the Khronos profiles/validation layers resolve those names only when the simulated device is 1.3 (under a 1.2 profile `vkGetDeviceProcAddr` returns NULL and an unchecked call crashes); `samples/HelloMesh/HelloMesh.Desktop` passes under the 1.3 profiles with zero VUIDs.

## Contents

| Profile | Capabilities | Meaning |
| --- | --- | --- |
| `VP_BRUTAL_web_poc_2026` | `web_core` | The device the ICD reports today (`Profile.cs` is the source of truth; checked by `ProfileJsonSyncTests`): core WebGPU defaults, no optional WebGPU features, 128 B push constants through a UBO ring (so `maxBoundDescriptorSets` 3), minimal emulation. |
| `VP_BRUTAL_web_game_2026` | `web_core`, `web_game_emulated` (+ `optionals`) | PoC plus the game-tier capabilities the ICD **does not yet implement or advertise**: events, triangle fans, constant-alpha blend, timeline semaphores, extended dynamic state, push descriptors, descriptor update templates, `multiDrawIndirect` (`maxDrawIndirectCount` 65535), `shaderDrawParameters`, input attachments, `pipelineCreationCacheControl`. |
| `VP_BRUTAL_web_game_max_2026` | game + every `web_<feature>` block | The most capable adapter: BC/ETC2/ASTC, `D32_SFLOAT_S8_UINT`, f16, clip distances, dual-source blend, depth clip control, subgroups, texture swizzle, timestamps, tier1 formats, filterable/blendable float32. |

Capability blocks: `web_core` (PoC values), `web_game_emulated` (game-tier overrides, repeats whole `VkFormatProperties` entries it changes), and one optional block per WebGPU adapter feature (`web_bc`, `web_etc2`, `web_astc`, `web_depth32_stencil8`, `web_float32_filterable_blendable`, `web_texture_formats_tier1`, `web_f16`, `web_clip_distances`, `web_dual_source_blend`, `web_depth_clip_control`, `web_indirect_first_instance`, `web_subgroups`, `web_subgroup_size_control`, `web_texture_component_swizzle`, `web_timestamp_query`, `web_uniform_buffer_standard_layout`, `web_hdr_canvas`). The game profile lists them in `optionals`; **the Khronos profiles layer ignores `optionals`**, which is why `..._game_max_2026` lists them as required capabilities. Use `game` to test against the guaranteed game tier, `game_max` when your code uses an adapter-gated feature.

The JSON holds extensions, features (`VkPhysicalDeviceFeatures`, the Vulkan 1.1/1.2/1.3 structs member by member — dynamic rendering, synchronization2 and maintenance4 are carried by `VkPhysicalDeviceVulkan13Features`, not by the KHR structs, which the profiles layer warns about when both are present —, portability subset with all 15 booleans), every `VkPhysicalDeviceLimits` member, formats (optimal-tiling bits from `format-tables.md`, `VERTEX_BUFFER_BIT` on exactly the 42 WebGPU vertex formats, everything else unsupported) and one graphics+compute+transfer queue family with `queueCount` 1.

## Running the validation

```sh
scripts/validate-web-profile.sh [--profile VP_BRUTAL_web_poc_2026] [options] -- <command...>
scripts/validate-web-profile.sh -- dotnet run --project samples/HelloMesh/HelloMesh.Desktop -c Release -- --frames 120
scripts/validate-web-profile.sh --profile VP_BRUTAL_web_game_2026 -- dotnet run --project samples/HelloMesh/HelloMesh.Desktop -c Release -- --frames 120
scripts/validate-web-profile.sh --list
```

The script exports `VK_INSTANCE_LAYERS=VK_LAYER_KHRONOS_validation:VK_LAYER_KHRONOS_profiles` (profiles layer nearest the driver, validation above it, system ICD untouched) plus the settings below, prints that configuration, runs the command on a pty, scans the merged output and exits `0` pass / `1` VUIDs or validation errors / `2` usage or setup error / `3` profiles layer failure or profile never applied / `4` command failed without a layer finding. Options: `--profile`, `--profile-dir`, `--log`, `--sync`, `--best-practices`, `--ignore REGEX`, `--no-pty`, `--fail-on-error`; see `--help`.

A passing run ends with `PASS`, exit 0, `0 error(s), 0 warning(s), 0 line(s) mentioning a VUID` (recorded 2026-10-16 for HelloMesh.Desktop, 120 frames, Apple M4 Pro, SDK 1.4.328.1: `VP_BRUTAL_web_poc_2026`, `..._game_2026` and `..._game_max_2026` all pass). A new app is validated the same way; fix the app (or, when the ICD really supports what it does, the ICD and then the profile) for every VUID. The script restores `DYLD_LIBRARY_PATH` for the Vulkan SDK on macOS (see below).

Settings (env names verified against SDK 1.4.328.1; `khronos_profiles.<key>` / `khronos_validation.<key>` in a `vk_layer_settings.txt` via `VK_LAYER_SETTINGS_PATH` also works):

| Layer setting | Environment variable | Script value |
| --- | --- | --- |
| profile_dirs / profile_name | `VK_KHRONOS_PROFILES_PROFILE_DIRS` / `_PROFILE_NAME` | `profiles/` / chosen profile |
| profile_validation | `VK_KHRONOS_PROFILES_PROFILE_VALIDATION` | `true` (schema-checks the JSON at load) |
| simulate_capabilities | `VK_KHRONOS_PROFILES_SIMULATE_CAPABILITIES` | `SIMULATE_API_VERSION_BIT,..._FEATURES_BIT,..._PROPERTIES_BIT,..._EXTENSIONS_BIT,..._FORMATS_BIT,..._QUEUE_FAMILY_PROPERTIES_BIT` (the last is not in the 1.4.328 manifest but works) |
| emulate_portability + 15 booleans + stride | `VK_KHRONOS_PROFILES_EMULATE_PORTABILITY=true`, `VK_KHRONOS_PROFILES_<MEMBER>` (upper case, e.g. `..._TRIANGLEFANS`), `..._MINVERTEXINPUTBINDINGSTRIDEALIGNMENT` | read from the profile JSON |
| debug_actions / debug_reports | `VK_KHRONOS_PROFILES_DEBUG_ACTIONS` / `_DEBUG_REPORTS` | stdout / notification,warning,error |
| debug_fail_on_error | `VK_KHRONOS_PROFILES_DEBUG_FAIL_ON_ERROR` | off (opt-in `--fail-on-error`: with this layer it makes `vkEnumeratePhysicalDevices` fail even for a valid profile) |
| validation | `VK_KHRONOS_VALIDATION_VALIDATE_CORE`, `_REPORT_FLAGS=error,warn`, `_VALIDATE_SYNC`, `_VALIDATE_BEST_PRACTICES` | core on |

With `emulate_portability=true` the layer takes the portability booleans from its settings and ignores the JSON; with `false` it takes them from the JSON (requires a device that really has `VK_KHR_portability_subset`). The script derives the settings from the JSON so both agree.

## Limitations and expected noise

- **Not expressible**: the custom canvas-surface instance extension `VK_BRUTAL_web_surface` (schema accepts registry extension names only; see top-level `comment`), the 32 bytes/sample colour-attachment budget, anything per-pipeline.
- **WebGPU-only rules the layers cannot see**: 256-byte `bytesPerRow`, 4-byte buffer offsets/sizes/strides, `DONT_CARE` stores that are read later, SPIR-V combined samplers/arrays, Y-convention mixing, blocking readback waits. Check them with a debug-only `WebProfileLint` and offline SPIR-V reflection.
- **Instance extensions** are listed for documentation; the layer cannot filter them. Device extensions are filtered, so an app that enables an extension outside the list fails `vkCreateDevice` (the layer also adds every extension promoted to core up to 1.3, e.g. `VK_KHR_timeline_semaphore`, `VK_KHR_shader_terminate_invocation`, `VK_EXT_private_data`, which the ICD list does not contain).
- **The api-version must stay at the ICD's (1.3.0)**: the validation layer returns NULL from `vkGetDeviceProcAddr` for the Vulkan 1.3 core entry points (`vkCmdBeginRendering`, `vkCmdPipelineBarrier2`, `vkQueueSubmit2`, `vkCmdCopyBuffer2`, ...) when the profile is 1.2, and a caller using unchecked function pointers then crashes with SIGSEGV instead of getting a VUID (script status 4 with a signal hint; this is what `samples/HelloMesh/HelloMesh.Desktop` did under the former 1.2 profiles). The 1.3 profile is also what lets `VkPhysicalDeviceVulkan13Features` (below) be carried.
- **`PROFILES WARNING` lines are informational** and appear on most desktop GPUs: values the host cannot back (`D24` depth formats on Apple GPUs, game tier `multiDrawIndirect`, `timelineSemaphore`, `pipelineCreationCacheControl` ...) or that are more permissive than the host (`bufferImageGranularity` 1, `nonCoherentAtomSize` 4, `pointSizeGranularity` 0). The layer compares each profile value with the host *or an earlier capability block*, so the game tier's overrides of PoC values also warn. They never fail the script.
- **Vulkan 1.3 features**: `VkPhysicalDeviceVulkan13Features` carries exactly what `Profile.FillVulkan13Features` reports — `robustImageAccess`, `shaderDemoteToHelperInvocation`, `shaderTerminateInvocation`, `shaderZeroInitializeWorkgroupMemory`, `dynamicRendering`, `synchronization2`, `maintenance4` true; `bufferDeviceAddress` (1.2 struct), descriptor indexing, `privateData`, `inlineUniformBlock`, `subgroupSizeControl`, `shaderIntegerDotProduct`, ... false (non-conformant). `pipelineCreationCacheControl` is false in `web_core` and true in `web_game_emulated` (not yet implemented by the ICD). `timestampValidBits` is not simulated by the layer, and `queueFamiliesProperties` must stay in `web_core` (arrays from later blocks are appended).
- Beyond the proposal's tables: image formats outside its short "guaranteed" list (`R8_SNORM`, 8/16-bit integer formats, `A2B10G10R10_UINT`) come from `format-tables.md`; derived limits (per-set descriptor totals = the per-stage values, `maxPerStageResources` 64, `maxFragmentCombinedOutputResources` 20, `maxTexelBufferElements` 0, `viewportBoundsRange` +-16384) mirror what the ICD's `Profile.cs` reports.
- Depth formats report no `SAMPLED_IMAGE_FILTER_LINEAR` (WebGPU depth is unfilterable), so linear PCF comparison samplers draw a VUID; the format table says comparison samplers work, decide per sampler.
- macOS: `/bin/bash` and `/usr/bin/env` drop `DYLD_LIBRARY_PATH` (SIP); the script restores the Vulkan SDK lib dir (`$VALIDATE_WEB_PROFILE_DYLD_LIBRARY_PATH`, `$VULKAN_SDK/lib`, newest `~/VulkanSDK/*/macOS/lib`), otherwise GLFW fails with "Could not create the window surface".

## Checking the JSON

```sh
python3 -I -m json.tool profiles/VP_BRUTAL_web.json > /dev/null     # parses
scripts/validate-web-profile.sh -- vulkaninfo --summary             # layer: PROFILES ERROR => rejected; device shows apiVersion 1.3.0
scripts/validate-web-profile.sh -- vulkaninfo | grep -E 'apiVersion|maxBoundDescriptorSets|maxPushConstantsSize'   # 1.3.0, 3, 128
```

Schema check (SDK `share/vulkan/registry/profiles-0.8-latest.json`; install `jsonschema` with `python3 -I -m pip install --target DIR jsonschema`, never globally):

```python
import json, sys
sys.path.insert(0, "DIR")
import jsonschema
schema = json.load(open("<VulkanSDK>/macOS/share/vulkan/registry/profiles-0.8-latest.json"))
jsonschema.Draft7Validator(schema).validate(json.load(open("profiles/VP_BRUTAL_web.json")))
```

## Keeping the ICD and this file in sync

The ICD's `vkGetPhysicalDevice*` answers (`src/Brutal.Vulkan.WebGpu/Profile.cs`) are the source of truth; `web_core` (= `VP_BRUTAL_web_poc_2026`) and this file must describe the same device.

**Sync test**: `tests/Brutal.Vulkan.WebGpu.Tests/ProfileJsonSyncTests.cs` loads this JSON, creates the ICD's physical device through the recording-bridge test setup (an adapter at the WebGPU default limits, no optional features) and fails, listing every difference, when either side drifts:

- `api-version` of all three profiles equals `vkEnumerateInstanceVersion` / `VkPhysicalDeviceProperties.apiVersion`;
- every `VkPhysicalDeviceLimits` member is listed in the JSON and equals the ICD's value (also `sparseProperties`, `VkPhysicalDeviceVulkan11Properties`, `VkPhysicalDeviceMaintenance4PropertiesKHR`, portability subset properties);
- every extension in the JSON is advertised by the ICD with the same spec version, and every ICD extension except `VK_BRUTAL_web_surface` is in the JSON;
- `VkPhysicalDeviceFeatures` and the `VkPhysicalDeviceVulkan11/12/13Features` / portability subset structs match member by member, in both directions;
- every listed format has the ICD's `optimalTilingFeatures` / `linearTilingFeatures` / `bufferFeatures`, and every format the ICD gives any feature is listed;
- the queue family (flags, count, transfer granularity).

It does not cover `web_game_emulated` (by definition not implemented by the ICD) or the adapter-gated `web_<feature>` blocks. Run it with `dotnet test tests/Brutal.Vulkan.WebGpu.Tests --filter ProfileJsonSyncTests`.

1. Change to a limit, feature, extension, format or queue family in the ICD: edit the matching capability block (`web_core` for what the ICD reports, `web_game_emulated` for game-tier values it does not yet implement, `web_<feature>` for adapter-gated values), keep a changed `VkFormatProperties` entry complete (a later block replaces the whole entry), bump the profile `version` and add a `history` entry. When the ICD starts implementing a game-tier capability, move it from `web_game_emulated` to `web_core` in the same change.
2. Run the sync test, the three checks above for all three profiles, and the script against a sample app; compare `vulkaninfo --json` of the simulated device with the JSON when in doubt.
3. New app-side conventions that Vulkan cannot express belong in the proposal's convention table and in `WebProfileLint`, not here.

Known ICD inconsistencies the profile mirrors (the ICD is the truth, so they are in `web_core` until the ICD changes):

- `maxDescriptorSetUniformBuffersDynamic` is 8, but the emulated push constants occupy one dynamic uniform buffer in the extra bind group, so an app that uses 8 dynamic UBOs plus push constants exceeds WebGPU's 8 per pipeline layout; the effective budget is 7.
- Ten colour formats report `BLIT_SRC` / `BLIT_DST` (`Formats.OptimalTilingFeatures`) while `vkCmdBlitImage` is still a stub, so the profile allows blits the ICD cannot run.
