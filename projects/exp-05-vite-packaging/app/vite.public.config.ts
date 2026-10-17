import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';

// Option (a): no plugin at all. The .NET `dotnet publish` output folder (`wwwroot/` containing `_framework/`) IS Vite's publicDir.
// (Experiment only: for Debug builds you would point publicDir at bin/Debug/net10.0/wwwroot.)
export default defineConfig({
  publicDir: process.env.DOTNET_PUBLIC_DIR ?? '../out/publish-plain/wwwroot',
  resolve: { alias: { '#dotnet-loader': fileURLToPath(new URL('./src/loaders/static.ts', import.meta.url)) } },
  build: { target: 'es2023', outDir: 'dist-public' },
});
