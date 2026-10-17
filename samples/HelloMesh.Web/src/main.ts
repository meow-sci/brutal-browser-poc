// HelloMesh host page: docs/architecture.md section 5.
//   WebGPU device + canvas context -> JS modules from the .NET wwwroot -> .NET runtime -> rAF loop calling BrowserHost.Tick.
// Everything except the three module files and the .NET runtime is done here; game and ICD logic live in C#.
import type { DotnetHostBuilder } from './types/dotnet';

type GetHeap = () => Uint8Array;
/** Contract of the JS modules shipped in the .NET projects' wwwroot (architecture.md section 4). */
interface JsModule<O, I extends object = object> { init(options: O): void; imports: I }
type WebGpuModule = JsModule<{ device: GPUDevice; canvas: HTMLCanvasElement; format: GPUTextureFormat; getHeapU8: GetHeap }>;
type BrowserModule = JsModule<{ canvas: HTMLCanvasElement; getHeapU8: GetHeap }>;
/** brutal-browser-audio.js keeps `resume` (call from a user gesture) and `getState` next to its other JSImports. */
type AudioModule = JsModule<{ getHeapU8: GetHeap }, { resume?(): Promise<unknown> | unknown; getState?(): string }>;
/** `Dispatch(name)` runs a handler the game registered with `BrowserHost.RegisterCommand(name, ...)` (architecture.md section 5). */
interface BrutalExports { Brutal: { Browser: { BrowserHost: { Tick(timestampMs: number): boolean; Dispatch(command: string): void } } } }

declare global { interface Window { __brutalAudioState?: () => string; __brutalDispatch?: (command: string) => void } }

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const canvas = $<HTMLCanvasElement>('canvas');
const startBtn = $<HTMLButtonElement>('start');
const playBtn = $<HTMLButtonElement>('play-sound');
const overlay = $('overlay');
const statusEl = $('status');
const errorEl = $('error');

const base = import.meta.env.BASE_URL;
const setStatus = (s: string) => { statusEl.textContent = s; };

// ---- diagnostics: uncaught errors and the game's `ERROR ...` lines (architecture.md section 12) go to the #error overlay
const shown = new Set<string>();
function showError(message: string): void {
  if (shown.has(message)) return;
  shown.add(message);
  errorEl.hidden = false;
  errorEl.textContent += message + '\n';
}
const describe = (e: unknown) => (e instanceof Error ? (e.stack ?? e.message) : String(e));
for (const level of ['log', 'info', 'warn', 'error'] as const) {
  const original = console[level].bind(console);
  console[level] = (...args: unknown[]) => { // output is forwarded unchanged
    original(...args);
    if (/^\s*ERROR\b/.test(String(args[0]))) showError(args.map(String).join(' '));
  };
}
addEventListener('error', (e) => showError(`${e.message} (${e.filename}:${e.lineno})`));
addEventListener('unhandledrejection', (e) => showError(`Unhandled rejection: ${describe(e.reason)}`));

/** Dynamic import of a file the .NET build ships in wwwroot; served by plugins/dotnet-wwwroot.ts, never bundled. */
async function load<T>(file: string): Promise<T> {
  try { return await import(/* @vite-ignore */ new URL(base + file, document.baseURI).href) as T; }
  catch (e) { throw new Error(`Cannot load ${base}${file} (is the .NET project built? npm run build:dotnet): ${describe(e)}`); }
}

// ---- 1. WebGPU
async function initWebGpu() {
  if (!navigator.gpu) throw new Error('WebGPU is not available (navigator.gpu is undefined). Use a current Chrome or Edge on https:// or http://localhost.');
  const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
  if (!adapter) throw new Error('No WebGPU adapter (requestAdapter() returned null): blocklisted GPU, headless without GPU, or WebGPU disabled.');
  // Device policy (architecture.md section 5): ask for every feature and limit the adapter offers; the ICD reports a Vulkan
  // device clamped to its profile (Profile.cs), so games see the same limits on every machine except adapter-gated features.
  const requiredLimits: Record<string, number> = {};
  for (const k in adapter.limits) requiredLimits[k] = (adapter.limits as unknown as Record<string, number>)[k]!;
  const device = await adapter.requestDevice({ requiredFeatures: [...adapter.features] as GPUFeatureName[], requiredLimits });
  device.addEventListener('uncapturederror', (e) => showError(`WebGPU error: ${e.error.message}`));
  void device.lost.then((i) => { if (i.reason !== 'destroyed') showError(`WebGPU device lost (${i.reason}): ${i.message}`); });

  const context = canvas.getContext('webgpu');
  if (!context) throw new Error('canvas.getContext("webgpu") returned null');
  const format = navigator.gpu.getPreferredCanvasFormat();
  // brutal-browser.js keeps the canvas DPR-sized after init; give it a sane size for the first configure/frame.
  canvas.width = Math.max(1, Math.round(canvas.clientWidth * devicePixelRatio));
  canvas.height = Math.max(1, Math.round(canvas.clientHeight * devicePixelRatio));
  // Bootstrap configuration only: the ICD configures the context again in vkCreateSwapchainKHR (format, usage, view formats).
  context.configure({ device, format, alphaMode: 'opaque', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC });
  console.log(`HOST webgpu adapter=${adapter.info.vendor}/${adapter.info.architecture} format=${format} canvas=${canvas.width}x${canvas.height}`);
  return { device, format };
}

// ---- 2-4. modules, runtime, frame loop
async function boot(): Promise<void> {
  setStatus('requesting WebGPU device');
  const { device, format } = await initWebGpu();

  setStatus('loading modules');
  const [webgpu, host, audio, dotnetJs] = await Promise.all([
    load<WebGpuModule>('webgpu-glue.js'),
    load<BrowserModule>('brutal-browser.js'),
    load<AudioModule>('brutal-browser-audio.js'),
    load<{ dotnet: DotnetHostBuilder }>('_framework/dotnet.js'),
  ]);

  setStatus('starting .NET runtime');
  const runtime = await dotnetJs.dotnet.withDiagnosticTracing(false).create(); // never dotnet.run(): Main must return
  const getHeapU8 = () => runtime.localHeapViewU8(); // JS modules call this on every access (the view detaches on heap growth)
  const config = runtime.getConfig();
  const mainAssembly = config.mainAssemblyName!;

  webgpu.init({ device, canvas, format, getHeapU8 });
  host.init({ canvas, getHeapU8 });
  audio.init({ getHeapU8 });
  runtime.setModuleImports('webgpu', webgpu.imports);
  runtime.setModuleImports('brutal-browser', host.imports);
  runtime.setModuleImports('brutal-browser-audio', audio.imports);
  await runtime.runMain(mainAssembly, []); // C# Main installs the ICD, builds the game, calls BrowserHost.RunLoop and returns
  const { BrowserHost } = ((await runtime.getAssemblyExports('Brutal.Browser.dll')) as BrutalExports).Brutal.Browser;
  const tick = (ts: number) => BrowserHost.Tick(ts);

  // A click is the user gesture that lets the audio module create/resume its AudioContext. resume() must be CALLED synchronously
  // inside the handler (no await, no async work before it); its promise is only observed afterwards.
  const audioState = audio.imports.getState;
  if (audioState) window.__brutalAudioState ??= () => audioState();
  const resumeAudio = () => {
    const warn = (e: unknown) => console.warn('audio resume failed', e);
    try { void Promise.resolve(audio.imports.resume?.()).catch(warn); } catch (e) { warn(e); }
  };
  startBtn.disabled = false;
  startBtn.textContent = 'Click to start';
  startBtn.addEventListener('click', () => {
    resumeAudio(); // synchronous inside the gesture; hiding the overlay does not wait for the AudioContext to start running
    overlay.hidden = true;
    canvas.focus();
  }, { once: true });

  // Page -> game command (BrowserHost.Dispatch, registered in HelloMesh.Browser/Program.cs). The handler runs inside this click, so
  // the sound starts within the user gesture even if the context was still suspended: resume first, then dispatch.
  window.__brutalDispatch ??= (command) => BrowserHost.Dispatch(command); // test hook: the e2e dispatches an unknown command
  playBtn.disabled = false;
  playBtn.addEventListener('click', () => {
    resumeAudio();
    BrowserHost.Dispatch('play-sound');
    canvas.focus(); // keep keyboard focus off the button: Space must reach the game, not re-click the button
  });

  let count = 0, since = performance.now();
  const frame = (ts: number) => {
    try {
      if (!tick(ts)) return setStatus('stopped (Tick returned false)');
    } catch (e) {
      showError(`Tick threw: ${describe(e)}`);
      return setStatus('crashed');
    }
    if (++count % 30 === 0) { setStatus(`${mainAssembly}: ${Math.round(30000 / (ts - since))} fps`); since = ts; }
    requestAnimationFrame(frame);
  };
  requestAnimationFrame(frame);
}

boot().catch((e) => {
  console.error(`ERROR host boot failed: ${describe(e)}`); // also lands in #error through the console mirror above
  setStatus('failed');
  startBtn.textContent = 'Failed';
});
