// Serve (dev/preview) and copy (build) a .NET wasm `_framework/` directory, UNBUNDLED ("Approach A").
// dotnet.js stays a plain static ES module and finds its sibling files itself via import.meta.url,
// exactly like it does when served by `dotnet run` / any static host.
//
// Two ways to say where the files are:
//   frameworkDir  - a directory that already is a `_framework` (dotnet publish: bin/Release/net10.0/publish/wwwroot/_framework;
//                   .NET 10 `dotnet build`: bin/<cfg>/net10.0/wwwroot/_framework)
//   manifest      - the SDK's `<Project>.staticwebassets.runtime.json` (build) or `staticwebassets.development.json` (obj/).
//                   This is the "single source of truth" the .NET team recommends for external bundlers; it keeps working
//                   when `dotnet build` stops copying framework assets into bin/wwwroot (.NET 11).
import fs from 'node:fs';
import path from 'node:path';
import type { Connect, Plugin, ResolvedConfig } from 'vite';

export interface DotnetFrameworkOptions {
  frameworkDir?: string; // or env DOTNET_FRAMEWORK_DIR
  manifest?: string; // or env DOTNET_MANIFEST
  /** Site-root route prefixes served/copied. Default ['_framework']; in manifest mode default is every top-level
   *  route in the manifest (typically `_framework` and `_content`, the latter holds NuGet-package JS such as the
   *  Hot Reload library initializer when WasmEnableHotReload=true). */
  mounts?: string[];
  /** Also negotiate `.br`/`.gz` siblings in dev (default: only in `vite preview`). */
  precompressedInDev?: boolean;
  /** Suffixes NOT copied into dist, e.g. ['.pdb', '.map']. */
  excludeSuffixes?: string[];
}

const MIME: Record<string, string> = {
  '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm',
  '.json': 'application/json', '.map': 'application/json', '.symbols': 'text/plain',
  '.dat': 'application/octet-stream', '.pdb': 'application/octet-stream',
  '.dll': 'application/octet-stream', '.bin': 'application/octet-stream',
};

/** Maps a site-root route (e.g. `_framework/dotnet.js`) to an absolute file. */
interface Source { mounts(): string[]; resolve(route: string): string | undefined; list(): Array<[string, string]> }

function dirSource(dir: string, mount = '_framework'): Source {
  const abs = path.resolve(dir);
  if (!fs.existsSync(path.join(abs, 'dotnet.js'))) throw new Error(`dotnetFramework: no dotnet.js in ${abs}`);
  return {
    mounts: () => [mount],
    resolve(route) {
      if (!route.startsWith(mount + '/')) return undefined;
      const f = path.normalize(path.join(abs, route.slice(mount.length + 1)));
      return f.startsWith(abs + path.sep) && fs.existsSync(f) && fs.statSync(f).isFile() ? f : undefined;
    },
    list: () => fs.readdirSync(abs, { recursive: true, withFileTypes: true })
      .filter((e) => e.isFile())
      .map((e) => { const f = path.join(e.parentPath, e.name); return [`${mount}/${path.relative(abs, f).split(path.sep).join('/')}`, f] as [string, string]; }),
  };
}

interface ManifestNode { Children?: Record<string, ManifestNode> | null; Asset?: { ContentRootIndex: number; SubPath: string } | null }
function manifestSource(manifestPath: string, wanted?: string[]): Source {
  // Re-read on every call: `dotnet build` rewrites it and fingerprinted names change.
  const load = () => {
    const m = JSON.parse(fs.readFileSync(manifestPath, 'utf8')) as { ContentRoots: string[]; Root: ManifestNode };
    const out = new Map<string, string>();
    const walk = (n: ManifestNode, prefix: string) => {
      // entries whose SubPath contains `{0}` are the build-time .gz copies in obj/compressed (not servable by name)
      if (n.Asset && !n.Asset.SubPath.includes('{0}')) out.set(prefix, path.join(m.ContentRoots[n.Asset.ContentRootIndex]!, n.Asset.SubPath));
      for (const [k, v] of Object.entries(n.Children ?? {})) if (prefix || !wanted || wanted.includes(k)) walk(v, prefix ? `${prefix}/${k}` : k);
    };
    walk(m.Root, '');
    return out;
  };
  const top = () => [...new Set([...load().keys()].map((k) => k.split('/')[0]!))];
  return {
    mounts: top,
    resolve: (route) => { const f = load().get(route); return f && fs.existsSync(f) ? f : undefined; },
    list: () => [...load()].filter(([, f]) => fs.existsSync(f)),
  };
}

function makeMiddleware(src: Source, allowCompressed: boolean): Connect.NextHandleFunction {
  return (req, res, next) => {
    if (req.method !== 'GET' && req.method !== 'HEAD') return next();
    const rel = decodeURIComponent((req.url ?? '/').split('?')[0]!).replace(/^\//, ''); // route relative to `base`
    const file = rel && src.resolve(rel);
    if (!file) return next();

    let sendPath = file;
    const accept = String(req.headers['accept-encoding'] ?? '');
    if (allowCompressed) {
      for (const [enc, ext] of [['br', '.br'], ['gzip', '.gz']] as const) {
        const c = accept.includes(enc) && src.resolve(rel + ext);
        if (c) { sendPath = c; res.setHeader('Content-Encoding', enc); break; }
      }
      res.setHeader('Vary', 'Accept-Encoding');
    }
    const st = fs.statSync(sendPath);
    const etag = `"${st.size.toString(16)}-${Math.floor(st.mtimeMs).toString(16)}${sendPath === file ? '' : '-c'}"`;
    res.setHeader('ETag', etag);
    res.setHeader('Cache-Control', 'no-cache'); // dotnet.js is not fingerprinted; revalidate (cheap 304) everywhere in dev
    res.setHeader('Content-Type', MIME[path.extname(file).toLowerCase()] ?? 'application/octet-stream');
    if (req.headers['if-none-match'] === etag) { res.statusCode = 304; return res.end(); }
    res.setHeader('Content-Length', st.size);
    if (req.method === 'HEAD') return res.end();
    fs.createReadStream(sendPath).pipe(res);
  };
}

export function dotnetFramework(options: DotnetFrameworkOptions = {}): Plugin {
  const exclude = options.excludeSuffixes ?? [];
  const manifest = options.manifest ?? process.env.DOTNET_MANIFEST;
  const dir = options.frameworkDir ?? process.env.DOTNET_FRAMEWORK_DIR;
  if (!manifest && !dir) throw new Error('dotnetFramework: set frameworkDir/manifest or env DOTNET_FRAMEWORK_DIR / DOTNET_MANIFEST');
  const src = manifest ? manifestSource(path.resolve(manifest), options.mounts) : dirSource(dir!);
  let config: ResolvedConfig;

  return {
    name: 'dotnet-framework',
    configResolved(c) { config = c; },

    configureServer(server) {
      const base = config.base.replace(/\/$/, '');
      server.middlewares.use(base || '/', makeMiddleware(src, options.precompressedInDev ?? false));
      // Reload the page when the .NET build output changes (debounced: `dotnet build` writes many files).
      const watchRoot = manifest ? path.dirname(path.resolve(manifest)) : path.resolve(dir!);
      server.watcher.add(watchRoot);
      let t: NodeJS.Timeout | undefined;
      const onFs = (f: string) => {
        if (!f.startsWith(watchRoot)) return;
        clearTimeout(t);
        t = setTimeout(() => {
          config.logger.info(`[dotnet-framework] ${path.relative(watchRoot, f)} changed -> full reload`, { timestamp: true });
          server.ws.send({ type: 'full-reload', path: '*' });
        }, 300);
      };
      server.watcher.on('add', onFs).on('change', onFs);
    },

    configurePreviewServer(server) {
      // dist already holds the copied routes (closeBundle); add the .br/.gz negotiation that vite preview lacks.
      const outRoot = path.resolve(config.root, config.build.outDir);
      const dist: Source = {
        mounts: src.mounts,
        resolve(route) { const f = path.join(outRoot, route); return f.startsWith(outRoot + path.sep) && fs.existsSync(f) && fs.statSync(f).isFile() && src.mounts().some((m) => route.startsWith(m + '/')) ? f : undefined; },
        list: () => [],
      };
      server.middlewares.use(config.base.replace(/\/$/, '') || '/', makeMiddleware(dist, true));
    },

    closeBundle() {
      if (config.command !== 'build') return;
      const outRoot = path.resolve(config.root, config.build.outDir);
      for (const m of src.mounts()) fs.rmSync(path.join(outRoot, m), { recursive: true, force: true });
      let n = 0;
      for (const [route, abs] of src.list()) {
        if (exclude.some((s) => route.endsWith(s))) continue;
        const dest = path.join(outRoot, route);
        fs.mkdirSync(path.dirname(dest), { recursive: true });
        fs.copyFileSync(abs, dest); n++;
      }
      config.logger.info(`[dotnet-framework] copied ${n} files (${src.mounts().join(', ')}) -> ${path.relative(config.root, outRoot)}`);
    },
  };
}
