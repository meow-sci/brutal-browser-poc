# exp-04: Brutal.Vulkan rebuilt from source with a pluggable loader

This experiment rebuilds BRUTAL's `Brutal.Vulkan` from the public source tree with a small patch. The patch lets the
application supply `vkGetInstanceProcAddr` itself (a C# "ICD"), so Brutal.Vulkan no longer needs a native Vulkan
library. The patched build is then tested three ways:

* on desktop macOS with MoltenVK, on the unchanged native path;
* on desktop with a managed fake ICD;
* in a real browser (browser-wasm), driven by the same managed fake ICD.

The full write-up is `docs/research/experiments/exp-04-brutal-vulkan-source-build.md`.

The BRUTAL checkout at `/Users/asherwin/repos/meow-sci/BRUTAL` is read-only. Its `.cs` files are compiled in place.
Only the two patched files are carried here, under `src/Brutal.Vulkan/Patched/`.

## Layout

| Path | What |
|---|---|
| `Directory.Build.props`, `nuget.config` | Shared settings (`BrutalSourceRoot`, package versions). The NuGet feed is nuget.org only. |
| `src/Brutal.Vulkan/Brutal.Vulkan.csproj` | The `.csproj` rebuilt for `Brutal.Vulkan`. Switches: `-p:BrutalVulkanPatched=false` builds the pristine upstream source; `-p:BrutalAnalyzers=false` drops the Brutal.Core, Numerics and Strings analyzers. |
| `src/Brutal.Vulkan/Patched/ImportResolver.cs` | Patched copy of `Brutal.Vulkan/ImportResolver.cs`: the AppContext loader hook and `Vulkan.GlobalCommands`. |
| `src/Brutal.Vulkan/Patched/Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs` | Patched copy of the generated file. Four call sites now use `Vulkan.GlobalCommands.*`. The file keeps its CRLF line endings. |
| `patches/` | Unified diffs against BRUTAL `a25cdf8`: `brutal-vulkan-pluggable-loader.patch` (`git apply`-able from the BRUTAL root), plus one diff per file. |
| `shared/FakeIcd.cs` | Managed fake ICD. It records every proc-addr name it is asked for. It is exposed either as `[UnmanagedCallersOnly]` pointers (`UcoEntryPoints`) or through JS trampolines (browser). |
| `shared/ManagedLoaderScenario.cs` | The scenario both managed-ICD apps run: globals, instance, physical device, `DeviceEx`, queue, `AllocateBuffer`, dispose. |
| `apps/DesktopInstanceSmoke` | macOS/MoltenVK smoke test on the native loader path (patched Brutal.Vulkan plus the NuGet Abstractions). |
| `apps/ManagedLoaderSmoke` | Desktop smoke test with the managed ICD (no GPU or Vulkan SDK needed). `--call-null` calls a command whose pointer is NULL. |
| `apps/BrowserCompileCheck` | `Microsoft.NET.Sdk.WebAssembly` app (browser-wasm) using the same managed ICD. `wwwroot/main.js` creates the JS trampolines. |
| `test/run-browser.mjs` | Headless Chromium runner. It reuses puppeteer-core from `../exp-01-dotnet-wasm-smoke/test/node_modules`. |
| `tools/AsmInspect`, `tools/ApiDump` | Metadata inspectors: assembly identity, P/Invoke table, and a signature-level API listing used for the diffs. |
| `out/` | Logs and results: see below. |

## Build and run

Requirements: the .NET SDK 10.0.100. No workloads are needed (`wasm-tools` is not installed). The desktop smoke test
also needs the Vulkan SDK with MoltenVK.

```sh
cd projects/exp-04-brutal-vulkan-source-build

# 1. Library (patched; add -p:BrutalVulkanPatched=false for the pristine baseline)
dotnet build src/Brutal.Vulkan -c Release

# 2. Desktop, native loader, MoltenVK. Works with no environment variables when the SDK's system-wide install is present
#    (/usr/local/lib/libvulkan.1.dylib + /usr/local/share/vulkan/icd.d/MoltenVK_icd.json); otherwise
#    `source ~/VulkanSDK/1.4.328.1/setup-env.sh` first.
dotnet build apps/DesktopInstanceSmoke -c Release
dotnet apps/DesktopInstanceSmoke/bin/Release/net10.0/DesktopInstanceSmoke.dll

# 3. Desktop, managed ICD (writes out/instance-proc-names.txt and out/device-proc-names.txt)
dotnet build apps/ManagedLoaderSmoke -c Release
EXP04_OUT=$PWD/out dotnet apps/ManagedLoaderSmoke/bin/Release/net10.0/ManagedLoaderSmoke.dll
dotnet apps/ManagedLoaderSmoke/bin/Release/net10.0/ManagedLoaderSmoke.dll --call-null   # expect SIGSEGV, exit 139

# 4. Browser: build or publish, then run in headless Chromium
dotnet build apps/BrowserCompileCheck -c Release
dotnet publish apps/BrowserCompileCheck -c Release
node test/run-browser.mjs --mode js                       # JS trampolines -> [JSExport] C# ICD; expect RESULT: OK
node test/run-browser.mjs --mode uco                      # [UnmanagedCallersOnly] pointers are 0 without native relink
node test/run-browser.mjs --mode js --query callnull=1    # expect "RuntimeError: null function"
```

### Consuming the source-built Brutal.Vulkan next to NuGet packages

`Ahwoo.BRUTAL.Vulkan.Abstractions` pulls in `Ahwoo.BRUTAL.Vulkan` transitively. If nothing else is done, that
package's `Brutal.Vulkan.dll` **silently overwrites** the project-reference output in `bin/`, and the app runs the
unpatched assembly with no warning. Reference the package directly with `ExcludeAssets="compile;runtime"`. This keeps
only its native `libVulkanEx` assets:

```xml
<ProjectReference Include="…/src/Brutal.Vulkan/Brutal.Vulkan.csproj" />
<PackageReference Include="Ahwoo.BRUTAL.Vulkan.Abstractions" Version="2026.10.2" />
<PackageReference Include="Ahwoo.BRUTAL.Vulkan" Version="2026.10.2" ExcludeAssets="compile;runtime" />
```

## Using the loader hook

```csharp
// Before anything touches Brutal.VulkanApi.Vulkan (including Vulkan.Log):
AppContext.SetData("BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR",
    (nint)(delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction>)&MyGetInstanceProcAddr);
var instance = Vulkan.CreateInstance(new Instance.CreateInfo { … });
```

On browser-wasm without a native relink, `&UnmanagedCallersOnlyMethod` evaluates to 0. The pointer must therefore
come from JS: `Module.addFunction` wrapping a `[JSExport]` method (see `apps/BrowserCompileCheck/wwwroot/main.js`).

## out/

| File | Content |
|---|---|
| `build-baseline-pristine.log`, `build-patched.log` | Normal-verbosity library builds: 0 warnings, 0 errors. |
| `api-diff.txt` | NuGet vs pristine source: no differences. Pristine vs patched: private and internal members only. |
| `assembly-identity.txt` | Identity, attributes and P/Invoke tables of the NuGet, KSA, source-built and trimmed-browser assemblies. |
| `libvulkanex-inspection.txt` | `otool`/`nm`/`objdump` output for libVulkanEx (osx-arm64, linux-x64, win-x64, KSA Linux). |
| `desktop-instance-smoke.txt`, `desktop-instance-smoke-envmatrix.txt` | MoltenVK run (Apple M4 Pro) and the environment-variable matrix with `DYLD_PRINT_LIBRARIES`. |
| `desktop-instance-smoke-nuget-baseline.txt` | The same program against the published NuGet Brutal.Vulkan. Identical output apart from handles and versions. |
| `managed-loader-smoke.txt`, `managed-loader-call-null.txt` | Managed-ICD desktop run, and the null-pointer call (exit 139). |
| `instance-proc-names.txt`, `device-proc-names.txt` | The ordered names Brutal.Vulkan asks the ICD for (110 and 697), with whether each was implemented. |
| `browser-build.log`, `browser-publish.log`, `browser-publish-trimwarn.log` | browser-wasm build and publish (trimmed, with trim warnings unsuppressed): 0 warnings. |
| `browser-run-js.txt`, `browser-run-uco.txt`, `browser-run-js-callnull_1.txt` | Headless Chrome for Testing 151 console captures. |
