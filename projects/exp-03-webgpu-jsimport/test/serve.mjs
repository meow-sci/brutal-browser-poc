// Tiny dependency-free static server for the published AppBundle (wwwroot).
// Usage: node serve.mjs [rootDir] [port]
// Sets application/wasm for .wasm and COOP/COEP so performance.now() gets the
// fine-grained (cross-origin-isolated) timer resolution.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8', '.json': 'application/json',
  '.wasm': 'application/wasm', '.dat': 'application/octet-stream',
  '.map': 'application/json', '.css': 'text/css', '.png': 'image/png',
  '.ico': 'image/x-icon', '.pdb': 'application/octet-stream',
};

export function startServer(root, port = 0) {
  root = path.resolve(root);
  const server = http.createServer((req, res) => {
    let p = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    if (p.endsWith('/')) p += 'index.html';
    const file = path.join(root, p);
    if (!file.startsWith(root)) { res.writeHead(403); res.end(); return; }
    fs.stat(file, (err, st) => {
      if (err || !st.isFile()) { res.writeHead(404); res.end('not found: ' + p); return; }
      res.writeHead(200, {
        'Content-Type': MIME[path.extname(file)] ?? 'application/octet-stream',
        'Content-Length': st.size,
        'Cache-Control': 'no-store',
        'Cross-Origin-Opener-Policy': 'same-origin',
        'Cross-Origin-Embedder-Policy': 'require-corp',
        'Cross-Origin-Resource-Policy': 'same-origin',
      });
      fs.createReadStream(file).pipe(res);
    });
  });
  return new Promise(resolve => server.listen(port, '127.0.0.1', () => resolve(server)));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = process.argv[2] ?? path.join(here, '../WebGpuTriangle/bin/Release/net10.0/publish/wwwroot');
  const port = Number(process.argv[3] ?? 8083);
  const s = await startServer(root, port);
  console.log(`serving ${root} at http://localhost:${s.address().port}/`);
}
