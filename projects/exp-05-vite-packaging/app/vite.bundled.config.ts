import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';
import DotnetWasm from 'unplugin-dotnet-wasm/vite';

// Approach B: bundler-friendly .NET output (WasmBundlerFriendlyBootConfig=true) bundled by Rolldown
// through unplugin-dotnet-wasm.   vite --config vite.bundled.config.ts
export default defineConfig({
  plugins: [
    DotnetWasm(
      process.env.DOTNET_OUTPUT_DIR
        ? // explicit mode: any build/publish output dir (e.g. `dotnet publish -o ../out/publish-bundler`)
          { projectName: 'Fixture', dotnetOutputDir: process.env.DOTNET_OUTPUT_DIR, logLevel: 'info' }
        : // discovery mode: <projectRoot>/bin/<configuration>/<tfm>[/publish]
          { projectName: 'Fixture', projectRoot: '../dotnet-fixture', configuration: 'Debug', targetFramework: 'net10.0', isPublish: false, logLevel: 'info' },
    ),
  ],
  resolve: { alias: { '#dotnet-loader': fileURLToPath(new URL('./src/loaders/bundled.ts', import.meta.url)) } },
  define: { 'import.meta.env.VITE_DOTNET_MODE': '"bundled"' },
  build: { target: 'es2023', outDir: 'dist-bundled' },
});
