import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';

// NEGATIVE CONTROLS (expected to FAIL): import dotnet.js straight into the Rolldown graph with no helper plugin.
//   NEGATIVE=plain          npx vite build -c vite.negative.config.ts   -> builds, but sibling files 404 at runtime
//   NEGATIVE=bundlerfriendly npx vite build -c vite.negative.config.ts  -> build error: "Missing export" on `import x from './*.wasm'`
const which = process.env.NEGATIVE ?? 'plain';
export default defineConfig({
  resolve: { alias: { '#dotnet-loader': fileURLToPath(new URL(`./src/loaders/naive-${which}.ts`, import.meta.url)) } },
  build: { target: 'es2023', outDir: `dist-negative-${which}` },
});
