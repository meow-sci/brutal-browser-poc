import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';
import { dotnetFramework } from './plugins/dotnet-framework.ts';

// Approach A (default): serve/copy the .NET `_framework/` directory unbundled.
//   DOTNET_FRAMEWORK_DIR=../dotnet-fixture/bin/Debug/net10.0/wwwroot/_framework npx vite
export default defineConfig({
  plugins: [dotnetFramework({ excludeSuffixes: process.env.DOTNET_EXCLUDE_PDB ? ['.pdb', '.map'] : [] })],
  resolve: { alias: { '#dotnet-loader': fileURLToPath(new URL('./src/loaders/static.ts', import.meta.url)) } },
  build: { target: 'es2023' },
});
