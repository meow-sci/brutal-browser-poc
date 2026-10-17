# exp-05-vite-packaging

Validates how a **Vite 8** (vanilla TypeScript) app can serve, bundle and test a **.NET 10 `Microsoft.NET.Sdk.WebAssembly`** (non-Blazor, Mono interpreter) application, plus **headless Chromium with WebGPU** from `puppeteer-core`.
Research notes: `docs/research/web-packaging/` (start with `experiment-results-exp-05.md`).

Nothing here is global: `node_modules` is project-local (~94 MB with `typescript`, `vite`, `puppeteer-core`, `unplugin-dotnet-wasm`, `@webgpu/types`); no browser is downloaded (the test uses browsers already on the Mac).

## Layout

| Path | What |
|---|---|
| `dotnet-fixture/` | Tiny .NET 10 wasm app: `Program.Main`, one `[JSExport] Tick(double)`, one `[JSImport("host.log","main.js")]`. `WasmEnableHotReload=false` (bundler-compatible output). `-p:BundlerFriendly=true` sets `WasmBundlerFriendlyBootConfig`. |
| `app/` | Vite 8.3.4 + TypeScript app. `src/main.ts` probes WebGPU (adapter, device, clear + GPU-buffer pixel readback) and boots .NET (`create`, `setModuleImports`, `getAssemblyExports`, `runMain`, 5 x `Tick` from rAF). Results land in `window.__e2e`. |
| `app/plugins/dotnet-framework.ts` | **Approach A** plugin: serves `_framework/` (and `_content/` in manifest mode) with correct MIME types in dev, negotiates `.br`/`.gz` in preview, copies into `dist/` on build, full-reloads the page when the .NET output changes. |
| `app/vite.config.ts` | Approach A config (default). |
| `app/vite.bundled.config.ts` | **Approach B** config: `unplugin-dotnet-wasm` bundles bundler-friendly output through Rolldown. |
| `app/vite.negative.config.ts` | Negative controls (naive static import of `dotnet.js`): expected to fail. |
| `app/vite.public.config.ts` | Option (a): no plugin, `publicDir` = the `dotnet publish` `wwwroot/`. Build/preview work; dev fails (Vite refuses `import()` of a `public/` file). |
| `app/vite.coi.config.ts` | Same as `vite.config.ts` plus COOP/COEP headers (only needed for `WasmEnableThreads`). |
| `app/test/headless.mjs` | puppeteer-core script. Exit 0 = ok, 1 = failure, 2 = usage/launch error. |
| `app/test/matrix.mjs` | Runs `headless.mjs` for every (browser x flag preset). |
| `out/` (generated) | `dotnet publish` outputs: `publish-plain/` and `publish-bundler/` (git-ignored). |

## Run

```sh
cd app && npm install

# .NET side
npm run dotnet:build            # Debug  -> ../dotnet-fixture/bin/Debug/net10.0/wwwroot/_framework  (dev loop, ~1 s incremental)
npm run dotnet:publish          # Release-> ../out/publish-plain/wwwroot/_framework (trimmed, .br + .gz)
npm run dotnet:publish:bundler  # Release, WasmBundlerFriendlyBootConfig=true -> ../out/publish-bundler

# Approach A (serve/copy _framework unbundled)
npm run dev                     # http://localhost:5173  (DOTNET_FRAMEWORK_DIR=... set by the script)
npm run dev:manifest            # same, but resolves files via <Project>.staticwebassets.runtime.json
npm run build && npm run preview

# Approach B (bundle dotnet.js with Rolldown through unplugin-dotnet-wasm)
npm run dev:bundled             # needs `dotnet build -p:BundlerFriendly=true` output (Debug)
npm run build:bundled           # uses ../out/publish-bundler

# Headless WebGPU + .NET test (starts its own Vite dev server on :5199)
npm run test:headless                              # default: Chrome for Testing 151, --enable-unsafe-webgpu
node test/headless.mjs --serve --serve-config vite.coi.config.ts --expect-dotnet --summary   # prints coi=true
NEGATIVE=plain npx vite build -c vite.negative.config.ts      # builds; boot fails at runtime (404 siblings)
node test/headless.mjs --serve --browser edge --preset none --expect-dotnet --summary
npm run test:matrix                                # browsers x presets table
```

`headless.mjs` options: `--browser cft|shell|edge|chrome|<path>`, `--preset none|unsafe|unsafe+metal|unsafe+swiftshader|unsafe+swiftshader-angle|unsafe+devfeatures|...`, `--flags="--a --b"` (note the `=`; `parseArgs` rejects a separate value that starts with `--`), `--expect-dotnet`, `--allow-no-gpu`, `--net` (per-request headers), `--screenshot out.png`, `--serve [--serve-config file]`, `--url`.

Browser paths are the ones present on this Mac (no Google Chrome installed): `cft` and `shell` are Playwright's cached Chrome for Testing 151.0.7922.34 and its headless shell (`~/Library/Caches/ms-playwright/chromium-1234`, `chromium_headless_shell-1234`), `edge` is `/Applications/Microsoft Edge.app` 155.0.4283.45.

## Results (this Mac: Apple M4 Pro, macOS arm64, Node 24.18.0, npm 12.0.2, .NET SDK 10.0.100, runtime pack 10.0.1)

See `docs/research/web-packaging/experiment-results-exp-05.md` for the full tables. Headlines:

- Approach A and B both boot the .NET runtime in a real browser from Vite 8.3.4 dev **and** build+preview output (`Main` ran, `Tick` returned 42, 5 `JSImport` callbacks).
- WebGPU in new headless Chrome for Testing 151 and Edge 155 on macOS: adapter present with **no flags** (`apple` / `metal-3`); GPU readback and screenshot both pass. `--enable-unsafe-webgpu` is harmless.
- `chrome-headless-shell` 151: no adapter without flags; `--enable-unsafe-webgpu` alone gives a SwiftShader fallback adapter whose readback is zeros; add `--use-angle=metal` (real GPU) or `--use-angle=swiftshader` (software) to get a working path.
