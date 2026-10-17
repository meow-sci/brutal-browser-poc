// Tiny static server for the browser experiments. Stores the POSTed result then exits.
// Usage: node src/browser-check-server.mjs [port] [html] [resultfile]
// Serves: GET /  -> <html>;  GET /out/*, /node_modules/*, /src/* -> project files (ESM-friendly MIME types, wasm as application/wasm).
import { createServer } from 'node:http';
import { readFileSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { resolve, extname, sep } from 'node:path';
const port = Number(process.argv[2] ?? 8765);
const htmlPath = process.argv[3] ?? 'out/browser-check.html', resultPath = process.argv[4] ?? 'out/browser-check.result.json';
const root = process.cwd();
const MIME = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm', '.html': 'text/html', '.json': 'application/json', '.spv': 'application/octet-stream', '.wgsl': 'text/plain' };
const srv = createServer((req, res) => {
  if (req.method === 'POST' && req.url === '/result') {
    let body = ''; req.on('data', (d) => (body += d));
    req.on('end', () => { writeFileSync(resultPath, body); res.end('ok'); console.log('result received'); setTimeout(() => process.exit(0), 100); });
    return;
  }
  const url = decodeURIComponent(req.url.split('?')[0]);
  if (url === '/') { res.setHeader('content-type', 'text/html'); res.end(readFileSync(htmlPath)); return; }
  const file = resolve(root, '.' + url);
  if (/^\/(out|node_modules|src)\//.test(url) && file.startsWith(root + sep) && existsSync(file) && statSync(file).isFile()) {
    res.setHeader('content-type', MIME[extname(file)] ?? 'application/octet-stream'); res.end(readFileSync(file)); return;
  }
  res.statusCode = 404; res.end('not found');
}).listen(port, '127.0.0.1', () => console.log(`serving http://127.0.0.1:${port}/`));
setTimeout(() => { console.log('timeout waiting for browser result'); process.exit(1); }, 170000);
