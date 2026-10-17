---
type: Technology Assessment
title: Vite 8 overview for a vanilla-TypeScript .NET-WASM game front end
description: What Vite 8.3.4 is (Rolldown + Oxc), what changed from Vite 6/7, config/API renames, Node requirements, browser targets, wasm import semantics, Environment API status, scaffolding commands and observed install footprint.
tags: [vite, vite-8, rolldown, oxc, typescript, bundler, scaffold, node, wasm]
sources:
  - id: vite-8-announcement
    resource: https://vite.dev/blog/announcing-vite8
    title: Announcing Vite 8 (2026-03-12)
  - id: vite-migration
    resource: https://vite.dev/guide/migration
    title: Migration from v7 (Vite docs)
  - id: vite-features
    resource: https://vite.dev/guide/features
    title: Vite features (WebAssembly, Web Workers, glob/dynamic import)
  - id: vite-build-guide
    resource: https://vite.dev/guide/build
    title: Building for production (browser compatibility, base)
  - id: vite-api-environment
    resource: https://vite.dev/guide/api-environment
    title: Environment API
  - id: vite-server-options
    resource: https://vite.dev/config/server-options
    title: Server options
  - id: vite-changelog
    resource: https://github.com/vitejs/vite/blob/main/packages/vite/CHANGELOG.md
    title: Vite CHANGELOG (8.0.0 to 8.3.4)
  - id: npm-registry-vite
    resource: https://registry.npmjs.org/vite
    title: npm registry metadata for vite (queried with `npm view` on 2026-10-16)
  - id: exp05-app
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app
    title: exp-05 Vite app (scaffolded with create-vite 9.2.1)
  - id: exp05-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/README.md
    title: exp-05 README
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:20:00Z }
status: draft
---

# Vite 8 overview

## Summary

- Latest is **Vite 8.3.4**, published 2026-10-08. Vite 8.0.0 shipped 2026-03-12.[^npm-registry-vite][^vite-changelog]
- Vite 8 replaces the old esbuild (dev) + Rollup (prod) pair with a single Rust bundler, **Rolldown**, and uses **Oxc** for parsing, transforms and minification.[^vite-8-announcement]
- It needs Node `^20.19.0 || >=22.12.0`. This Mac has Node 24.18.0 and npm 12.0.2.[^npm-registry-vite][^exp05-readme]
- For this project the single most relevant 8.x change is **8.1.0 (2026-06-23): a direct `import './x.wasm'` now instantiates the module (WASM ESM integration)** instead of erroring or returning a URL. That breaks naive bundling of .NET's "bundler-friendly" `dotnet.js` (see [/web-packaging/serving-dotnet-wasm-output-with-vite.md](/web-packaging/serving-dotnet-wasm-output-with-vite.md)).[^vite-changelog][^vite-features]

## Versions and dates (npm registry, 2026-10-16)

| Item | Value |
|---|---|
| `vite` latest dist-tag | 8.3.4 (`time` 2026-10-08T12:07Z) |
| 8.0.0 | 2026-03-12 |
| 8.1.0 | 2026-06-23 (direct `.wasm` imports) |
| 8.2.0 | 2026-07-30 |
| 8.3.0 | 2026-09-10 |
| `engines.node` | `^20.19.0 \|\| >=22.12.0` |
| dependencies of 8.3.4 | `rolldown ~1.2.12` (installed: 1.2.14), `lightningcss ^1.33.0`, `postcss ^8.5.29`, `picomatch`, `tinyglobby` |
| `create-vite` | 9.2.1 (registry `modified` 2026-09-10); scaffolds `vite ^8.3.0` and `typescript ~6.0.2` |
| npm `typescript` latest | 7.0.2 (the scaffold still pins 6.0.x) |
| `@webgpu/types` | 0.1.74 |

Source for every row: `npm view` output, 2026-10-16.[^npm-registry-vite] `create-vite` scaffold contents were observed by running it.[^exp05-app]

## What changed from Vite 6/7 to 8

- **Bundler**: Rolldown for both dev dependency optimisation and production builds; the announcement claims 10 to 30 times faster builds than Rollup and quotes production builds going from 46 s to 6 s at one adopter.[^vite-8-announcement] Treat those numbers as vendor claims.
- **Compiler**: Oxc replaces esbuild for TS/JSX transforms and minification (`build.minify: 'esbuild'` still works if you install esbuild yourself). Lightning CSS is now the default CSS minifier.[^vite-migration]
- **Compat layer**: existing `esbuild` and `build.rollupOptions` config is auto-converted to `oxc` and `build.rolldownOptions`, so most Vite 7 configs load unchanged. `rolldownOptions` is the new name; `rollupOptions` is deprecated.[^vite-8-announcement][^vite-migration]
- **Default `build.target`** (`'baseline-widely-available'`) moved to Chrome/Edge 111, Firefox 114, Safari 16.4.[^vite-migration][^vite-build-guide]
- **Install size**: about 15 MB larger than Vite 7 (lightningcss about 10 MB, Rolldown binary about 5 MB).[^vite-8-announcement]
- **Removed or changed**: object-form `output.manualChunks` removed (use Rolldown `codeSplitting`); `build.commonjsOptions` is a no-op; CJS default-import interop changed (`legacy.inconsistentCjsInterop` escape hatch); `import.meta.url` is no longer polyfilled in UMD/IIFE output; `parseAst` is deprecated in favour of `parse`; `output.format` `system` and `amd` unsupported.[^vite-migration]
- **Plugin authors**: Rolldown assigns module types by extension, so a `load`/`transform` hook that returns JavaScript for a non-JS id must return `moduleType: 'js'`. `bundle[...]` assignment is not supported (use `this.emitFile()`).[^vite-migration] In JS code the URL of an emitted file is `import.meta.ROLLDOWN_FILE_URL_${referenceId}`.[^vite-features]
- **New options** worth knowing: `resolve.tsconfigPaths`, `devtools`, and `server.forwardConsole` (forwards browser console to the terminal; defaults on automatically when a coding agent is detected).[^vite-8-announcement][^vite-server-options]
- **Wasm**: `.wasm?init` gained SSR support in 8.0; direct `.wasm` imports (WASM ESM integration) landed in 8.1.0.[^vite-8-announcement][^vite-changelog]
- **Full Bundle Mode** (`experimental.bundledDev`) is experimental; it is not needed here.[^vite-8-announcement][^vite-changelog]

## Environment API

Described as a release candidate: "generally in the release candidate phase", some APIs still experimental. A client-only SPA does not need to touch it; top-level `build` and `optimizeDeps` options apply to the `client` environment automatically.[^vite-api-environment] The only place it surfaced in the experiment was a third-party plugin documenting a Node/server environment recipe.[^exp05-readme]

## Wasm semantics in Vite 8.3 (important)

- `import { add } from './add.wasm'` instantiates the module, re-exports its exports, and behaves as an async module needing top-level await.[^vite-features]
- `import init from './x.wasm?init'` returns an init function; small files are inlined as base64 under `assetsInlineLimit`.[^vite-features]
- A default import of a `.wasm` file as a *URL string* is therefore **not** a built-in behaviour; use `?url`, or a plugin that emits the file and exports its URL (this is what `unplugin-dotnet-wasm` does).[^vite-features]
- Observed: bundling .NET's bundler-friendly `dotnet.js` with no helper plugin fails in Rolldown with "Missing export" on each `import x from './Name.hash.wasm'`.[^exp05-app]

## Scaffolding (verified)

```sh
CI=1 npm create vite@latest app -- --template vanilla-ts --no-interactive   # create-vite 9.2.1
cd app && npm install                                                         # 4 s warm
npx vite --version                                                            # vite/8.3.4 darwin-arm64 node-v24.18.0
```

Scaffold contents: `index.html`, `src/main.ts`, `src/counter.ts`, `src/style.css`, `public/`, `tsconfig.json` (target `es2023`, `moduleResolution: bundler`, `verbatimModuleSyntax`, `erasableSyntaxOnly`, `types: ["vite/client"]`). Scripts are `dev`, `build` (`tsc && vite build`), `preview`.[^exp05-app]

## Observed behaviours worth remembering

- **npm 12 blocks install scripts by default**: `npm install` printed `install-scripts ... fsevents@2.3.3 ... blocked because not covered by allowScripts` and moved on. A package that needs a postinstall (for example `puppeteer`, which downloads a browser) would need `npm install-scripts approve`. `puppeteer-core` has no download step.[^exp05-app]
- **Config loader warning**: with Vite 8.3.4, a `vite.config.ts` that imports a local `.ts` file without the extension logs "uses features that are unsupported by `configLoader: 'native'`, which is planned to become the default in a future major version". Write `import ... from './plugins/x.ts'` (needs `allowImportingTsExtensions`).[^exp05-app]
- **`forwardConsole`** was active in this agent session: browser `console.warn` lines appeared in the Vite terminal as `(client) [console.warn] ...`.[^exp05-app][^vite-server-options]
- **SPA fallback**: `vite preview` answers unknown paths with `index.html` and status 200 (observed: missing `/assets/*.wasm` came back as `200 text/html`). The dev server has the same default `appType: 'spa'` fallback (UNVERIFIED by observation). A missing `.wasm` therefore arrives as `text/html`, which produces confusing errors (see the negative control in [/web-packaging/experiment-results-exp-05.md](/web-packaging/experiment-results-exp-05.md)).[^exp05-app]
- **Footprint**: `app/node_modules` is about 94 MB after adding `puppeteer-core` (8 MB), `unplugin-dotnet-wasm` (4.7 MB), `typescript` (24 MB), `@rolldown/*` binaries (17 MB), `lightningcss-darwin-arm64` (8 MB).[^exp05-app]

## Recommendations for the PoC

1. Pin `vite@^8.3` and Node >= 22.12 (24 is fine). Keep TypeScript at the scaffold's 6.0.x unless 7.x is tested with `@webgpu/types` 0.1.74.
2. Use `build.target: 'es2023'` or the default; WebGPU-capable browsers exceed the default floor anyway.
3. Add `@webgpu/types` to `compilerOptions.types` for `navigator.gpu` typings.[^exp05-app]
4. Import local config helpers with explicit `.ts` extensions.
5. Do not rely on Vite to understand .NET assets natively; pick one of the integration approaches in [/web-packaging/serving-dotnet-wasm-output-with-vite.md](/web-packaging/serving-dotnet-wasm-output-with-vite.md).

## Open questions

- UNVERIFIED: whether TypeScript 7.x (native compiler) type-checks `@webgpu/types` 0.1.74 and Vite 8.3 configs without friction; only 6.0.x was exercised.
- UNVERIFIED: whether Full Bundle Mode (`experimental.bundledDev`) behaves with large `.wasm` assets and a custom middleware; it was not tried.
- UNVERIFIED: the announcement's performance figures; they were not reproduced.

[^npm-registry-vite]: npm registry metadata for vite (queried with `npm view` on 2026-10-16)
[^vite-changelog]: Vite CHANGELOG (8.0.0 to 8.3.4)
[^vite-8-announcement]: Announcing Vite 8 (2026-03-12)
[^vite-migration]: Migration from v7 (Vite docs)
[^vite-features]: Vite features (WebAssembly, Web Workers, glob/dynamic import)
[^vite-build-guide]: Building for production (browser compatibility, base)
[^vite-api-environment]: Environment API
[^vite-server-options]: Server options
[^exp05-app]: exp-05 Vite app (scaffolded with create-vite 9.2.1)
[^exp05-readme]: exp-05 README
