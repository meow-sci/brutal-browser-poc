// Boot of the web ICD smoke app (docs/architecture.md section 5): WebGPU adapter/device and canvas first, then the .NET
// runtime; register the "webgpu" module imports, run Main (builds the scene, prints READY), drive Tick from rAF.
import { dotnet } from './_framework/dotnet.js';
import * as webgpu from './webgpu-glue.js';

const canvas = document.getElementById('canvas');

try {
  if (!navigator.gpu) throw new Error('navigator.gpu is undefined (WebGPU unavailable)');
  const adapter = await navigator.gpu.requestAdapter();
  if (!adapter) throw new Error('navigator.gpu.requestAdapter() returned null');
  const device = await adapter.requestDevice();
  const format = navigator.gpu.getPreferredCanvasFormat();
  canvas.getContext('webgpu').configure({
    device, format, alphaMode: 'opaque',
    usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC, // COPY_SRC: window.__brutal.readPixel
  });

  let runtime = null;
  const getHeapU8 = () => runtime.localHeapViewU8(); // fresh view per access: the heap can grow
  webgpu.init({ device, canvas, format, getHeapU8 });

  runtime = await dotnet.create();
  runtime.setModuleImports('webgpu', webgpu.imports);
  const app = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).WebGpuIcdSmoke.SmokeApp;
  await runtime.runMain();

  const frame = (timestamp) => {
    let more = false;
    try {
      more = app.Tick(timestamp);
    } catch (e) {
      console.error('ERROR Tick threw: ' + (e?.stack ?? e));
    }
    if (more) requestAnimationFrame(frame);
    else console.log('HOST loop stopped');
  };
  requestAnimationFrame(frame);
} catch (e) {
  console.error('ERROR ' + (e?.stack ?? e));
}
