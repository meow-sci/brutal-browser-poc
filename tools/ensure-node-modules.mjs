// `npm ci` in tools/ once, even when several builds ask at the same time (tools/Shaders.Web.targets, EnsureToolsNodeModules).
//
//   node ensure-node-modules.mjs [npm executable]
//
// The first caller creates tools/.npm-ci.lock (exclusive create), installs and removes the lock; concurrent callers wait for the
// lock to disappear and then only check the result. A lock older than 10 minutes is treated as left over from a killed build.
import { spawnSync } from 'node:child_process';
import { existsSync, openSync, closeSync, rmSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const tools = dirname(fileURLToPath(import.meta.url));
const npm = process.argv[2] || 'npm';
const lock = join(tools, '.npm-ci.lock');
const done = () => existsSync(join(tools, 'node_modules', 'naga-wasm', 'package.json'));
const STALE_MS = 10 * 60 * 1000;
const sleep = (ms) => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);

for (;;) {
  if (done()) break;
  let fd;
  try { fd = openSync(lock, 'wx'); } catch (e) {
    if (e.code !== 'EEXIST') throw e;
    let age = 0;
    try { age = Date.now() - statSync(lock).mtimeMs; } catch { continue; }   // released between the two calls
    if (age > STALE_MS) { console.error(`removing stale ${lock}`); rmSync(lock, { force: true }); continue; }
    sleep(500);                                                               // another build is installing
    continue;
  }
  try {
    if (!done()) {
      const args = [existsSync(join(tools, 'package-lock.json')) ? 'ci' : 'install', '--prefer-offline', '--no-audit', '--no-fund'];
      console.log(`> ${npm} ${args.join(' ')}  (in ${tools})`);
      const r = spawnSync(npm, args, { cwd: tools, stdio: 'inherit', shell: process.platform === 'win32' });
      if (r.status !== 0 || !done()) {
        console.error(`error: ${npm} ${args[0]} failed in ${tools} (exit ${r.status ?? r.error?.message})`);
        process.exitCode = 1;
      }
    }
  } finally {
    closeSync(fd);
    rmSync(lock, { force: true });
  }
  break;
}
