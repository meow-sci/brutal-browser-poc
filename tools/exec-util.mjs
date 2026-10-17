// Locating and running the native SPIR-V tools (glslc, spirv-val, spirv-opt).
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

const cache = new Map();

/** Candidate directories besides PATH: $VULKAN_SDK, ~/VulkanSDK/<newest>/{macOS/,}bin and /usr/local/bin. */
function fallbackDirs() {
  const dirs = [];
  if (process.env.VULKAN_SDK) dirs.push(join(process.env.VULKAN_SDK, 'bin'), join(process.env.VULKAN_SDK, 'macOS', 'bin'));
  const sdkRoot = join(homedir(), 'VulkanSDK');
  if (existsSync(sdkRoot)) {
    for (const v of readdirSync(sdkRoot).sort().reverse()) dirs.push(join(sdkRoot, v, 'macOS', 'bin'), join(sdkRoot, v, 'x86_64', 'bin'));
  }
  dirs.push('/usr/local/bin');
  return dirs;
}

const works = (exe) => { const r = spawnSync(exe, ['--version'], { encoding: 'utf8' }); return !r.error && r.status === 0; };

/**
 * Resolve a tool: explicit path/name (CLI option or MSBuild property) first, then $<ENV> override, then PATH, then the Vulkan SDK folders.
 * Returns the executable to spawn, or throws with the places that were searched.
 */
export function findTool(name, { explicit, env } = {}) {
  const key = `${name}|${explicit ?? ''}`;
  if (cache.has(key)) return cache.get(key);
  const envValue = env ? process.env[env] : undefined;
  const candidates = [explicit, envValue, name, ...fallbackDirs().map((d) => join(d, name))].filter(Boolean);
  for (const c of candidates) if (works(c)) { cache.set(key, c); return c; }
  throw new Error(`cannot find '${name}' (tried: ${candidates.join(', ')}). Install the Vulkan SDK / shaderc, put it on PATH${env ? ` or set ${env}` : ''}.`);
}

/** Run a command, throw with its stderr on failure, return stdout. */
export function run(cmd, args, what = cmd) {
  const r = spawnSync(cmd, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (r.error) throw new Error(`${what}: ${r.error.message}`);
  if (r.status !== 0) throw new Error(`${what} failed (exit ${r.status})\n${(r.stderr || r.stdout).trim()}`);
  return r.stdout;
}
