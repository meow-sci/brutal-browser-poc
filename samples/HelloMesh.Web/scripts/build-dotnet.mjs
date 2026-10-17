// `npm run build:dotnet`: dotnet publish of the browser project that the Vite app serves/copies.
//   DOTNET_APP_DIR        project directory, default ../HelloMesh/HelloMesh.Browser (relative to this app)
//   DOTNET_PROJECT        explicit .csproj (default: <appDir>/<last path segment of appDir>.csproj)
//   DOTNET_CONFIGURATION  default Release
// Extra arguments are passed through: npm run build:dotnet -- -p:WasmBundlerFriendlyBootConfig=false
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const appDir = path.resolve(root, process.env.DOTNET_APP_DIR ?? '../HelloMesh/HelloMesh.Browser');
const project = path.resolve(root, process.env.DOTNET_PROJECT ?? path.join(appDir, `${path.basename(appDir)}.csproj`));
const args = ['publish', project, '-c', process.env.DOTNET_CONFIGURATION ?? 'Release', ...process.argv.slice(2)];
console.log(`> dotnet ${args.join(' ')}`);
process.exit(spawnSync('dotnet', args, { stdio: 'inherit' }).status ?? 1);
