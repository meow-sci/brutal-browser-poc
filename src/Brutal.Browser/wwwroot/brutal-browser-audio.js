// brutal-browser-audio.js - Web Audio side of Brutal.Browser.Audio (ESM). Registered with
//   runtime.setModuleImports('brutal-browser-audio', imports)
// after `init({ getHeapU8 })`.
//
// The AudioContext is created lazily (first resume/decode/play, or the first user gesture seen by the unlock listeners below).
// Browsers keep a context created outside a user gesture in the 'suspended' state (Chrome's default policy for top-level pages is
// document-user-activation-required; Safari also reports 'interrupted' after a tab/device switch), so the page must call
// `resume()` synchronously from a user-activation handler (click/keydown): in the HelloMesh page that is the start overlay and the
// "Play sound" button. decodeAudioData() works on a suspended context, so assets can be decoded before the first gesture.
//
// Signal path: source -> per-voice GainNode -> master GainNode -> AnalyserNode -> ctx.destination. The analyser passes the audio
// through unchanged and backs getOutputLevel(): the RMS of what is sent to the output, so tests can tell silence from sound (a
// suspended context, a source that never started, a muted master or a broken connection upstream of the meter all read 0).
//
// Playing while the context is not running is allowed: the source is started anyway (the context clock does not advance while
// suspended, so the browser plays it from its start as soon as the context runs) and a resume() is requested.
//
// Handles are small positive integers held in JS-side tables, so C# never keeps JSObject proxies.

let getHeapU8 = null;
let ctx = null;
let master = null;
let analyser = null;
let levelBuf = null;
let masterGain = 1;
let lastError = '';

let nextId = 1;
const buffers = new Map(); // handle -> AudioBuffer
const voices = new Map();  // voice handle -> { src, gain }

function ensureContext() {
  if (ctx) return ctx;
  const Ctor = globalThis.AudioContext || globalThis.webkitAudioContext;
  if (!Ctor) throw new Error('Web Audio is not available');
  try {
    // 48 kHz is the rate of the game assets, which avoids a resample in decodeAudioData; fall back to the device rate.
    ctx = new Ctor({ latencyHint: 'interactive', sampleRate: 48000 });
  } catch {
    ctx = new Ctor({ latencyHint: 'interactive' });
  }
  master = ctx.createGain();
  master.gain.value = masterGain;
  analyser = ctx.createAnalyser(); // output meter in series: audio passes through unchanged
  analyser.fftSize = 2048;
  analyser.smoothingTimeConstant = 0;
  master.connect(analyser).connect(ctx.destination);
  return ctx;
}

/** Asks a context that is not running to run; never throws and never leaves an unhandled rejection. */
function requestRunning(c) {
  if (c.state === 'running') return Promise.resolve(true);
  try {
    return c.resume().then(() => c.state === 'running', (e) => { lastError = String(e); return false; });
  } catch (e) {
    lastError = String(e);
    return Promise.resolve(false);
  }
}

/** RMS (0..1) of the last analyser frame (fftSize samples of the master mix); 0 while there is no context or it is not running. */
function outputLevel() {
  if (!ctx || !analyser || ctx.state !== 'running') return 0; // a stopped graph would keep returning its last (stale) frame
  if (!levelBuf || levelBuf.length !== analyser.fftSize) levelBuf = new Float32Array(analyser.fftSize);
  analyser.getFloatTimeDomainData(levelBuf);
  let sum = 0;
  for (let i = 0; i < levelBuf.length; i++) sum += levelBuf[i] * levelBuf[i];
  return Math.min(1, Math.sqrt(sum / levelBuf.length));
}

/** `getHeapU8` must return a fresh Uint8Array over the wasm heap per call (`() => runtime.localHeapViewU8()`). */
export function init({ getHeapU8: g }) {
  getHeapU8 = g;
  const hooks = (globalThis.__brutal ??= {});
  hooks.audio = {
    getState: () => (ctx ? ctx.state : 'none'),
    sampleRate: () => (ctx ? ctx.sampleRate : 0),
    voices: () => voices.size,
    buffers: () => buffers.size,
    getOutputLevel: outputLevel,
  };
  // Unlock listeners: every user activation (capture phase, so nothing can stop it) creates the context or resumes a context that
  // is not running. This is the safety net for games whose sound is triggered by a key press: the game sees the key a frame later,
  // outside the gesture, where Safari (and Chrome without sticky activation) would refuse to resume.
  const unlock = () => {
    try { void requestRunning(ensureContext()); } catch (e) { lastError = String(e); }
  };
  for (const type of ['pointerdown', 'keydown', 'touchend']) {
    globalThis.addEventListener?.(type, unlock, { capture: true, passive: true });
  }
}

export const imports = {
  /**
   * Creates the context if needed and resumes it; resolves true when it is running. Call it from a user gesture and
   * synchronously: the context is created / `ctx.resume()` is called before this function returns (nothing is awaited first).
   */
  resume: () => {
    try {
      return requestRunning(ensureContext());
    } catch (e) {
      lastError = String(e);
      return Promise.resolve(false);
    }
  },

  /** Decodes a complete audio file (copied into JS by the interop layer). Resolves a buffer handle, or 0 on failure. */
  decode: async (bytes) => {
    try {
      const c = ensureContext();
      // decodeAudioData detaches its argument; give it a private copy
      const ab = new Uint8Array(bytes).buffer;
      const buf = await c.decodeAudioData(ab);
      const h = nextId++;
      buffers.set(h, buf);
      return h;
    } catch (e) {
      lastError = String(e && e.name ? `${e.name}: ${e.message}` : e);
      return 0;
    }
  },

  getLastError: () => lastError,

  /** Builds a buffer from interleaved float32 PCM that lives in the wasm heap (read synchronously, copied). */
  createBufferFromPcm: (ptr, floatCount, channels, sampleRate) => {
    try {
      const c = ensureContext();
      const frames = Math.floor(floatCount / channels);
      const heap = getHeapU8();
      const pcm = new Float32Array(heap.slice(ptr, ptr + floatCount * 4).buffer); // slice() copies -> 4-byte aligned
      const buf = c.createBuffer(channels, frames, sampleRate);
      for (let ch = 0; ch < channels; ch++) {
        const out = buf.getChannelData(ch);
        for (let i = 0, j = ch; i < frames; i++, j += channels) out[i] = pcm[j];
      }
      const h = nextId++;
      buffers.set(h, buf);
      return h;
    } catch (e) {
      lastError = String(e);
      return 0;
    }
  },

  releaseBuffer: (handle) => { buffers.delete(handle); },

  getDuration: (handle) => { const b = buffers.get(handle); return b ? b.duration : 0; },
  getChannels: (handle) => { const b = buffers.get(handle); return b ? b.numberOfChannels : 0; },
  getSampleRate: (handle) => { const b = buffers.get(handle); return b ? b.sampleRate : 0; },

  /** Starts a one-shot (or looping) voice; returns its handle, or 0 when the buffer is unknown. */
  play: (handle, gain, loop) => {
    const buf = buffers.get(handle);
    if (!buf) return 0;
    const c = ensureContext();
    if (c.state !== 'running') void requestRunning(c); // suspended/interrupted: start anyway, it plays once the context runs
    const src = c.createBufferSource();
    src.buffer = buf;
    src.loop = !!loop;
    const g = c.createGain();
    g.gain.value = gain;
    src.connect(g).connect(master);
    const id = nextId++;
    voices.set(id, { src, gain: g });
    src.onended = () => { voices.delete(id); try { g.disconnect(); } catch { /* ignore */ } };
    src.start();
    return id;
  },

  stop: (voice) => {
    const v = voices.get(voice);
    if (!v) return;
    voices.delete(voice);
    try { v.src.stop(); } catch { /* already stopped */ }
  },

  isPlaying: (voice) => voices.has(voice),

  setMasterGain: (g) => {
    masterGain = g;
    if (master) master.gain.value = g;
  },

  /** 'none' (no context yet), 'suspended', 'running', 'interrupted' (Safari) or 'closed'. */
  getState: () => (ctx ? ctx.state : 'none'),

  /** RMS (0..1) of the last analyser frame of the master mix: what is sent to the output. 0 when silent or not running. */
  getOutputLevel: outputLevel,
};
