# exp-06: managed function-pointer flavor of Brutal.Vulkan

This experiment builds `Brutal.Vulkan` and `Brutal.Vulkan.Abstractions` from the read-only BRUTAL source in a
second flavor. In that flavor, every `delegate* unmanaged[Cdecl]<…>` becomes a managed `delegate*<…>`. A Vulkan
"ICD" written as plain `static` C# methods can then be called by BRUTAL's own bindings in .NET 10 browser-wasm. It
needs no `wasm-tools` workload, no `[UnmanagedCallersOnly]`, no JS trampolines and no `[JSExport]`. The interpreter
has no call-signature limits on this path.

The full write-up is `docs/research/experiments/exp-06-managed-fnptr-flavor.md`. Numbers and raw logs are in
`RESULTS.md` and `out/`.

The BRUTAL checkout at `/Users/asherwin/repos/meow-sci/BRUTAL` (commit `a25cdf8`) is never modified. The
unmanaged flavor compiles it in place. The managed flavor compiles transformed copies written to
`src/*/obj/managed/generated/src/` at build time.

## Layout

| Path | What |
|---|---|
| `Directory.Build.props`, `nuget.config` | Shared settings: `BrutalSourceRoot`, package versions, `Python`. The NuGet feed is nuget.org only. |
| `src/Directory.Build.props` | Flavor switch `BrutalManagedFnptr` (default `false`). `obj/` and `bin/` are split per flavor (`obj/managed`, `obj/unmanaged`), so both flavors build side by side from the same projects. NuGet restore output stays in `obj/`. |
| `src/Directory.Build.targets` | `BrutalManagedFnptrTransform` target. Before `BeforeCompile` it runs `python3 -I tools/transform.py`, adds the copies as `Compile` items and defines `BRUTAL_MANAGED_FNPTR`. |
| `src/Brutal.Vulkan/Brutal.Vulkan.csproj` | Reconstructed project (from exp-04) plus the flavor switch. `Patched/` holds the exp-04 pluggable-loader patch. It is one source for both flavors: `#if BRUTAL_MANAGED_FNPTR` inside `ImportResolver.cs`. |
| `src/Brutal.Vulkan.Abstractions/Brutal.Vulkan.Abstractions.csproj` | Reconstructed project. Dependencies: Core, Collections, Logging, Memory, Numerics, Strings, CommunityToolkit.HighPerformance 8.4.2, plus `DisableRuntimeMarshalling`. |
| `tools/transform.py` | The textual transform (rules R1–R5, below). It writes only changed files, reports counts per rule and per file, and fails if any `delegate* unmanaged` or `UnmanagedCallersOnly` survives. |
| `tools/FnptrInspect` | Metadata/IL inspector. Reports function-pointer field calling conventions, `calli` call-site conventions, P/Invokes, UCO methods and `ldftn` targets. |
| `shared/ManagedIcd.cs` | The managed ICD: 41 plain static entry points, `GetInstanceProcAddr`/`GetDeviceProcAddr` returning `&Method`, and probes that record raw argument values. |
| `shared/FlavorScenario.cs` | The scenario both apps run. It prints `STEP`, `PROBE`, `BENCH`, `TIMING` and `RESULT` lines. |
| `shared/exp04-copy/` | Verbatim copies of exp-04's UCO `FakeIcd.cs` and `ManagedLoaderScenario.cs`, used for the unmanaged-flavor regression check. |
| `apps/DesktopFlavorSmoke` | Managed flavor on CoreCLR (net10.0 console). `--no-bench`, `--iters N`, `--call-null`. |
| `apps/BrowserFlavorSmoke` | Managed flavor on browser-wasm (`Microsoft.NET.Sdk.WebAssembly`, wasmbrowser template layout). Query parameters: `bench=0`, `iters=N`, `callnull=1`. |
| `apps/DesktopUnmanagedSmoke` | Unmanaged flavor from the same projects. `--uco` runs exp-04's UCO ICD; `--native` runs exp-04's MoltenVK smoke. |
| `apps/DesktopNuGetAbstractionsMismatch` | Negative check: managed-flavor Brutal.Vulkan with the **NuGet** Abstractions, showing what breaks. |
| `test/run-browser.mjs` | Headless Chromium/Edge runner. It reuses puppeteer-core from `../exp-01-dotnet-wasm-smoke/test/node_modules`. |
| `patches/` | Unified diffs against BRUTAL `a25cdf8`. `brutal-vulkan-pluggable-loader-both-flavors.patch` passes `git apply --check`. `exp04-to-exp06-ImportResolver.cs.diff` shows the `#if` delta. |
| `out/` | Logs and results. See `RESULTS.md`. |

## Transform rules (`tools/transform.py`)

| Rule | Regex | Replacement | Brutal.Vulkan | Abstractions |
|---|---|---|---|---|
| R1 | `delegate\*\s*unmanaged\s*\[\s*Cdecl\s*\]\s*<` | `delegate*<` | 2441 (all in `Generated/Functions.gen.cs`) | 1 (`VulkanHelpers.cs` global using alias) |
| R2 | `delegate\*\s*unmanaged\s*\[\s*Stdcall\s*\]\s*<` | `delegate*<` | 0 | 0 |
| R3 | `delegate\*\s*unmanaged\s*\[[^\]]*\]\s*<` (any other callconv list) | `delegate*<` | 0 | 0 |
| R4 | `delegate\*\s*unmanaged\s*<` | `delegate*<` | 0 | 0 |
| R5 | `[UnmanagedCallersOnly(…)]` (own line, or inline) | removed | 0 | 3 (`VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet/Default/Verbose`) |

* The upstream `ImportResolver.cs` holds the 2442nd occurrence. It is excluded from the copy and replaced by `Patched/ImportResolver.cs`.
* `where T : unmanaged` constraints are not touched.
* R5 can be switched off with `-p:BrutalStripUco=false`. User code that takes `&VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet` then fails with CS8786.

## Build and run

Requirements: .NET SDK 10.0.100 and `python3`. No workloads are needed (`wasm-tools` is not installed). The
`--native` mode also needs the Vulkan SDK with MoltenVK.

```sh
cd projects/exp-06-managed-fnptr-flavor

# Libraries, either flavor (apps pick their flavor themselves through ProjectReference AdditionalProperties)
dotnet build src/Brutal.Vulkan.Abstractions -c Release -p:BrutalManagedFnptr=true    # managed delegate*<…>
dotnet build src/Brutal.Vulkan.Abstractions -c Release -p:BrutalManagedFnptr=false   # upstream unmanaged (exp-04 equivalent)

# Desktop, managed flavor (no GPU, no Vulkan SDK)
dotnet build apps/DesktopFlavorSmoke -c Release
EXP06_OUT=$PWD/out dotnet apps/DesktopFlavorSmoke/bin/Release/net10.0/DesktopFlavorSmoke.dll          # RESULT: OK pass=52
dotnet apps/DesktopFlavorSmoke/bin/Release/net10.0/DesktopFlavorSmoke.dll --no-bench --call-null      # SIGSEGV, exit 139

# Desktop, unmanaged flavor from the same projects
dotnet build apps/DesktopUnmanagedSmoke -c Release
dotnet apps/DesktopUnmanagedSmoke/bin/Release/net10.0/DesktopUnmanagedSmoke.dll --uco      # exp-04 UCO ICD: RESULT: OK
dotnet apps/DesktopUnmanagedSmoke/bin/Release/net10.0/DesktopUnmanagedSmoke.dll --native   # MoltenVK: RESULT: OK

# Browser (publish = ILLink trim), then headless Chrome for Testing 151 (or --browser <Edge>)
dotnet publish apps/BrowserFlavorSmoke -c Release
node test/run-browser.mjs --out ../out/browser-run-chrome-1.txt                  # RESULT: OK pass=52 (with benchmarks)
node test/run-browser.mjs --query 'bench=0&callnull=1' --out ../out/browser-run-callnull.txt   # Mono assertion, runtime abort

# Negative check: managed-flavor Brutal.Vulkan + NuGet Abstractions
dotnet build apps/DesktopNuGetAbstractionsMismatch -c Release
dotnet apps/DesktopNuGetAbstractionsMismatch/bin/Release/net10.0/DesktopNuGetAbstractionsMismatch.dll   # MissingFieldException

# Metadata evidence
dotnet run --project tools/FnptrInspect -c Release -- <assembly.dll>...
```

## Using the managed flavor from an app

```xml
<PropertyGroup>
  <BrutalManagedFnptr>true</BrutalManagedFnptr>
  <DefineConstants>$(DefineConstants);BRUTAL_MANAGED_FNPTR</DefineConstants>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="…/src/Brutal.Vulkan/Brutal.Vulkan.csproj" AdditionalProperties="BrutalManagedFnptr=$(BrutalManagedFnptr)" />
  <ProjectReference Include="…/src/Brutal.Vulkan.Abstractions/Brutal.Vulkan.Abstractions.csproj" AdditionalProperties="BrutalManagedFnptr=$(BrutalManagedFnptr)" />
  <!-- do NOT reference the NuGet Ahwoo.BRUTAL.Vulkan(.Abstractions) managed dlls in this flavor -->
</ItemGroup>
```

```csharp
// Before anything touches Brutal.VulkanApi.Vulkan:
AppContext.SetData("BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR",
    (nint)(delegate*<VkInstance, byte*, PfnVkVoidFunction>)&MyIcd.GetInstanceProcAddr);

static PfnVkVoidFunction GetInstanceProcAddr(VkInstance instance, byte* pName) =>
    new PfnVkVoidFunction((delegate*<void>)Lookup(Marshal.PtrToStringUTF8((nint)pName)!)); // nint from (nint)(delegate*<…>)&Method
```

## Runner note

`test/run-browser.mjs` clears its timeout timer and calls `process.exit` when the page reports `EXP06 DONE`.
The exp-04 runner leaves the `setTimeout` pending, so each run keeps Node alive for the whole timeout (60 s there,
180 s in the first exp-06 copy) after the page has finished.
