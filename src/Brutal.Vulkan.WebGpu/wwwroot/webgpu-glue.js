// webgpu-glue.js - JS half of the web ICD (Brutal.Vulkan.WebGpu), JS module name "webgpu" (docs/architecture.md section 4).
//
//  * WebGPU objects live in a handle table; C# only sees small integers (0 = none). Ids 1..15 are reserved (RESERVED below).
//  * Bulk data and command streams are read straight from the wasm heap: getHeapU8() is called on every access because the
//    view detaches when the heap grows.
//  * Structured descriptors (textures, pipelines, bind groups) arrive as JSON with ids embedded; they are created rarely.
//  * replayCommandStream(ptr, len) replays one vkQueueSubmit: records of little-endian u32 words, each starting with a
//    header word `opcode | (recordWords << 16)`. OP must match Opcodes.cs (a unit test checks names and values).
//  * replayCommandStream returns the number of errors waiting in pollErrors plus STATUS flags; acquireCanvasTexture/present return
//    STATUS.CanvasDeviceLost once the device is lost. STATUS must match BridgeStatus in Bridge/GpuConstants.cs.
//  * Test hooks on window.__brutal: readPixel(x, y) -> Promise<[r,g,b,a]>, frameCount, lastError.

export const OP = Object.freeze({
  End: 0,
  BeginRenderPass: 1,
  EndRenderPass: 2,
  BindPipeline: 3,
  BindDescriptorSets: 4,
  BindVertexBuffers: 5,
  BindIndexBuffer: 6,
  SetViewport: 7,
  SetScissor: 8,
  SetBlendConstants: 9,
  SetStencilReference: 10,
  Draw: 11,
  DrawIndexed: 12,
  DrawIndirect: 13,
  DrawIndexedIndirect: 14,
  Dispatch: 15,
  DispatchIndirect: 16,
  CopyBuffer: 17,
  CopyBufferToTexture: 18,
  WriteTexture: 19,
  CopyTextureToBuffer: 20,
  CopyTextureToTexture: 21,
  ClearBuffer: 22,
  WriteBuffer: 23,
  UpdateBuffer: 24,
  PushConstants: 25,
  PushDebugGroup: 26,
  PopDebugGroup: 27,
  InsertDebugMarker: 28,
  Readback: 29,
  Barrier: 30,
});

// Must match ReservedHandles in Bridge/GpuConstants.cs.
const RESERVED = Object.freeze({
  CanvasView: 1, CanvasViewSrgb: 2, CanvasTexture: 3, EmptyBindGroupLayout: 4, EmptyBindGroup: 5,
  PushUniformLayout: 6, PushStorageLayout: 7, FirstDynamic: 16,
});

// Must match BridgeStatus in Bridge/GpuConstants.cs.
const STATUS = Object.freeze({ ReplayDeviceLost: 1 << 16, ReplayOutOfMemory: 1 << 17, CanvasDeviceLost: -1 });

const LOAD_OPS = ['clear', 'load'];
const STORE_OPS = ['store', 'discard'];
const ASPECTS = ['all', 'depth-only', 'stencil-only'];
const INDEX_FORMATS = ['uint16', 'uint32'];
const PUSH_SLOT = 256;               // bytes per push-constant slot (minUniformBufferOffsetAlignment)
const PUSH_RING_SLOTS = 4096;        // initial slots (draws with changed push constants per submit); doubles when a submit needs more

let device = null, queue = null, canvas = null, context = null, getHeapU8 = null;
let preferredFormat = 'bgra8unorm', canvasFormat = null, canvasSrgbFormat = null;
let debugScopes = false, deviceLost = false, outOfMemory = false;

// ---- handle table -------------------------------------------------------------------------------------------------------
const objs = [];
const freeList = [];

function put(o) {
  if (freeList.length) { const h = freeList.pop(); objs[h] = o; return h; }
  objs.push(o);
  return objs.length - 1;
}

/** Resolves an id; the reserved canvas ids resolve to the canvas texture of the frame being replayed. */
function obj(h) {
  switch (h) {
    case RESERVED.CanvasView: return canvasView(false);
    case RESERVED.CanvasViewSrgb: return canvasView(true);
    case RESERVED.CanvasTexture: return canvasTexture();
    default: return objs[h];
  }
}

function release(h) {
  if (h < RESERVED.FirstDynamic || h >= objs.length || objs[h] === undefined) return;
  const o = objs[h];
  objs[h] = undefined;
  freeList.push(h);
  if (o instanceof GPUBuffer || o instanceof GPUTexture) o.destroy();
}

// ---- errors ---------------------------------------------------------------------------------------------------------------
const pendingErrors = [];

function reportError(message) {
  pendingErrors.push(message);
  if (pendingErrors.length > 64) pendingErrors.shift();
  window.__brutal.lastError = message;
  console.error('WEBGPU ERROR ' + message);
}

// ---- canvas -----------------------------------------------------------------------------------------------------------------
let currentTexture = null;
let currentView = null, currentSrgbView = null;

function canvasTexture() {
  if (!currentTexture) currentTexture = context.getCurrentTexture();
  return currentTexture;
}

function canvasView(srgb) {
  const t = canvasTexture();
  if (srgb) return currentSrgbView ??= t.createView({ format: canvasSrgbFormat ?? t.format });
  return currentView ??= t.createView();
}

const packSize = () => (canvas.width << 16) | (canvas.height & 0xffff);

// ---- pixel readback test hook -----------------------------------------------------------------------------------------------
const pixelRequests = [];

function readPixel(x, y) {
  return new Promise((resolve, reject) => pixelRequests.push({ x, y, resolve, reject }));
}

/** Copies the requested pixels of the frame's canvas texture before it is presented (called from present()). */
function servePixelRequests(texture) {
  if (!pixelRequests.length) return;
  const requests = pixelRequests.splice(0);
  const bgra = texture.format.startsWith('bgra');
  const encoder = device.createCommandEncoder({ label: 'readPixel' });
  const buffers = requests.map((r) => {
    const buffer = device.createBuffer({ size: 256, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
    encoder.copyTextureToBuffer({ texture, origin: { x: r.x, y: r.y } }, { buffer, bytesPerRow: 256 }, { width: 1, height: 1 });
    return buffer;
  });
  queue.submit([encoder.finish()]);
  requests.forEach((r, i) => {
    buffers[i].mapAsync(GPUMapMode.READ).then(() => {
      const p = new Uint8Array(buffers[i].getMappedRange(0, 4));
      r.resolve(bgra ? [p[2], p[1], p[0], p[3]] : [p[0], p[1], p[2], p[3]]);
      buffers[i].unmap();
      buffers[i].destroy();
    }, r.reject);
  });
}

// ---- replay state -----------------------------------------------------------------------------------------------------------
let heapBuffer = null, U32 = null, I32 = null, F32 = null, U8 = null;

function heapViews() {
  const heap = getHeapU8();
  if (heap.buffer !== heapBuffer) {
    heapBuffer = heap.buffer;
    U32 = new Uint32Array(heapBuffer);
    I32 = new Int32Array(heapBuffer);
    F32 = new Float32Array(heapBuffer);
    U8 = new Uint8Array(heapBuffer);
  }
}

let encoder = null, encoderUsed = false, encoderGroups = 0;
let pass = null, passGroups = 0, passWidth = 0, passHeight = 0;
let cpass = null;
const passArea = new Int32Array(4);
let pendingReadbacks = [];

let pushRing = null, pushRingShadow = null, pushUniformGroup = null, pushStorageGroup = null, pushRingSlots = 0;
const retiredRings = [];             // rings replaced by a bigger one during this replay; destroyed after its submit
const pushData = new Uint8Array(PUSH_SLOT);
let pushDirty = true, pushOffset = 0, ringUsed = 0, ringWritten = 0;

function makeBindState() {
  return { handle: 0, count: 0, dyn: new Uint32Array(16) }; // up to 8 dynamic uniform + 4 dynamic storage buffers per set
}

// Vulkan state shadowed per bind point (it survives render passes; WebGPU pass state does not).
const gfx = {
  pipeline: 0, push: 0, filler: 0, groupCount: 0,
  groups: [makeBindState(), makeBindState(), makeBindState(), makeBindState()],
  vb: Array.from({ length: 8 }, () => ({ handle: 0, offset: 0, size: 0 })), vbDirty: 0,
  ib: { handle: 0, format: 0, offset: 0, size: 0 }, ibDirty: false,
  viewport: new Float32Array(6), hasViewport: false, viewportDirty: false,
  scissor: new Int32Array(4), hasScissor: false, scissorDirty: false,
  blend: new Float32Array(4), hasBlend: false, blendDirty: false,
  stencilRef: 0, hasStencilRef: false, stencilDirty: false,
};
const comp = {
  pipeline: 0, push: 0, filler: 0, groupCount: 0,
  groups: [makeBindState(), makeBindState(), makeBindState(), makeBindState()],
};
// What is currently set on the open pass encoder (render or compute).
const applied = { pipeline: 0, groups: [makeBindState(), makeBindState(), makeBindState(), makeBindState()] };

function resetApplied() {
  applied.pipeline = 0;
  for (const g of applied.groups) { g.handle = -1; g.count = 0; }
}

function enc() {
  if (!encoder) { encoder = device.createCommandEncoder(); encoderUsed = false; encoderGroups = 0; }
  encoderUsed = true;
  return encoder;
}

function endCompute() {
  if (cpass) { cpass.end(); cpass = null; }
}

function writePushRing() {
  if (ringUsed > ringWritten) {
    queue.writeBuffer(pushRing, ringWritten * PUSH_SLOT, pushRingShadow, ringWritten * PUSH_SLOT, (ringUsed - ringWritten) * PUSH_SLOT);
    ringWritten = ringUsed;
  }
}

/** Submits what was recorded so far (keeps queue-level writes ordered after earlier commands of the same submit). */
function flushEncoder() {
  endCompute();
  if (pass) { reportError('replay: a queue write inside a render pass; ending the pass'); pass.end(); pass = null; }
  writePushRing();
  if (encoder) {
    while (encoderGroups-- > 0) encoder.popDebugGroup();
    if (encoderUsed) queue.submit([encoder.finish()]);
    encoder = null;
  }
}

/** The push-constant ring buffer, its CPU shadow and the two bind groups (uniform / read-only storage view) over it. */
function createPushRing(slots) {
  pushRing = device.createBuffer({ label: 'push-constant ring', size: PUSH_SLOT * slots, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
  pushRingShadow = new Uint8Array(PUSH_SLOT * slots);
  pushUniformGroup = device.createBindGroup({ layout: objs[RESERVED.PushUniformLayout], entries: [{ binding: 0, resource: { buffer: pushRing, offset: 0, size: PUSH_SLOT } }] });
  pushStorageGroup = device.createBindGroup({ layout: objs[RESERVED.PushStorageLayout], entries: [{ binding: 0, resource: { buffer: pushRing, offset: 0, size: PUSH_SLOT } }] });
  pushRingSlots = slots;
}

/**
 * A submit needs more slots than the ring has: upload the slots used so far into the current ring (the commands recorded so far
 * read them there), keep that ring alive until the submit, and continue in a ring twice the size. Slots are never reused within a
 * submit. Beyond maxBufferSize the submit fails (STATUS.ReplayOutOfMemory -> VK_ERROR_OUT_OF_DEVICE_MEMORY in C#).
 */
function growPushRing() {
  const slots = pushRingSlots * 2;
  if (PUSH_SLOT * slots > device.limits.maxBufferSize) {
    outOfMemory = true;
    throw new Error(`more than ${pushRingSlots} push-constant updates in one submit (the push-constant ring cannot grow beyond maxBufferSize)`);
  }
  writePushRing();
  retiredRings.push(pushRing);
  createPushRing(slots);
  applied.groups[3].handle = -1; // rebind group 3 from the new ring
}

function destroyRetiredRings() {
  for (const r of retiredRings) r.destroy();
  retiredRings.length = 0;
}

function pushSlotOffset() {
  if (pushDirty) {
    if (ringUsed >= pushRingSlots) growPushRing();
    pushOffset = ringUsed * PUSH_SLOT;
    pushRingShadow.set(pushData, pushOffset);
    ringUsed++;
    pushDirty = false;
  }
  return pushOffset;
}

const dynScratch = new Uint32Array(1);

/** Binds groups 0..groupCount-1 for the current pipeline: empty group for unused sets, the push ring for group 3. */
function applyGroups(encoderLike, state) {
  for (let g = 0; g < state.groupCount; g++) {
    const a = applied.groups[g];
    if (state.filler & (1 << g)) {
      if (a.handle !== RESERVED.EmptyBindGroup) { encoderLike.setBindGroup(g, objs[RESERVED.EmptyBindGroup]); a.handle = RESERVED.EmptyBindGroup; a.count = 0; }
    } else if (state.push && g === 3) {
      const offset = pushSlotOffset();
      const handle = state.push === 2 ? -3 : -2;
      if (a.handle !== handle || a.count !== 1 || a.dyn[0] !== offset) {
        dynScratch[0] = offset;
        encoderLike.setBindGroup(3, state.push === 2 ? pushStorageGroup : pushUniformGroup, dynScratch);
        a.handle = handle; a.count = 1; a.dyn[0] = offset;
      }
    } else {
      const s = state.groups[g];
      let same = a.handle === s.handle && a.count === s.count;
      for (let k = 0; same && k < s.count; k++) same = a.dyn[k] === s.dyn[k];
      if (!same) {
        if (!s.handle) throw new Error(`no descriptor set bound at set ${g}`);
        if (s.count) encoderLike.setBindGroup(g, objs[s.handle], s.dyn, 0, s.count);
        else encoderLike.setBindGroup(g, objs[s.handle]);
        a.handle = s.handle; a.count = s.count; a.dyn.set(s.dyn.subarray(0, s.count));
      }
    }
  }
}

let drawErrors = 0;

/**
 * A draw/dispatch that cannot be prepared is skipped (reported once per submit) instead of discarding the whole submit; an exhausted
 * push-constant ring is rethrown so that the whole submit fails (VK_ERROR_OUT_OF_DEVICE_MEMORY).
 */
function skipped(kind, e) {
  if (outOfMemory) throw e;
  if (drawErrors++ === 0) reportError(`replay: ${kind} skipped: ${e?.message ?? e}`);
  return false;
}

function prepareDraw() {
  try {
    prepareDrawState();
    return true;
  } catch (e) {
    return skipped('draw', e);
  }
}

function prepareDrawState() {
  if (!pass) throw new Error('draw outside a render pass');
  if (applied.pipeline !== gfx.pipeline) { pass.setPipeline(objs[gfx.pipeline]); applied.pipeline = gfx.pipeline; }
  applyGroups(pass, gfx);
  if (gfx.vbDirty) {
    for (let s = 0; s < 8; s++) {
      if (!(gfx.vbDirty & (1 << s))) continue;
      const v = gfx.vb[s];
      if (v.handle) pass.setVertexBuffer(s, objs[v.handle], v.offset, v.size || undefined);
    }
    gfx.vbDirty = 0;
  }
  if (gfx.ibDirty && gfx.ib.handle) {
    pass.setIndexBuffer(objs[gfx.ib.handle], INDEX_FORMATS[gfx.ib.format], gfx.ib.offset, gfx.ib.size || undefined);
    gfx.ibDirty = false;
  }
  if (gfx.viewportDirty && gfx.hasViewport) {
    const v = gfx.viewport;
    pass.setViewport(v[0], v[1], v[2], v[3], v[4], v[5]);
    gfx.viewportDirty = false;
  }
  if (gfx.scissorDirty) {
    // Vulkan scissors may exceed the attachment; WebGPU's must lie inside it. Also clip to the render area.
    const s = gfx.hasScissor ? gfx.scissor : passArea;
    const x0 = Math.max(s[0], passArea[0]), y0 = Math.max(s[1], passArea[1]);
    const x1 = Math.min(s[0] + s[2], passArea[0] + passArea[2], passWidth), y1 = Math.min(s[1] + s[3], passArea[1] + passArea[3], passHeight);
    pass.setScissorRect(Math.min(x0, passWidth), Math.min(y0, passHeight), Math.max(0, x1 - x0), Math.max(0, y1 - y0));
    gfx.scissorDirty = false;
  }
  if (gfx.blendDirty && gfx.hasBlend) {
    const b = gfx.blend;
    pass.setBlendConstant({ r: b[0], g: b[1], b: b[2], a: b[3] });
    gfx.blendDirty = false;
  }
  if (gfx.stencilDirty && gfx.hasStencilRef) { pass.setStencilReference(gfx.stencilRef); gfx.stencilDirty = false; }
}

function prepareDispatch() {
  try {
    if (pass) throw new Error('dispatch inside a render pass');
    if (!cpass) { cpass = enc().beginComputePass(); resetApplied(); }
    if (applied.pipeline !== comp.pipeline) { cpass.setPipeline(objs[comp.pipeline]); applied.pipeline = comp.pipeline; }
    applyGroups(cpass, comp);
    return true;
  } catch (e) {
    return skipped('dispatch', e);
  }
}

/** Vulkan dynamic state is per command buffer: forget the previous submit's bindings. */
function resetState() {
  for (const s of [gfx, comp]) {
    s.pipeline = s.push = s.filler = s.groupCount = 0;
    for (const g of s.groups) { g.handle = 0; g.count = 0; }
  }
  for (const v of gfx.vb) v.handle = 0;
  gfx.ib.handle = 0;
  gfx.hasViewport = gfx.hasScissor = gfx.hasBlend = gfx.hasStencilRef = false;
}

function markPassStateDirty() {
  resetApplied();
  gfx.vbDirty = 0xff;
  gfx.ibDirty = true;
  gfx.viewportDirty = gfx.scissorDirty = gfx.blendDirty = gfx.stencilDirty = true;
}

const decoder = new TextDecoder();
const label = (byteOffset, length) => decoder.decode(U8.slice(byteOffset, byteOffset + length));

// ---- replay -----------------------------------------------------------------------------------------------------------------
function beginRenderPass(i) {
  endCompute();
  const colorCount = U32[i], flags = U32[i + 1];
  passArea[0] = U32[i + 2]; passArea[1] = U32[i + 3]; passArea[2] = U32[i + 4]; passArea[3] = U32[i + 5];
  passWidth = U32[i + 6]; passHeight = U32[i + 7];
  const colorAttachments = [];
  let q = i + 8;
  for (let c = 0; c < colorCount; c++, q += 9) {
    const view = U32[q];
    if (!view) { colorAttachments.push(null); continue; }
    const kind = U32[q + 4];
    const src = kind === 0 ? F32 : kind === 1 ? I32 : U32;
    const a = {
      view: obj(view), loadOp: LOAD_OPS[U32[q + 2]], storeOp: STORE_OPS[U32[q + 3]],
      clearValue: { r: src[q + 5], g: src[q + 6], b: src[q + 7], a: src[q + 8] },
    };
    if (U32[q + 1]) a.resolveTarget = obj(U32[q + 1]);
    colorAttachments.push(a);
  }
  const desc = { colorAttachments };
  if (flags & 1) {
    const d = { view: obj(U32[q]) };
    if (U32[q + 1] < 2) { d.depthLoadOp = LOAD_OPS[U32[q + 1]]; d.depthStoreOp = STORE_OPS[U32[q + 2]]; d.depthClearValue = F32[q + 3]; }
    if (U32[q + 4] < 2) { d.stencilLoadOp = LOAD_OPS[U32[q + 4]]; d.stencilStoreOp = STORE_OPS[U32[q + 5]]; d.stencilClearValue = U32[q + 6]; }
    if (U32[q + 7] & 1) d.depthReadOnly = true;
    if (U32[q + 7] & 2) d.stencilReadOnly = true;
    desc.depthStencilAttachment = d;
  }
  pass = enc().beginRenderPass(desc);
  passGroups = 0;
  markPassStateDirty();
}

function endRenderPass() {
  if (!pass) return;
  while (passGroups-- > 0) pass.popDebugGroup();
  passGroups = 0;
  pass.end();
  pass = null;
}

/** Buffer side of a copy: WebGPU wants bytesPerRow (a multiple of 256) only for multi-row copies, rowsPerImage only for 3D/layers. */
function bufferCopy(buffer, offset, bytesPerRow, rowsPerImage, height, depth) {
  const b = { buffer, offset };
  if (height > 1 || depth > 1) b.bytesPerRow = bytesPerRow;
  if (depth > 1) b.rowsPerImage = rowsPerImage;
  return b;
}

function textureCopy(handle, i) {
  return { texture: obj(handle), mipLevel: U32[i], origin: { x: U32[i + 1], y: U32[i + 2], z: U32[i + 3] }, aspect: ASPECTS[U32[i + 4]] };
}

function execute(op, i, len) {
  switch (op) {
    case OP.BeginRenderPass: beginRenderPass(i); break;
    case OP.EndRenderPass: endRenderPass(); break;
    case OP.BindPipeline: {
      const s = U32[i + 1] === 1 ? comp : gfx;
      s.pipeline = U32[i]; s.push = U32[i + 2]; s.filler = U32[i + 3]; s.groupCount = U32[i + 4];
      break;
    }
    case OP.BindDescriptorSets: {
      const s = U32[i] === 1 ? comp : gfx;
      const first = U32[i + 1], count = U32[i + 2];
      let q = i + 3;
      for (let k = 0; k < count; k++) {
        const g = s.groups[first + k], n = U32[q + 1];
        g.handle = U32[q]; g.count = n;
        for (let d = 0; d < n; d++) g.dyn[d] = U32[q + 2 + d];
        q += 2 + n;
      }
      break;
    }
    case OP.BindVertexBuffers: {
      const first = U32[i], count = U32[i + 1];
      for (let k = 0; k < count; k++) {
        const v = gfx.vb[first + k], q = i + 2 + 3 * k;
        v.handle = U32[q]; v.offset = U32[q + 1]; v.size = U32[q + 2];
        gfx.vbDirty |= 1 << (first + k);
      }
      break;
    }
    case OP.BindIndexBuffer:
      gfx.ib.handle = U32[i]; gfx.ib.offset = U32[i + 1]; gfx.ib.format = U32[i + 2]; gfx.ib.size = U32[i + 3];
      gfx.ibDirty = true;
      break;
    case OP.SetViewport:
      for (let k = 0; k < 6; k++) gfx.viewport[k] = F32[i + k];
      gfx.hasViewport = gfx.viewportDirty = true;
      break;
    case OP.SetScissor:
      for (let k = 0; k < 4; k++) gfx.scissor[k] = I32[i + k];
      gfx.hasScissor = gfx.scissorDirty = true;
      break;
    case OP.SetBlendConstants:
      for (let k = 0; k < 4; k++) gfx.blend[k] = F32[i + k];
      gfx.hasBlend = gfx.blendDirty = true;
      break;
    case OP.SetStencilReference: gfx.stencilRef = U32[i]; gfx.hasStencilRef = gfx.stencilDirty = true; break;
    case OP.Draw: if (prepareDraw()) pass.draw(U32[i], U32[i + 1], U32[i + 2], U32[i + 3]); break;
    case OP.DrawIndexed: if (prepareDraw()) pass.drawIndexed(U32[i], U32[i + 1], U32[i + 2], I32[i + 3], U32[i + 4]); break;
    case OP.DrawIndirect:
    case OP.DrawIndexedIndirect: {
      if (!prepareDraw()) break;
      const buffer = objs[U32[i]], offset = U32[i + 1], count = U32[i + 2], stride = U32[i + 3];
      for (let k = 0; k < count; k++) {
        if (op === OP.DrawIndirect) pass.drawIndirect(buffer, offset + k * stride);
        else pass.drawIndexedIndirect(buffer, offset + k * stride);
      }
      break;
    }
    case OP.Dispatch: if (prepareDispatch()) cpass.dispatchWorkgroups(U32[i], U32[i + 1], U32[i + 2]); break;
    case OP.DispatchIndirect: if (prepareDispatch()) cpass.dispatchWorkgroupsIndirect(objs[U32[i]], U32[i + 1]); break;
    case OP.CopyBuffer: endCompute(); enc().copyBufferToBuffer(objs[U32[i]], U32[i + 1], objs[U32[i + 2]], U32[i + 3], U32[i + 4]); break;
    case OP.CopyBufferToTexture:
      endCompute();
      enc().copyBufferToTexture(bufferCopy(objs[U32[i]], U32[i + 1], U32[i + 2], U32[i + 3], U32[i + 11], U32[i + 12]),
        textureCopy(U32[i + 4], i + 5), { width: U32[i + 10], height: U32[i + 11], depthOrArrayLayers: U32[i + 12] });
      break;
    case OP.WriteTexture:
      if (encoder && encoderUsed) flushEncoder(); // keep the commands recorded before this copy ahead of it
      queue.writeTexture(textureCopy(U32[i + 4], i + 5), U8, { offset: U32[i], bytesPerRow: U32[i + 2], rowsPerImage: U32[i + 3] },
        { width: U32[i + 10], height: U32[i + 11], depthOrArrayLayers: U32[i + 12] });
      break;
    case OP.CopyTextureToBuffer:
      endCompute();
      enc().copyTextureToBuffer(textureCopy(U32[i], i + 1), bufferCopy(objs[U32[i + 6]], U32[i + 7], U32[i + 8], U32[i + 9], U32[i + 11], U32[i + 12]),
        { width: U32[i + 10], height: U32[i + 11], depthOrArrayLayers: U32[i + 12] });
      break;
    case OP.CopyTextureToTexture:
      endCompute();
      enc().copyTextureToTexture(textureCopy(U32[i], i + 1), textureCopy(U32[i + 6], i + 7),
        { width: U32[i + 12], height: U32[i + 13], depthOrArrayLayers: U32[i + 14] });
      break;
    case OP.ClearBuffer: endCompute(); enc().clearBuffer(objs[U32[i]], U32[i + 1], U32[i + 2]); break;
    case OP.WriteBuffer:
      if (encoder && encoderUsed) flushEncoder();
      queue.writeBuffer(objs[U32[i]], U32[i + 1], U8, U32[i + 2], U32[i + 3]);
      break;
    case OP.UpdateBuffer:
      if (encoder && encoderUsed) flushEncoder();
      queue.writeBuffer(objs[U32[i]], U32[i + 1], U8, (i + 3) * 4, U32[i + 2]);
      break;
    case OP.PushConstants: {
      const offset = U32[i], size = U32[i + 1], at = (i + 2) * 4;
      pushData.set(U8.subarray(at, at + size), offset);
      pushDirty = true;
      break;
    }
    case OP.PushDebugGroup: {
      const text = label((i + 1) * 4, U32[i]);
      if (pass) { pass.pushDebugGroup(text); passGroups++; } else { endCompute(); enc().pushDebugGroup(text); encoderGroups++; }
      break;
    }
    case OP.PopDebugGroup:
      if (pass) { if (passGroups > 0) { pass.popDebugGroup(); passGroups--; } } else if (encoder && encoderGroups > 0) { endCompute(); encoder.popDebugGroup(); encoderGroups--; }
      break;
    case OP.InsertDebugMarker: {
      const text = label((i + 1) * 4, U32[i]);
      if (pass) pass.insertDebugMarker(text); else { endCompute(); enc().insertDebugMarker(text); }
      break;
    }
    case OP.Readback: {
      endCompute();
      const size = U32[i + 2], size4 = (size + 3) & ~3;
      const staging = device.createBuffer({ size: Math.max(4, size4), usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
      enc().copyBufferToBuffer(objs[U32[i]], U32[i + 1], staging, 0, size4);
      pendingReadbacks.push({ staging, size, dst: U32[i + 3], status: U32[i + 4] });
      break;
    }
    case OP.Barrier: break;
    default: throw new Error(`unknown opcode ${op}`);
  }
}

function replayCommandStream(ptr, byteLength) {
  if (deviceLost) return pendingErrors.length | STATUS.ReplayDeviceLost;
  heapViews();
  if (debugScopes) device.pushErrorScope('validation');
  outOfMemory = false;
  ringUsed = ringWritten = 0;
  pushDirty = true;
  pendingReadbacks = [];
  drawErrors = 0;
  resetState();
  let i = ptr >>> 2, op = -1;
  const end = (ptr + byteLength) >>> 2;
  try {
    while (i < end) {
      const header = U32[i];
      op = header & 0xffff;
      const len = header >>> 16;
      if (op === OP.End || len === 0) break;
      execute(op, i + 1, len);
      i += len;
    }
    if (pass) { reportError('replay: a render pass was not ended (missing vkCmdEndRendering)'); endRenderPass(); }
    flushEncoder();
    destroyRetiredRings();
  } catch (e) {
    destroyRetiredRings();
    reportError(`replay: ${e?.message ?? e} (opcode ${op})`);
    try { if (pass) pass.end(); } catch { }
    try { if (cpass) cpass.end(); } catch { }
    pass = cpass = null;
    encoder = null;
    // the encoder was dropped: no readback of this submit will carry data. Fail them all, including those not reached yet.
    const status = new Int32Array(getHeapU8().buffer);
    for (const r of pendingReadbacks) { status[r.status >>> 2] = -1; r.staging.destroy(); }
    pendingReadbacks = [];
    for (let j = i; j < end;) {
      const header = U32[j], len = header >>> 16;
      if ((header & 0xffff) === OP.End || len === 0) break;
      if ((header & 0xffff) === OP.Readback) status[U32[j + 5] >>> 2] = -1;
      j += len;
    }
  }
  for (const r of pendingReadbacks) {
    r.staging.mapAsync(GPUMapMode.READ).then(() => {
      const heap = getHeapU8();
      heap.set(new Uint8Array(r.staging.getMappedRange(), 0, r.size), r.dst);
      r.staging.unmap();
      r.staging.destroy();
      new Int32Array(heap.buffer)[r.status >>> 2] = 1;
    }, (e) => {
      reportError(`readback failed: ${e?.message ?? e}`);
      new Int32Array(getHeapU8().buffer)[r.status >>> 2] = -1;
    });
  }
  pendingReadbacks = [];
  if (debugScopes) device.popErrorScope().then((err) => { if (err) reportError(err.message); });
  return pendingErrors.length | (outOfMemory ? STATUS.ReplayOutOfMemory : 0) | (deviceLost ? STATUS.ReplayDeviceLost : 0);
}

// ---- descriptor resolution --------------------------------------------------------------------------------------------------
function resolveRenderPipeline(d) {
  d.layout = objs[d.layout];
  d.vertex.module = objs[d.vertex.module];
  if (d.fragment) d.fragment.module = objs[d.fragment.module];
  return d;
}

function resolveBindGroup(d) {
  return {
    layout: objs[d.layout],
    entries: d.entries.map((e) => {
      if (e.buffer !== undefined) {
        const resource = { buffer: objs[e.buffer], offset: e.offset };
        if (e.size !== undefined) resource.size = e.size;
        return { binding: e.binding, resource };
      }
      return { binding: e.binding, resource: objs[e.view ?? e.sampler] };
    }),
  };
}

// ---- module API ---------------------------------------------------------------------------------------------------------------
/**
 * init({ device, canvas, format, getHeapU8 }) - called by main.js after requestDevice() and before dotnet.create().
 * `format` is navigator.gpu.getPreferredCanvasFormat(); getHeapU8 returns runtime.localHeapViewU8().
 */
export function init(options) {
  ({ device, canvas, getHeapU8 } = options);
  preferredFormat = options.format ?? navigator.gpu.getPreferredCanvasFormat();
  queue = device.queue;
  context = canvas.getContext('webgpu');
  window.__brutal ??= {};
  window.__brutal.readPixel = readPixel;
  window.__brutal.frameCount ??= 0;
  window.__brutal.lastError ??= null;

  objs.length = 0;
  for (let h = 0; h < RESERVED.FirstDynamic; h++) objs.push(undefined);
  const all = GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT | GPUShaderStage.COMPUTE;
  objs[RESERVED.EmptyBindGroupLayout] = device.createBindGroupLayout({ label: 'empty', entries: [] });
  objs[RESERVED.EmptyBindGroup] = device.createBindGroup({ label: 'empty', layout: objs[RESERVED.EmptyBindGroupLayout], entries: [] });
  objs[RESERVED.PushUniformLayout] = device.createBindGroupLayout({
    label: 'push constants (uniform)', entries: [{ binding: 0, visibility: all, buffer: { type: 'uniform', hasDynamicOffset: true } }],
  });
  objs[RESERVED.PushStorageLayout] = device.createBindGroupLayout({
    label: 'push constants (storage)', entries: [{ binding: 0, visibility: all, buffer: { type: 'read-only-storage', hasDynamicOffset: true } }],
  });
  createPushRing(PUSH_RING_SLOTS);

  device.addEventListener('uncapturederror', (e) => reportError(e.error?.message ?? String(e.error)));
  // From now on replays are skipped and acquire/present report STATUS.CanvasDeviceLost: C# returns VK_ERROR_DEVICE_LOST.
  device.lost.then((info) => { deviceLost = true; reportError(`device lost (${info.reason}): ${info.message}`); });
}

/** Object creation: a descriptor WebGPU throws on (TypeError etc.) is reported and returns id 0 instead of throwing into C#. */
function create(kind, fn) {
  try {
    return put(fn());
  } catch (e) {
    reportError(`${kind}: ${e?.message ?? e}`);
    return 0;
  }
}

/** The functions C# calls through [JSImport(..., "webgpu")] (Bridge/WebGpuImports.cs). */
export const imports = {
  getAdapterInfoJson: () => {
    const i = device.adapterInfo ?? {};
    return JSON.stringify({ vendor: i.vendor ?? '', architecture: i.architecture ?? '', device: i.device ?? '', description: i.description ?? '', isFallbackAdapter: !!i.isFallbackAdapter });
  },
  getLimitsJson: () => {
    const o = {};
    for (const k in device.limits) if (typeof device.limits[k] === 'number') o[k] = device.limits[k];
    return JSON.stringify(o);
  },
  getFeaturesJson: () => JSON.stringify([...device.features]),
  getPreferredCanvasFormat: () => preferredFormat,
  getCanvasSize: packSize,
  configureCanvas: (format, usage, viewFormatsJson, alphaMode) => {
    const viewFormats = JSON.parse(viewFormatsJson);
    context.configure({ device, format, usage, viewFormats, alphaMode });
    canvasFormat = format;
    canvasSrgbFormat = viewFormats[0] ?? null;
    currentTexture = currentView = currentSrgbView = null;
  },
  acquireCanvasTexture: () => {
    if (deviceLost) return STATUS.CanvasDeviceLost;
    currentTexture = context.getCurrentTexture();
    currentView = currentSrgbView = null;
    return packSize();
  },
  present: () => {
    if (currentTexture) servePixelRequests(currentTexture);
    currentTexture = currentView = currentSrgbView = null;
    window.__brutal.frameCount++;
    return deviceLost ? STATUS.CanvasDeviceLost : packSize();
  },

  createBuffer: (size, usage, label) => create('createBuffer', () => device.createBuffer({ size, usage, label: label || undefined })),
  createTexture: (json) => create('createTexture', () => device.createTexture(JSON.parse(json))),
  createTextureView: (texture, json) => create('createView', () => objs[texture].createView(JSON.parse(json))),
  createSampler: (json) => create('createSampler', () => device.createSampler(JSON.parse(json))),
  createShaderModule: (code, label) => create('createShaderModule', () => {
    const module = device.createShaderModule({ code, label });
    module.getCompilationInfo?.().then((info) => {
      for (const m of info.messages) if (m.type === 'error') reportError(`shader ${label}:${m.lineNum}:${m.linePos}: ${m.message}`);
    });
    return module;
  }),
  createBindGroupLayout: (json) => create('createBindGroupLayout', () => device.createBindGroupLayout(JSON.parse(json))),
  createPipelineLayout: (json) => create('createPipelineLayout',
    () => device.createPipelineLayout({ bindGroupLayouts: JSON.parse(json).bindGroupLayouts.map((h) => objs[h]) })),
  createBindGroup: (json) => create('createBindGroup', () => device.createBindGroup(resolveBindGroup(JSON.parse(json)))),
  createRenderPipeline: (json) => create('createRenderPipeline', () => device.createRenderPipeline(resolveRenderPipeline(JSON.parse(json)))),
  createComputePipeline: (json) => create('createComputePipeline', () => {
    const d = JSON.parse(json);
    d.layout = objs[d.layout];
    d.compute.module = objs[d.compute.module];
    return device.createComputePipeline(d);
  }),
  setLabel: (h, text) => { const o = objs[h]; if (o) o.label = text; },
  release,

  writeBuffer: (buffer, offset, ptr, size) => queue.writeBuffer(objs[buffer], offset, getHeapU8(), ptr, size),
  replayCommandStream,
  submitEmpty: () => queue.submit([]),
  pollErrors: () => pendingErrors.splice(0).join('\n'),
  setDebugMode: (scopes) => { debugScopes = !!scopes; },
};
