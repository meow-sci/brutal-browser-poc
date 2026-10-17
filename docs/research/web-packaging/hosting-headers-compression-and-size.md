---
type: Technology Assessment
title: Hosting a Vite + .NET WebAssembly game - headers, MIME, compression, caching, size, base path
description: Which response headers a .NET 10 wasm app needs (MIME, COOP/COEP only for threads, CSP), what dotnet publish precompresses and how to serve it, cache behaviour measured in Chrome, payload sizes, sub-path hosting, and what is known (and not) about static hosts.
tags: [hosting, headers, mime, coop-coep, brotli, gzip, caching, fingerprinting, size, base-path, cloudflare, netlify, github-pages, railway]
sources:
  - id: dotnet-features-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
    title: Configuring and hosting .NET WebAssembly applications (features.md, release/10.0)
  - id: dotnet-browser-targets
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/nuget/Microsoft.NET.Sdk.WebAssembly.Pack/build/Microsoft.NET.Sdk.WebAssembly.Browser.targets
    title: Microsoft.NET.Sdk.WebAssembly.Browser.targets (compression, fingerprint defaults)
  - id: dotnet-wasm-host
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/host/DevServer/DevServerStartup.cs
    title: WasmAppHost DevServerStartup.cs (COOP/COEP policy, pathbase, static assets endpoints)
  - id: learn-migration-90-100
    resource: https://learn.microsoft.com/aspnet/core/migration/90-to-100
    title: Migrate from ASP.NET Core 9.0 to 10.0 (boot config inlined, fingerprinted client files, BlazorCacheBootResources removed)
  - id: learn-github-pages
    resource: https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/github-pages
    title: Host and deploy Blazor WebAssembly to GitHub Pages
  - id: cloudflare-compression
    resource: https://developers.cloudflare.com/speed/optimization/content/compression/
    title: Cloudflare content compression
  - id: netlify-precompressed
    resource: https://answers.netlify.com/t/serving-pre-compressed-brotli-files/53515
    title: Netlify forum - Serving pre-compressed Brotli files (dated staff answer)
  - id: vite-build-guide
    resource: https://vite.dev/guide/build
    title: Vite - building for production (base option)
  - id: vite-server-options
    resource: https://vite.dev/config/server-options
    title: Vite server options (headers)
  - id: chrome-webgpu-troubleshooting
    resource: https://developer.chrome.com/docs/web-platform/webgpu/troubleshooting-tips
    title: WebGPU troubleshooting tips (secure context, localhost)
  - id: exp05-results
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/web-packaging/experiment-results-exp-05.md
    title: Experiment results exp-05
  - id: exp05-plugin
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/plugins/dotnet-framework.ts
    title: exp-05 dotnet-framework Vite plugin
  - id: exp05-coi-config
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/vite.coi.config.ts
    title: exp-05 COOP/COEP Vite config
  - id: exp01-runtime-overview
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/dotnet-wasm/runtime-overview-net10.md
    title: "dotnet-wasm runtime overview (exp-01: payload, threads, AOT notes)"
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T17:10:00Z }
status: draft
---

# Hosting a Vite + .NET WebAssembly game

## Headers checklist

| Concern | Needed? | Detail |
|---|---|---|
| `Content-Type` | **Yes** | `.wasm` -> `application/wasm` (also needed by the runtime: it calls `WebAssembly.compileStreaming` only when the response type equals `application/wasm`, else logs "does not have the expected content type ... falling back to slower ArrayBuffer instantiation"); `.js` -> `text/javascript`; `.json` -> `application/json`; `.bin`/`.dat` -> `application/octet-stream`. Optional: `.map` `application/json`, `.symbols` `text/plain`, `.pdb`/`.dll`/`.webcil` `application/octet-stream`.[^dotnet-features-md][^exp05-results] |
| `Content-Encoding` | Optional | `dotnet publish` emits `.br` and `.gz` beside every file; the host must map `Accept-Encoding` to them and send `Content-Encoding` plus `Vary: Accept-Encoding`.[^dotnet-features-md][^exp05-results] |
| `Cache-Control` / `ETag` | Recommended | See "Caching" below.[^exp05-results] |
| COOP `same-origin` + COEP `require-corp` | **Only for `WasmEnableThreads=true`** (SharedArrayBuffer) | `features.md`: required for the multithreaded runtime; JS interop stays main-thread only. WasmAppHost applies these headers to `/_framework/*.js` and `index.html` when `WebServerUseCrossOriginPolicy` is set.[^dotnet-features-md][^dotnet-wasm-host] |
| `Cross-Origin-Resource-Policy` | Only if resources are cross-origin under COEP | Same-origin assets need nothing; a CDN origin for `_framework` combined with COEP would need `Cross-Origin-Resource-Policy: cross-origin` (or CORS). UNVERIFIED (not tested with a second origin). |
| CSP | If you use one | `features.md` suggests `Content-Security-Policy: default-src 'self' 'wasm-unsafe-eval'`; since .NET 8 the runtime is more CSP compliant.[^dotnet-features-md] |
| Secure context | **Yes for WebGPU** | `navigator.gpu` needs HTTPS or `localhost`/`127.0.0.1`.[^chrome-webgpu-troubleshooting] |

**Verified: single-threaded .NET + WebGPU need no COOP/COEP.** The default dev server ran with `crossOriginIsolated === false` and everything worked. With `server.headers` and `preview.headers` set to `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy: require-corp` (file `vite.coi.config.ts`), the page became cross-origin isolated (`true`), and the same-origin `_framework` files, the wasm and the WebGPU clear test still passed.[^exp05-coi-config][^vite-server-options][^exp05-results]

```ts
// vite.coi.config.ts (only if you later enable threads)
const coi = { 'Cross-Origin-Opener-Policy': 'same-origin', 'Cross-Origin-Embedder-Policy': 'require-corp' };
export default defineConfig(mergeConfig(base, { server: { headers: coi }, preview: { headers: coi } }));
```

## Compression

- **What the SDK emits**: `dotnet publish -c Release` wrote `.br` and `.gz` next to every file in `_framework/` (8 real files, 4,470,408 bytes; `.br` total 1,410,541 bytes; `.gz` total 1,736,096 bytes). `dotnet build -c Debug` wrote `.gz` only (180 files, 24,023,184 bytes raw; 9,177,054 bytes gz) and no `.br`.[^exp05-results] Compression is driven by the static web assets compression pipeline (`CompressionIncludePatterns` includes `_framework\**`; the sample for bundlers turns it off with `CompressionEnabled=false`).[^dotnet-browser-targets]
- **Per-file Brotli sizes (publish, invariant globalization, Mono interpreter, trimmed)**:

| File | Raw bytes | Brotli bytes |
|---|---:|---:|
| `dotnet.native.<hash>.wasm` | 3,001,957 | 976,573 |
| `System.Private.CoreLib.<hash>.wasm` | 1,021,717 | 319,385 |
| `dotnet.runtime.<hash>.js` | 198,479 | 47,846 |
| `dotnet.native.<hash>.js` | 145,050 | 30,468 |
| `dotnet.js` (with inline config) | 41,446 | 11,497 |
| `System.Runtime.InteropServices.JavaScript.<hash>.wasm` | 40,213 | 15,735 |
| `System.Console.<hash>.wasm` | 14,613 | 6,153 |
| `Fixture.<hash>.wasm` (app) | 6,933 | 2,884 |
| **Total** | **4,470,408** | **1,410,541** |

Raw sizes are file sizes (confirmed by the `Content-Length` of uncompressed responses for the wasm files).[^exp05-results] `dotnet.native.wasm` and CoreLib dominate; for a BRUTAL-sized app expect the app assemblies to join them, but no such build was made here.

- **Does Chrome ask for `br` on plain `http://localhost`?** Yes: the request carried `Accept-Encoding: gzip, deflate, br, zstd`. Our preview middleware answered with `Content-Encoding: br`, and the runtime's SRI check passed on the decoded bytes.[^exp05-results]
- **Vite does not serve `.br` siblings**: `vite preview` and `publicDir` serving returned the raw wasm (`Content-Length: 3001957`) and only gzip-encoded the JS (observed). The experiment plugin adds `br` then `gzip` negotiation to `vite preview`. For production use a real static server/CDN.[^exp05-results][^exp05-plugin]
- If you take the bundled route (Approach B), Vite writes only the raw `.wasm` files; add a compression plugin or let the CDN compress. Not tested. See [/web-packaging/serving-dotnet-wasm-output-with-vite.md](/web-packaging/serving-dotnet-wasm-output-with-vite.md).

## Caching and cache busting (measured)

- In the publish output almost everything is **content-fingerprinted** (`dotnet.native.muve7a13r4.wasm`, `System.Private.CoreLib.qumlskvajz.wasm`, `dotnet.runtime.0j6ezsi0n0.js`, `Fixture.xwt0tqcb49.wasm`); the entry `dotnet.js` is not (the boot config with all those names is inlined into it, which makes `dotnet.js` the single mutable file). `WasmFingerprintAssets` defaults to true from net9.0; `WasmFingerprintDotnetJs=true` also hashes `dotnet.js` (verified: `dotnet.yy522yscju.js`) but then the HTML must refer to it by an import map.[^dotnet-browser-targets][^exp05-results]
- .NET 10 removed the custom Blazor cache (`BlazorCacheBootResources` has no effect); the migration guide says client files are fingerprinted and cached by the browser.[^learn-migration-90-100]
- **Observed with Chrome 151**: the runtime fetches `.wasm` files with `Cache-Control: max-age=0` on the request (no-cache semantics) and JS modules with a normal import. With our ETag-based server, a page reload produced `304` for all 8 `_framework` files. So even fingerprinted wasm files are revalidated on every load (8 conditional requests for the fixture). Serving `Cache-Control: public, max-age=31536000, immutable` on hashed `.js` avoids requests for the JS; the wasm revalidation is the runtime's choice (`features.md` says it may move to the HTTP cache in future).[^exp05-results][^dotnet-features-md]
- Recommended headers: `dotnet.js` and `index.html`: `Cache-Control: no-cache` + `ETag`; hashed files: `immutable`; everything keeps `Vary: Accept-Encoding` when precompressed. Hash-named Vite assets (`assets/index-<hash>.js`) are immutable as usual.
- `features.md` also documents preload/prefetch hints for `dotnet.boot.js`, `dotnet.native.js` and `dotnet.runtime.js`; with an inline boot config the first becomes `dotnet.js`. UNVERIFIED whether preload improves start-up for this app.[^dotnet-features-md]

## Static hosts (second-hand; re-test before relying)

| Host | What is known | Confidence |
|---|---|---|
| Cloudflare (CDN/Pages/Workers) | Docs: Cloudflare keeps the origin's Brotli/gzip response when the origin sends a matching `content-encoding`, the client supports it and no content-changing features are on; `application/wasm` is in the compressible type list; `cache-control: no-transform` disables this.[^cloudflare-compression] Whether Pages/static-assets maps `.br` siblings automatically: not found. | docs for CDN behaviour; Pages UNVERIFIED |
| Netlify | Staff answer (dated): Netlify compresses with Brotli at the edge but does not use uploaded `.br` files because the request has no `.br` extension.[^netlify-precompressed] | dated, UNVERIFIED now |
| GitHub Pages | Microsoft's Blazor guidance treats it as a simple static server without Brotli support and uses a `.br` loader workaround.[^learn-github-pages] | docs; verify |
| Railway static hosting | Not researched beyond a docs search that returned no compression details. | UNVERIFIED |
| Own Caddy/nginx | Both can do `Accept-Encoding` -> `.br`/`.gz` sibling mapping (`precompressed br gzip` in Caddy, `gzip_static`/`brotli_static` in nginx); not tested here. | UNVERIFIED |

If a host serves only on-the-fly compression, plain `.wasm` is still compressed correctly as long as `application/wasm` is in its compressible list.

## Payload size reference (this Mac, net10.0, runtime pack 10.0.1)

| Variant | Raw `_framework` | Brotli | Notes |
|---|---:|---:|---|
| Fixture, Release publish (trimmed, invariant, interpreter) | 4.47 MB | 1.41 MB | measured |
| Fixture, Debug build (untrimmed, 177 resource entries) | 24.0 MB | gz 9.18 MB | measured (no `.br`) |
| ICU data files in the runtime pack | `icudt.dat` 1,526,128; `icudt_no_CJK.dat` 1,107,168; `icudt_CJK.dat` 956,416; `icudt_EFIGS.dat` 550,832 | not measured | `InvariantGlobalization=true` ships none; otherwise the runtime ships ICU and loads the user's slice (sharded) per `features.md`.[^dotnet-features-md] |
| Multithreaded runtime | `dotnet.native.wasm` 3,090,662 vs 3,001,957 single-threaded | n/a | from exp-01, needs COOP/COEP.[^exp01-runtime-overview] |
| AOT (`RunAOTCompilation`) | not measured | n/a | needs the `wasm-tools` workload (not installed); `features.md`: bigger download and slower start, publish only. UNVERIFIED sizes.[^dotnet-features-md] |

The Vite JS payload is small: `index-<hash>.js` 5.2 kB for Approach A; 326 kB (98 kB gzip) for Approach B because the runtime JS is bundled into it.[^exp05-results]

## `base` path and `vite preview`

- `base: '/games/brutal/'` rewrites asset URLs (docs: set `base` for nested deployment; `'./'` makes URLs relative).[^vite-build-guide] Verified with both approaches: `vite build --base /games/brutal/` then `vite preview --base /games/brutal/` booted .NET and passed the WebGPU check. Approach A needs `import.meta.env.BASE_URL` in the dynamic import URL and a middleware mounted under `base`; dotnet.js itself needs nothing because it locates siblings from its own URL.[^exp05-results][^exp05-plugin]
- WasmAppHost has a `pathbase` option for `dotnet run`; irrelevant to Vite.[^dotnet-wasm-host]
- `vite preview` is a local sanity check server, not a production host (it also applies on-the-fly gzip and the SPA fallback).[^exp05-results]

## Open questions

- UNVERIFIED: actual Brotli/gzip negotiation on the chosen production host (Cloudflare Pages, Railway, etc.); needs a real deploy and `curl -I -H 'Accept-Encoding: br'`.
- UNVERIFIED: transfer size and start-up time for a BRUTAL-sized assembly set, for ICU-enabled builds and for AOT.
- UNVERIFIED: whether CDN-hosted `_framework` plus COEP needs CORP headers.
- UNVERIFIED: whether the runtime's no-cache revalidation can be turned off (for example through `withResourceLoader`) to save the 8+ conditional requests.

[^dotnet-features-md]: Configuring and hosting .NET WebAssembly applications (features.md, release/10.0)
[^dotnet-browser-targets]: Microsoft.NET.Sdk.WebAssembly.Browser.targets (compression, fingerprint defaults)
[^dotnet-wasm-host]: WasmAppHost DevServerStartup.cs (COOP/COEP policy, pathbase, static assets endpoints)
[^learn-migration-90-100]: Migrate from ASP.NET Core 9.0 to 10.0 (boot config inlined, fingerprinted client files, BlazorCacheBootResources removed)
[^learn-github-pages]: Host and deploy Blazor WebAssembly to GitHub Pages
[^cloudflare-compression]: Cloudflare content compression
[^netlify-precompressed]: Netlify forum - Serving pre-compressed Brotli files (dated staff answer)
[^vite-build-guide]: Vite - building for production (base option)
[^vite-server-options]: Vite server options (headers)
[^chrome-webgpu-troubleshooting]: WebGPU troubleshooting tips (secure context, localhost)
[^exp05-results]: Experiment results exp-05
[^exp05-plugin]: exp-05 dotnet-framework Vite plugin
[^exp05-coi-config]: exp-05 COOP/COEP Vite config
[^exp01-runtime-overview]: dotnet-wasm runtime overview (exp-01: payload, threads, AOT notes)
