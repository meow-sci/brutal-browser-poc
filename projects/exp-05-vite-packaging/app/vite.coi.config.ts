import { defineConfig, mergeConfig } from 'vite';
import base from './vite.config.ts';

// Cross-origin isolation (only needed for WasmEnableThreads=true / SharedArrayBuffer). Same headers for dev and preview.
const coi = { 'Cross-Origin-Opener-Policy': 'same-origin', 'Cross-Origin-Embedder-Policy': 'require-corp' };
export default defineConfig(mergeConfig(base, { server: { headers: coi }, preview: { headers: coi } }));
