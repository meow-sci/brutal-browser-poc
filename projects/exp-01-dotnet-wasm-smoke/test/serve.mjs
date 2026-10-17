// Tiny static file server with correct MIME types for .NET browser-wasm output (application/wasm etc.).
// Usage: node serve.mjs <rootDir> [port]
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json',
  '.wasm': 'application/wasm',
  '.dat': 'application/octet-stream',
  '.pdb': 'application/octet-stream',
  '.map': 'application/json',
  '.css': 'text/css',
  '.ico': 'image/x-icon',
};

export function startServer(root, port = 0, { log = () => {} } = {}) {
  root = path.resolve(root);
  const server = http.createServer((req, res) => {
    const urlPath = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    let file = path.join(root, urlPath);
    if (!file.startsWith(root)) { res.writeHead(403).end(); return; }
    if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
    fs.readFile(file, (err, data) => {
      if (err) { log(`404 ${urlPath}`); res.writeHead(404, { 'content-type': 'text/plain' }).end('not found'); return; }
      res.writeHead(200, {
        'content-type': MIME[path.extname(file).toLowerCase()] || 'application/octet-stream',
        'cache-control': 'no-store',
      });
      res.end(data);
    });
  });
  return new Promise((resolve) => server.listen(port, '127.0.0.1', () => resolve(server)));
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const root = process.argv[2] || '.';
  const port = Number(process.argv[3] || 8080);
  const s = await startServer(root, port, { log: console.log });
  console.log(`serving ${path.resolve(root)} at http://127.0.0.1:${s.address().port}/`);
}
