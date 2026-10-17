# patches/brutal

Everything needed to build BRUTAL's `Brutal.Vulkan` and `Brutal.Vulkan.Abstractions` from the pinned, read-only BRUTAL tree (`BrutalSourceRoot`, commit `a25cdf8`; never modified) for the browser.

| File | What |
| --- | --- |
| `transform.py` | `python3 -I transform.py --src <dir> --out <dir> [--managed-fnptr] [--exclude <rel path>]... [--no-strip-uco] [--report <file>]`. Copies a library's `.cs` files; with `--managed-fnptr` applies R1-R5 (`delegate* unmanaged[Cdecl]<` and other calling-convention forms become `delegate*<`; `[UnmanagedCallersOnly]` is removed). Writes only changed files, prunes stale outputs, prints a one-line summary, exits 1 if an unmanaged pointer survives. |
| `BrutalSource.targets` | Imported by the two flavor projects. Inputs: `BrutalLibrary`, `BrutalManagedFnptr` (default `true`), `BrutalStripUco`, `BrutalSourceRoot`, `BrutalPython`. Target `TransformBrutalSources` (incremental) writes `$(IntermediateOutputPath)brutal-src/`, the copies are compiled, `Patched/<path>` replaces the upstream `<path>`, `BRUTAL_MANAGED_FNPTR` is defined when managed, `DisableRuntimeMarshalling` is added. |
| `0001-pluggable-loader-import-resolver.patch` | `Brutal.Vulkan/ImportResolver.cs`: the `BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR` AppContext key, `Vulkan.GlobalCommands` (global commands through `GIPA(NULL, name)`), one source for both flavors via `#if BRUTAL_MANAGED_FNPTR`. |
| `0002-pluggable-loader-global-commands.patch` | `Brutal.Vulkan/Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs` (CRLF): four call sites go through `Vulkan.GlobalCommands.*` instead of the P/Invokes. |
| `brutal-vulkan-pluggable-loader.patch` | 0001 and 0002 concatenated. |
| `make-patches.sh` | Regenerates the three patches from `src/Brutal.Vulkan.Browser/Patched/` and checks them with `git apply --check`. |
| `tools/ApiDump`, `tools/api-diff.sh` | Signature-level API listing and the comparison of a built assembly with the NuGet one. |

All three patches pass `git -C <BRUTAL> apply --check` against `a25cdf8` (read-only check; verified again by `PatchTests` in `tests/Brutal.Vulkan.Browser.Tests`, which also applies the patch to a scratch copy and compares it byte for byte with `Patched/`).

Apply to your own checkout: `git -C <BRUTAL> apply patches/brutal/brutal-vulkan-pluggable-loader.patch`.

## Transform rules

| Rule | Pattern | Result | Count in `Brutal.Vulkan` / Abstractions |
| --- | --- | --- | --- |
| R1 | `delegate* unmanaged[Cdecl]<` | `delegate*<` | 2441 (+1 in the patched `ImportResolver.cs`) / 1 |
| R2 | `delegate* unmanaged[Stdcall]<` | `delegate*<` | 0 / 0 |
| R3 | `delegate* unmanaged[<other>]<` | `delegate*<` | 0 / 0 |
| R4 | `delegate* unmanaged<` | `delegate*<` | 0 / 0 |
| R5 | `[UnmanagedCallersOnly(...)]` | removed | 0 / 3 |
