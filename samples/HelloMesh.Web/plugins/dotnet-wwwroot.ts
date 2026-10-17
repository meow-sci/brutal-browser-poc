// Vite plugin of the HelloMesh page: serves (dev, preview) and copies (build) the wwwroot of the .NET browser app unbundled, so that
// main.ts can import `_framework/dotnet.js` and the libraries' JS modules (webgpu-glue.js, brutal-browser*.js) from the site root.
//
// Where the files come from (looked up again when they may have changed, so the dev server can start before the first build):
//   publish   <out>/publish/wwwroot          complete output of `npm run build:dotnet` (dotnet publish), including .br/.gz
//   build     <out>/wwwroot                  `dotnet build` puts only _framework/ here ...
//   manifest  <out>/<App>.staticwebassets.runtime.json   ... and maps every other route (the libraries' wwwroot files) to its source
// with <out> = <appDir>/bin/<configuration>/net10.0[/browser-wasm]. Dev serves the newest of publish and build+manifest (a wwwroot JS
// edit is served at once); `vite build` copies publish (else build+manifest) into dist/. A request for a plain name falls back to the
// fingerprinted file (dotnet.js -> dotnet.ab12cd34ef.js, newest first). `vite preview` negotiates the .br/.gz siblings.
import fs from 'node:fs';
import path from 'node:path';
import type { Connect, Plugin, ResolvedConfig } from 'vite';

const MIME: Record<string, string> = {
  '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.map': 'application/json',
  '.wasm': 'application/wasm', '.dat': 'application/octet-stream', '.pdb': 'application/octet-stream', '.dll': 'application/octet-stream',
  '.html': 'text/html', '.css': 'text/css', '.txt': 'text/plain', '.wgsl': 'text/plain',
};
const FINGERPRINTED = /^(.*)\.[a-z0-9]{10}(\.[A-Za-z0-9]+(?:\.(?:gz|br))?)$/; // dotnet.ab12cd34ef.js(.br) -> dotnet.js(.br)
const OWN_INDEX = /^index(?:\.[a-z0-9]{10})?\.html(?:\.br|\.gz)?$/;      // the .NET template's index.html never replaces ours
const ENTRY_ALIAS = /^(?:_framework\/dotnet|[^/]+)\.m?js(?:\.br|\.gz)?$/; // plain-named copies `vite build` writes (dotnet.js, glue JS)
const TTL_MS = 1000;                                                     // route tables are rebuilt at most once a second

type Routes = Map<string, string>; // route (relative to base) -> file
const mtime = (f: string) => { try { return fs.statSync(f).mtimeMs; } catch { return 0; } };

function dirRoutes(dir: string): Routes {
  const routes: Routes = new Map();
  if (!fs.existsSync(dir)) return routes;
  for (const e of fs.readdirSync(dir, { recursive: true, withFileTypes: true }))
    if (e.isFile() || e.isSymbolicLink()) { const f = path.join(e.parentPath, e.name); routes.set(path.relative(dir, f).split(path.sep).join('/'), f); }
  return routes;
}

interface ManifestNode { Children?: Record<string, ManifestNode> | null; Asset?: { ContentRootIndex: number; SubPath: string } | null }
function manifestRoutes(file: string): Routes {
  const routes: Routes = new Map();
  if (!fs.existsSync(file)) return routes;
  const m = JSON.parse(fs.readFileSync(file, 'utf8')) as { ContentRoots: string[]; Root: ManifestNode };
  const roots = m.ContentRoots.map((r) => path.resolve(path.dirname(file), r));
  const walk = (n: ManifestNode, prefix: string) => {
    // a SubPath with `{0}` is a template of a build-time .gz copy, not a file
    if (n.Asset && !n.Asset.SubPath.includes('{0}')) {
      const f = path.join(roots[n.Asset.ContentRootIndex]!, n.Asset.SubPath);
      if (fs.existsSync(f)) routes.set(prefix, f);
    }
    for (const [k, v] of Object.entries(n.Children ?? {})) walk(v, prefix ? `${prefix}/${k}` : k);
  };
  walk(m.Root, '');
  return routes;
}

/** Adds plain-name aliases of fingerprinted routes (the newest file wins: `dotnet publish` never deletes old ones). */
function withAliases(routes: Routes): { routes: Routes; aliases: Set<string> } {
  const aliases = new Set<string>();
  for (const [route, file] of [...routes]) {
    const plain = route.replace(FINGERPRINTED, '$1$2');
    if (plain === route || (routes.has(plain) && !aliases.has(plain))) continue;
    if (!aliases.has(plain) || mtime(file) > mtime(routes.get(plain)!)) { routes.set(plain, file); aliases.add(plain); }
  }
  return { routes, aliases };
}

export function dotnetWwwroot(): Plugin {
  const appDir = process.env.DOTNET_APP_DIR ?? '../HelloMesh/HelloMesh.Browser';
  const configuration = process.env.DOTNET_CONFIGURATION ?? 'Release';
  let config: ResolvedConfig;

  /** The .NET output folders right now. */
  const discover = () => {
    const app = path.resolve(config.root, appDir);
    const out = path.join(app, 'bin', configuration, 'net10.0');
    const rid = path.join(out, 'browser-wasm'); // present when the RuntimeIdentifier is part of the output path
    const first = (...candidates: string[]) => candidates.find((c) => fs.existsSync(c));
    // the app's manifest is named after its project; referenced libraries leave smaller ones of their own next to it
    const project = fs.existsSync(app) ? fs.readdirSync(app).find((f) => f.endsWith('.csproj'))?.slice(0, -'.csproj'.length) : undefined;
    return {
      out,
      publish: first(path.join(rid, 'publish', 'wwwroot'), path.join(out, 'publish', 'wwwroot')),
      build: first(path.join(rid, 'wwwroot'), path.join(out, 'wwwroot')),
      manifest: project ? first(path.join(rid, `${project}.staticwebassets.runtime.json`), path.join(out, `${project}.staticwebassets.runtime.json`)) : undefined,
    };
  };
  /** Routes of `dotnet build`: the build wwwroot plus the manifest (whose entries win: they point at the source files). */
  const buildRoutes = (d: ReturnType<typeof discover>): Routes =>
    new Map([...(d.build ? dirRoutes(d.build) : []), ...(d.manifest ? manifestRoutes(d.manifest) : [])]);
  /** When the build side last changed: the manifest (rewritten by every build) and the source wwwroot files outside bin/obj. */
  const buildStamp = (d: ReturnType<typeof discover>, routes: Routes) =>
    Math.max(d.manifest ? mtime(d.manifest) : 0, d.build ? mtime(path.join(d.build, '_framework')) : 0,
      ...[...routes.values()].filter((f) => !/[\\/](?:bin|obj)[\\/]/.test(f)).map(mtime));
  const describe = (d: ReturnType<typeof discover>) =>
    d.publish || d.manifest ? `publish: ${d.publish ? path.relative(config.root, d.publish) : '-'}; build manifest: ${d.manifest ? path.relative(config.root, d.manifest) : '-'}`
      : `NO .NET OUTPUT under ${path.relative(config.root, d.out)} (run \`npm run build:dotnet\` or \`dotnet build -c ${configuration}\`)`;

  /** Dev: the newest output wins (a fresh publish over an older build; a later build or a wwwroot source edit over an older publish). */
  let cache: { at: number; routes: Routes; roots: string[] } | undefined;
  const devTable = () => {
    if (cache && Date.now() - cache.at < TTL_MS) return cache;
    const d = discover();
    const build = buildRoutes(d);
    const publish = d.publish ? dirRoutes(d.publish) : new Map<string, string>();
    const publishNewer = d.publish !== undefined && Math.max(mtime(d.publish), mtime(path.join(d.publish, '_framework'))) >= buildStamp(d, build);
    const routes = publishNewer ? new Map([...build, ...publish]) : new Map([...publish, ...build]);
    const roots = [d.publish, d.build, d.manifest && path.dirname(d.manifest), ...new Set([...build.values()].filter((f) => !/[\\/](?:bin|obj)[\\/]/.test(f)).map(path.dirname))]
      .filter((r): r is string => !!r && fs.existsSync(r));
    return (cache = { at: Date.now(), routes: withAliases(routes).routes, roots });
  };

  function serve(lookup: (route: string) => string | undefined, compressed: boolean, onMissing?: (route: string, res: import('node:http').ServerResponse) => boolean): Connect.NextHandleFunction {
    return (req, res, next) => {
      if (req.method !== 'GET' && req.method !== 'HEAD') return next();
      const route = decodeURIComponent((req.url ?? '/').split('?')[0]!).replace(/^\/+/, ''); // relative to `base`
      if (!route || OWN_INDEX.test(route)) return next();
      const file = lookup(route);
      if (!file) return onMissing?.(route, res) ? undefined : next();
      let send = file;
      if (compressed) {
        const accept = String(req.headers['accept-encoding'] ?? '');
        for (const [enc, ext] of [['br', '.br'], ['gzip', '.gz']] as const) {
          const c = accept.includes(enc) ? lookup(route + ext) : undefined;
          if (c) { send = c; res.setHeader('Content-Encoding', enc); break; }
        }
        res.setHeader('Vary', 'Accept-Encoding');
      }
      let st: fs.Stats;
      try { st = fs.statSync(send); } catch { return next(); }
      const type = MIME[path.extname(route).toLowerCase()] ?? 'application/octet-stream';
      const etag = `"${st.size.toString(16)}-${Math.floor(st.mtimeMs).toString(16)}${send === file ? '' : '-c'}"`;
      res.setHeader('ETag', etag);
      res.setHeader('Cache-Control', 'no-cache'); // always revalidate (cheap 304): dotnet.js is not content-addressed for the page
      res.setHeader('Content-Type', type.startsWith('text/') ? `${type}; charset=utf-8` : type);
      if (req.headers['if-none-match'] === etag) { res.statusCode = 304; return res.end(); }
      res.setHeader('Content-Length', st.size);
      if (req.method === 'HEAD') return res.end();
      fs.createReadStream(send).pipe(res);
    };
  }
  const mount = () => config.base.replace(/\/$/, '') || '/';

  return {
    name: 'dotnet-wwwroot',
    configResolved(c) { config = c; },

    configureServer(server) {
      config.logger.info(`[dotnet-wwwroot] ${describe(discover())}; ${devTable().routes.size} routes`, { timestamp: true });
      const warned = new Set<string>();
      server.middlewares.use(mount(), serve((r) => devTable().routes.get(r), false, (route, res) => {
        // a missing .NET file is a loud 404, not Vite's SPA fallback (200 text/html breaks the runtime in obscure ways)
        if (!/^_(?:framework|content)\//.test(route)) return false;
        if (!warned.has(route)) { warned.add(route); config.logger.warn(`[dotnet-wwwroot] 404 ${route} - ${describe(discover())}`); }
        res.statusCode = 404; res.setHeader('Content-Type', 'text/plain'); res.end(`not found in the .NET output: ${route}`);
        return true;
      }));

      // full reload when the .NET output or a wwwroot source folder changes; debounced because `dotnet build` writes many files
      const watch = () => devTable().roots.forEach((r) => server.watcher.add(r));
      let timer: NodeJS.Timeout | undefined;
      const onFs = (f: string) => {
        if (!devTable().roots.some((r) => f === r || f.startsWith(r + path.sep))) return;
        cache = undefined;
        clearTimeout(timer);
        timer = setTimeout(() => { watch(); config.logger.info(`[dotnet-wwwroot] ${path.basename(f)} changed -> full reload`, { timestamp: true }); server.ws.send({ type: 'full-reload', path: '*' }); }, 300);
      };
      watch();
      server.watcher.on('add', onFs).on('change', onFs).on('unlink', onFs);
      if (!devTable().routes.size) { // started before the first build: notice when the output appears
        const poll = setInterval(() => { cache = undefined; if (devTable().routes.size) { clearInterval(poll); watch(); server.ws.send({ type: 'full-reload', path: '*' }); } }, 1500);
        poll.unref();
      }
    },

    configurePreviewServer(server) {
      // dist/ already holds the copied files; add what `vite preview` lacks: .br/.gz negotiation, MIME types, plain-name aliases.
      // Vite's own output (index.html, assets/) is left to preview.
      const dist = withAliases(dirRoutes(path.resolve(config.root, config.build.outDir))).routes;
      const assets = config.build.assetsDir.replace(/^\/|\/$/g, '') + '/';
      server.middlewares.use(mount(), serve((r) => (r.startsWith(assets) ? undefined : dist.get(r)), true));
    },

    closeBundle() {
      if (config.command !== 'build') return;
      const d = discover();
      const { routes, aliases } = withAliases(d.publish ? dirRoutes(d.publish) : buildRoutes(d));
      const list = [...routes].filter(([r]) => !OWN_INDEX.test(r) && (!aliases.has(r) || ENTRY_ALIAS.test(r)));
      if (!list.length) throw new Error(`[dotnet-wwwroot] ${describe(d)}`);
      const outDir = path.resolve(config.root, config.build.outDir);
      let bytes = 0;
      for (const [route, src] of list) {
        const dest = path.join(outDir, route);
        fs.mkdirSync(path.dirname(dest), { recursive: true });
        fs.copyFileSync(src, dest); // byte for byte: the runtime verifies integrity hashes
        bytes += fs.statSync(dest).size;
      }
      config.logger.info(`[dotnet-wwwroot] copied ${list.length} files (${(bytes / 1048576).toFixed(1)} MB) -> ${path.relative(config.root, outDir)} from ${d.publish ? 'publish' : 'build + manifest'}`);
    },
  };
}
