// webgpu-glue.js -- JS side of the C# [JSImport] WebGPU binding (exp-03).
//
// Design:
//  * Every WebGPU object lives in a JS-side handle table (`objs`); C# only ever
//    sees small integers (0 = null). This mirrors Vulkan's opaque `nint` handles
//    and avoids JSObject proxies (GC-tracked, ~slower to marshal).
//  * Bulk data (buffer contents, command streams, dynamic offsets) is read
//    directly from the wasm linear memory through the runtime API's
//    `localHeapViewU8/U32/F32()`. These return the *current* Emscripten HEAP*
//    views; after `memory.grow` older views are detached (byteLength 0), so we
//    re-fetch the view on every call and never cache it across calls/awaits.
//  * Complex, rare descriptors (render pipelines) are passed as JSON strings
//    with handle ids embedded; hot per-draw commands can be batched in a binary
//    command stream replayed by `replayCommandStream(ptr, byteLength)`.

let api = null;          // RuntimeAPI returned by dotnet.create()
let gpu = null;          // { adapter, device, queue, context, format }

const objs = [null];     // handle table: index = handle, 0 is null
const freeList = [];
// handle 1 is reserved for "current canvas texture view" (swapchain image)
const H_SWAPCHAIN_VIEW = 1;
objs.push(null);

export function put(o) {
  if (freeList.length) { const h = freeList.pop(); objs[h] = o; return h; }
  objs.push(o);
  return objs.length - 1;
}
export function get(h) { return objs[h]; }
export function liveHandleCount() { return objs.length - 2 - freeList.length; }

function releaseHandle(h) {
  if (h <= H_SWAPCHAIN_VIEW || h >= objs.length) return;
  const o = objs[h];
  if (o === undefined) return; // already free
  objs[h] = undefined;
  freeList.push(h);
  if (o && typeof o.destroy === 'function' && (o instanceof GPUBuffer || o instanceof GPUTexture)) o.destroy();
}

// Format enum shared with C# (WebGpu.TextureFormat)
export const FORMATS = [null, 'bgra8unorm', 'rgba8unorm', 'rgba16float', 'bgra8unorm-srgb', 'rgba8unorm-srgb'];
const LOAD_OPS = ['clear', 'load'];
const STORE_OPS = ['store', 'discard'];

// ---- heap access ---------------------------------------------------------
// localHeapViewU8() returns Module.HEAPU8 (refreshed by Emscripten on growth).
// It is cheap (no allocation) so we call it per use instead of caching.
const heapU8 = () => api.localHeapViewU8();
const heapU32 = () => api.localHeapViewU32();
const heapF32 = () => api.localHeapViewF32();

// reusable objects to keep the hot path allocation-free
const dynScratch = new Uint32Array(1);
const colorAttachment = { view: null, clearValue: { r: 0, g: 0, b: 0, a: 1 }, loadOp: 'clear', storeOp: 'store' };
const passDesc = { colorAttachments: [colorAttachment] };

function beginPassImpl(encoder, view, r, g, b, a, loadOp, storeOp) {
  colorAttachment.view = view;
  const cv = colorAttachment.clearValue; cv.r = r; cv.g = g; cv.b = b; cv.a = a;
  colorAttachment.loadOp = LOAD_OPS[loadOp];
  colorAttachment.storeOp = STORE_OPS[storeOp];
  return encoder.beginRenderPass(passDesc);
}

// ---- command stream --------------------------------------------------------
// Little-endian u32 words, 4-byte aligned. [opcode][args...]
export const OP = {
  END: 0,
  BEGIN_PASS: 1,        // viewH (0 = current canvas texture), loadOp, storeOp, r,g,b,a (f32)
  END_PASS: 2,
  SET_PIPELINE: 3,      // pipelineH
  SET_VERTEX_BUFFER: 4, // slot, bufferH, offset
  SET_BIND_GROUP: 5,    // index, bindGroupH, dynCount, dyn[dynCount]
  DRAW: 6,              // vertexCount, instanceCount, firstVertex, firstInstance
  WRITE_BUFFER: 7,      // bufferH, bufferOffset, srcPtr, size  (queue.writeBuffer, executes immediately)
  SUBMIT: 8,            // finish implicit encoder + queue.submit
};
let streamEncoder = null;
let streamPass = null;
export const streamStats = { lastReplayMs: 0, lastCommands: 0 };

function replayCommandStream(ptr, byteLength) {
  const t0 = performance.now();
  const w = heapU32();
  const f = heapF32();
  const { device, queue } = gpu;
  let i = ptr >>> 2;
  const end = (ptr + byteLength) >>> 2;
  let n = 0;
  let pass = streamPass;
  while (i < end) {
    const op = w[i++];
    n++;
    switch (op) {
      case 0: i = end; break;
      case 1: {
        if (!streamEncoder) streamEncoder = device.createCommandEncoder();
        const vh = w[i];
        const view = vh === 0 ? gpu.context.getCurrentTexture().createView() : objs[vh];
        pass = beginPassImpl(streamEncoder, view, f[i + 3], f[i + 4], f[i + 5], f[i + 6], w[i + 1], w[i + 2]);
        i += 7;
        break;
      }
      case 2: pass.end(); pass = null; break;
      case 3: pass.setPipeline(objs[w[i++]]); break;
      case 4: pass.setVertexBuffer(w[i], objs[w[i + 1]], w[i + 2]); i += 3; break;
      case 5: {
        const cnt = w[i + 2];
        if (cnt === 0) pass.setBindGroup(w[i], objs[w[i + 1]]);
        else pass.setBindGroup(w[i], objs[w[i + 1]], w, i + 3, cnt); // zero-copy dyn offsets from heap
        i += 3 + cnt;
        break;
      }
      case 6: pass.draw(w[i], w[i + 1], w[i + 2], w[i + 3]); i += 4; break;
      case 7: queue.writeBuffer(objs[w[i]], w[i + 1], heapU8(), w[i + 2], w[i + 3]); i += 4; break;
      case 8: queue.submit([streamEncoder.finish()]); streamEncoder = null; break;
      default: throw new Error(`replayCommandStream: bad opcode ${op} at word ${i - 1}`);
    }
  }
  streamPass = pass;
  streamStats.lastReplayMs = performance.now() - t0;
  streamStats.lastCommands = n;
  return n;
}

// ---- pipeline JSON ---------------------------------------------------------
function resolvePipelineJson(json) {
  const d = JSON.parse(json);
  const layout = d.layout === 'auto' ? 'auto'
    : gpu.device.createPipelineLayout({ bindGroupLayouts: d.layout.map(h => objs[h]) });
  const desc = {
    layout,
    vertex: { module: objs[d.vertex.module], entryPoint: d.vertex.entryPoint, buffers: d.vertex.buffers ?? [] },
    primitive: d.primitive ?? { topology: 'triangle-list' },
  };
  if (d.fragment) {
    desc.fragment = {
      module: objs[d.fragment.module], entryPoint: d.fragment.entryPoint,
      targets: d.fragment.targets.map(t => ({ ...t, format: typeof t.format === 'number' ? FORMATS[t.format] : t.format })),
    };
  }
  return desc;
}

// ---- imports table registered with setModuleImports('webgpu', ...) ----------
export function createImports() {
  return {
    // --- benchmark probes
    noop: (i) => i + 1,
    noop5: (a, b, c, d, e) => a + b + c + d + e,
    noopObj: (o) => (o ? 1 : 0),
    getDeviceObject: () => gpu.device,
    newObject: () => ({}),

    // --- device / queue / canvas
    getDevice: () => put(gpu.device),
    getQueue: () => put(gpu.queue),
    getPreferredFormat: () => FORMATS.indexOf(gpu.format),
    getCanvasWidth: () => gpu.context.canvas.width,
    getCanvasHeight: () => gpu.context.canvas.height,

    // --- buffers
    createBuffer: (size, usage, mappedAtCreation) =>
      put(gpu.device.createBuffer({ size, usage, mappedAtCreation })),
    copyToMappedRange: (h, offset, ptr, byteLength) => {
      const dst = new Uint8Array(objs[h].getMappedRange(offset, byteLength));
      dst.set(heapU8().subarray(ptr, ptr + byteLength));
    },
    unmapBuffer: (h) => objs[h].unmap(),
    writeBuffer: (h, bufferOffset, ptr, byteLength) =>
      gpu.queue.writeBuffer(objs[h], bufferOffset, heapU8(), ptr, byteLength),
    // Span<byte> marshalled as JSType.MemoryView (IMemoryView); slice() copies.
    writeBufferSpan: (h, bufferOffset, view) =>
      gpu.queue.writeBuffer(objs[h], bufferOffset, view.slice()),

    // --- shaders / layouts / pipelines
    createShaderModule: (code) => put(gpu.device.createShaderModule({ code })),
    createBindGroupLayoutUniform: (visibility, hasDynamicOffset, minBindingSize) =>
      put(gpu.device.createBindGroupLayout({
        entries: [{ binding: 0, visibility, buffer: { type: 'uniform', hasDynamicOffset, minBindingSize } }],
      })),
    createBindGroupUniform: (layoutH, bufH, offset, size) =>
      put(gpu.device.createBindGroup({
        layout: objs[layoutH], entries: [{ binding: 0, resource: { buffer: objs[bufH], offset, size } }],
      })),
    createRenderPipelineJson: (json) => put(gpu.device.createRenderPipeline(resolvePipelineJson(json))),
    createRenderPipelineJsonAsync: async (json) => put(await gpu.device.createRenderPipelineAsync(resolvePipelineJson(json))),

    // --- per-frame commands
    createCommandEncoder: () => put(gpu.device.createCommandEncoder()),
    getCurrentTextureView: () => { objs[H_SWAPCHAIN_VIEW] = gpu.context.getCurrentTexture().createView(); return H_SWAPCHAIN_VIEW; },
    beginRenderPass: (encH, viewH, r, g, b, a, loadOp, storeOp) =>
      put(beginPassImpl(objs[encH], objs[viewH], r, g, b, a, loadOp, storeOp)),
    setPipeline: (passH, pipeH) => objs[passH].setPipeline(objs[pipeH]),
    setVertexBuffer: (passH, slot, bufH, offset) => objs[passH].setVertexBuffer(slot, objs[bufH], offset),
    setBindGroup: (passH, index, bgH) => objs[passH].setBindGroup(index, objs[bgH]),
    setBindGroupDyn: (passH, index, bgH, dynOffset) => {
      dynScratch[0] = dynOffset;
      objs[passH].setBindGroup(index, objs[bgH], dynScratch, 0, 1);
    },
    draw: (passH, vertexCount, instanceCount, firstVertex, firstInstance) =>
      objs[passH].draw(vertexCount, instanceCount, firstVertex, firstInstance),
    // transient objects are released automatically: pass on end, encoder on finish, cmdbuf on submit
    endPass: (passH) => { objs[passH].end(); releaseHandle(passH); },
    finish: (encH) => { const cb = objs[encH].finish(); releaseHandle(encH); return put(cb); },
    submit: (cbH) => { gpu.queue.submit([objs[cbH]]); releaseHandle(cbH); },
    release: (h) => releaseHandle(h),

    // --- batched path
    replayCommandStream,
  };
}

export function initGlue(runtimeApi, gpuState) {
  api = runtimeApi;
  gpu = gpuState;
}
