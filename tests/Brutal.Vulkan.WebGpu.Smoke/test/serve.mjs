// Minimal static server for the published wwwroot (application/wasm etc.). http://localhost is a secure context (WebGPU).
// Usage: node serve.mjs <rootDir> [port]
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json', '.wasm': 'application/wasm', '.dat': 'application/octet-stream', '.pdb': 'application/octet-stream',
  '.map': 'application/json', '.css': 'text/css', '.png': 'image/png',
};

export function startServer(root, port = 0) {
  root = path.resolve(root);
  const server = http.createServer((req, res) => {
    const urlPath = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    if (urlPath === '/favicon.ico') { res.writeHead(204).end(); return; }
    let file = path.join(root, urlPath);
    if (!file.startsWith(root)) { res.writeHead(403).end(); return; }
    if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
    fs.readFile(file, (err, data) => {
      if (err) { res.writeHead(404, { 'content-type': 'text/plain' }).end('not found'); return; }
      res.writeHead(200, { 'content-type': MIME[path.extname(file).toLowerCase()] || 'application/octet-stream', 'cache-control': 'no-store' });
      res.end(data);
    });
  });
  return new Promise((resolve) => server.listen(port, 'localhost', () => resolve(server)));
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const s = await startServer(process.argv[2] || '.', Number(process.argv[3] || 8080));
  console.log(`serving ${path.resolve(process.argv[2] || '.')} at http://localhost:${s.address().port}/`);
}
