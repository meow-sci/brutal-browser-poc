import type { RuntimeAPI } from './vendor/dotnet';
import { loadDotnet } from '#dotnet-loader';

interface E2E {
  done: boolean;
  page: Record<string, unknown>;
  webgpu: Record<string, unknown>;
  dotnet: Record<string, unknown>;
  errors: string[];
}
declare global { interface Window { __e2e: E2E } }
const e2e: E2E = (window.__e2e = { done: false, page: { crossOriginIsolated: self.crossOriginIsolated, isSecureContext, hasSharedArrayBuffer: typeof SharedArrayBuffer !== 'undefined' }, webgpu: {}, dotnet: {}, errors: [] });

const logEl = document.getElementById('log')!;
const log = (...a: unknown[]) => {
  const s = a.map(String).join(' ');
  console.log(s);
  logEl.textContent += s + '\n';
};
addEventListener('error', (e) => e2e.errors.push(`error: ${e.message}`));
addEventListener('unhandledrejection', (e) => e2e.errors.push(`unhandledrejection: ${String(e.reason)}`));

async function pixelOfDataURL(url: string): Promise<number[]> {
  const img = new Image(); img.src = url; await img.decode();
  const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
  const g = c.getContext('2d', { willReadFrequently: true })!;
  g.drawImage(img, 0, 0);
  return Array.from(g.getImageData(img.width >> 1, img.height >> 1, 1, 1).data);
}

// ---------------------------------------------------------------- WebGPU probe
async function probeWebGPU(): Promise<void> {
  const r = e2e.webgpu;
  r.secureContext = isSecureContext;
  r.hasNavigatorGpu = 'gpu' in navigator;
  if (!navigator.gpu) return;
  r.preferredCanvasFormat = navigator.gpu.getPreferredCanvasFormat();
  const adapter = await navigator.gpu.requestAdapter();
  r.adapter = !!adapter;
  if (!adapter) return;
  const info = adapter.info;
  r.info = { vendor: info.vendor, architecture: info.architecture, device: info.device, description: info.description };
  r.isFallbackAdapter = (adapter as unknown as { isFallbackAdapter?: boolean }).isFallbackAdapter ?? info.isFallbackAdapter ?? null;
  r.featureCount = adapter.features.size;
  r.maxTextureDimension2D = adapter.limits.maxTextureDimension2D;
  const device = await adapter.requestDevice();
  r.device = true;

  // 1) Render a solid colour into the canvas texture and read the pixel back with a GPU buffer (authoritative check).
  const canvas = document.getElementById('gpu') as HTMLCanvasElement;
  const ctx = canvas.getContext('webgpu')!;
  const format = navigator.gpu.getPreferredCanvasFormat();
  ctx.configure({ device, format, alphaMode: 'opaque', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC });
  const clear = (copyTo?: GPUBuffer) => {
    const tex = ctx.getCurrentTexture();
    const enc = device.createCommandEncoder();
    enc.beginRenderPass({
      colorAttachments: [{ view: tex.createView(), clearValue: { r: 1, g: 0.5, b: 0, a: 1 }, loadOp: 'clear', storeOp: 'store' }],
    }).end();
    if (copyTo) enc.copyTextureToBuffer({ texture: tex }, { buffer: copyTo, bytesPerRow: 256 }, { width: 1, height: 1 });
    device.queue.submit([enc.finish()]);
  };
  const buf = device.createBuffer({ size: 256, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
  clear(buf);
  await buf.mapAsync(GPUMapMode.READ);
  const px = Array.from(new Uint8Array(buf.getMappedRange(0, 4)));
  buf.unmap();
  r.format = format;
  r.pixel = px; // bgra8unorm -> [0,128,255,255]; rgba8unorm -> [255,128,0,255]
  r.pixelOk = (px[0] === 255 && px[2] === 0) || (px[0] === 0 && px[2] === 255);

  // 2) Informational: reading the *canvas* (not a GPU buffer). Each probe is isolated; on some software paths a
  //    canvas readback loses the device, which must not mask the authoritative result above.
  const attempt = async (name: string, fn: () => Promise<number[]> | number[]) => {
    try { r[name] = await fn(); } catch (e) { r[name] = `ERR ${(e as Error).name}: ${(e as Error).message}`; }
  };
  clear();
  let url = '';
  await attempt('toDataURLSameTask', () => { url = canvas.toDataURL(); return pixelOfDataURL(url); });
  await attempt('drawImageSameTask', () => {
    const c2 = document.createElement('canvas'); c2.width = c2.height = 1;
    const g2 = c2.getContext('2d', { willReadFrequently: true })!;
    g2.drawImage(canvas, 0, 0, 1, 1);
    return Array.from(g2.getImageData(0, 0, 1, 1).data);
  });
  await new Promise<void>((res) => requestAnimationFrame(() => res()));
  await attempt('toDataURLNextFrame', () => pixelOfDataURL(canvas.toDataURL()));
}

// ---------------------------------------------------------------- .NET boot
async function bootDotnet(): Promise<void> {
  const r = e2e.dotnet;
  r.mode = import.meta.env.VITE_DOTNET_MODE ?? 'static';
  const builder = await loadDotnet();
  const t0 = performance.now();
  const rt: RuntimeAPI = await builder.withDiagnosticTracing(false).create();
  r.createMs = Math.round(performance.now() - t0);
  const hostLog: string[] = [];
  rt.setModuleImports('main.js', { host: { log: (s: string) => hostLog.push(s) } });
  const exports = await rt.getAssemblyExports(rt.getConfig().mainAssemblyName!);
  await rt.runMain();
  let last = 0;
  for (let i = 0; i < 5; i++) {
    await new Promise<void>((res) => requestAnimationFrame(() => res()));
    last = exports.Program.Tick(16.6 + i);
  }
  r.tickReturn = last;
  r.hostLog = hostLog;
  r.buildInfo = rt.runtimeBuildInfo;
  r.mainAssembly = rt.getConfig().mainAssemblyName;
}

for (const step of [probeWebGPU, bootDotnet]) {
  try { await step(); } catch (e) { e2e.errors.push(`${step.name}: ${(e as Error).stack ?? e}`); }
}
e2e.done = true;
log(JSON.stringify(e2e, null, 2));
