// Run headless.mjs for every (browser x flag preset) and print one line each. Usage: npm run test:matrix
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
const run = promisify(execFile); // async: the in-process Vite server must keep serving while the child runs
import { createServer } from 'vite';

const browsers = (process.env.BROWSERS ?? 'cft,shell,edge').split(',');
const presets = (process.env.PRESETS ?? 'none,unsafe,unsafe+metal,unsafe+swiftshader,unsafe+swiftshader-angle,unsafe+devfeatures').split(',');
const server = await createServer({ logLevel: 'warn', server: { port: 5199 } });
await server.listen();
const url = server.resolvedUrls.local[0];
for (const browser of browsers) for (const preset of presets) {
  const r = await run('node', ['test/headless.mjs', '--url', url, '--browser', browser, '--preset', preset, '--summary', '--quiet', '--allow-no-gpu', '--expect-dotnet'])
    .then((o) => ({ status: 0, ...o }), (e) => ({ status: e.code ?? 1, stdout: e.stdout ?? '', stderr: e.stderr ?? '' }));
  const line = (r.stdout.split('\n').find((l) => l.startsWith('SUMMARY')) ?? `no summary: ${r.stderr.split('\n')[0]}`)
    ;
  console.log(`${r.status === 0 ? 'PASS' : 'FAIL'} ${line}`);
}
await server.close();
