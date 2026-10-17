// brutal-browser.js - Brutal.Browser host module (ESM). Registered with
//   runtime.setModuleImports('brutal-browser', imports)
// after `init({ canvas, getHeapU8 })`. See ../README.md.
//
// Design: DOM events are normalised to GLFW semantics and appended to a preallocated binary queue. C# drains the queue
// with ONE `pollEvents(ptr, maxBytes)` call per BrowserInput.PollEvents() (records are copied into wasm memory).
//
// Record layout (little endian, 40 bytes; must match Brutal.Browser.BrowserInput.EventRecord):
//    0 u8  type        (EV.*)
//    1 u8  action      (key: 0 release / 1 press / 2 repeat; button: 0/1; flag events: 0/1; scroll: GLFW_SCROLL_* flags)
//    2 u16 mods        (GLFW_MOD_*: shift 1, control 2, alt 4, super 8)
//    4 i32 a           (key: GLFW key | char: code point | button: GLFW button | size: css width)
//    8 i32 b           (key: scancode | size: css height)
//   12 i32 c           (size: framebuffer width)
//   16 i32 d           (size: framebuffer height)
//   20 i32 (unused)
//   24 f64 x           (cursor x in CSS px | scroll dx | size: devicePixelRatio)
//   32 f64 y           (cursor y in CSS px | scroll dy)

const REC = 40;
const CAPACITY = 8192; // queued records; consecutive cursor/scroll records are coalesced, so this is never reached in practice

const EV = {
  KEY: 1, CHAR: 2, MOUSE_BUTTON: 3, CURSOR_POS: 4, CURSOR_ENTER: 5, SCROLL: 6,
  FOCUS: 7, SIZE: 8, POINTER_LOCK: 9, VISIBILITY: 10, CLOSE: 11,
};

const SCROLL_PRECISE = 1; // GLFW_SCROLL_PRECISE

// KeyboardEvent.code -> [GLFW key, scancode]. GLFW keys are the values of Glfw.Constants.GLFW_KEY_*; scancodes follow
// the PC/AT set-1 style DOM_PK_* values used by Emscripten (extended keys 0xE0xx). One entry per line: the xunit test
// tests/Brutal.Browser.Tests parses this table and checks it against Brutal.GlfwApi.GlfwKey.
const KEY_TABLE = [
  ['Space', 32, 0x0039],
  ['Quote', 39, 0x0028],
  ['Comma', 44, 0x0033],
  ['Minus', 45, 0x000C],
  ['Period', 46, 0x0034],
  ['Slash', 47, 0x0035],
  ['Digit0', 48, 0x000B],
  ['Digit1', 49, 0x0002],
  ['Digit2', 50, 0x0003],
  ['Digit3', 51, 0x0004],
  ['Digit4', 52, 0x0005],
  ['Digit5', 53, 0x0006],
  ['Digit6', 54, 0x0007],
  ['Digit7', 55, 0x0008],
  ['Digit8', 56, 0x0009],
  ['Digit9', 57, 0x000A],
  ['Semicolon', 59, 0x0027],
  ['Equal', 61, 0x000D],
  ['KeyA', 65, 0x001E],
  ['KeyB', 66, 0x0030],
  ['KeyC', 67, 0x002E],
  ['KeyD', 68, 0x0020],
  ['KeyE', 69, 0x0012],
  ['KeyF', 70, 0x0021],
  ['KeyG', 71, 0x0022],
  ['KeyH', 72, 0x0023],
  ['KeyI', 73, 0x0017],
  ['KeyJ', 74, 0x0024],
  ['KeyK', 75, 0x0025],
  ['KeyL', 76, 0x0026],
  ['KeyM', 77, 0x0032],
  ['KeyN', 78, 0x0031],
  ['KeyO', 79, 0x0018],
  ['KeyP', 80, 0x0019],
  ['KeyQ', 81, 0x0010],
  ['KeyR', 82, 0x0013],
  ['KeyS', 83, 0x001F],
  ['KeyT', 84, 0x0014],
  ['KeyU', 85, 0x0016],
  ['KeyV', 86, 0x002F],
  ['KeyW', 87, 0x0011],
  ['KeyX', 88, 0x002D],
  ['KeyY', 89, 0x0015],
  ['KeyZ', 90, 0x002C],
  ['BracketLeft', 91, 0x001A],
  ['Backslash', 92, 0x002B],
  ['BracketRight', 93, 0x001B],
  ['Backquote', 96, 0x0029],
  ['IntlBackslash', 161, 0x0056],
  ['IntlRo', 162, 0x0073],
  ['IntlYen', 162, 0x007D],
  ['Escape', 256, 0x0001],
  ['Enter', 257, 0x001C],
  ['Tab', 258, 0x000F],
  ['Backspace', 259, 0x000E],
  ['Insert', 260, 0xE052],
  ['Delete', 261, 0xE053],
  ['ArrowRight', 262, 0xE04D],
  ['ArrowLeft', 263, 0xE04B],
  ['ArrowDown', 264, 0xE050],
  ['ArrowUp', 265, 0xE048],
  ['PageUp', 266, 0xE049],
  ['PageDown', 267, 0xE051],
  ['Home', 268, 0xE047],
  ['End', 269, 0xE04F],
  ['CapsLock', 280, 0x003A],
  ['ScrollLock', 281, 0x0046],
  ['NumLock', 282, 0x0045],
  ['PrintScreen', 283, 0xE037],
  ['Pause', 284, 0xE046],
  ['F1', 290, 0x003B],
  ['F2', 291, 0x003C],
  ['F3', 292, 0x003D],
  ['F4', 293, 0x003E],
  ['F5', 294, 0x003F],
  ['F6', 295, 0x0040],
  ['F7', 296, 0x0041],
  ['F8', 297, 0x0042],
  ['F9', 298, 0x0043],
  ['F10', 299, 0x0044],
  ['F11', 300, 0x0057],
  ['F12', 301, 0x0058],
  ['F13', 302, 0x0064],
  ['F14', 303, 0x0065],
  ['F15', 304, 0x0066],
  ['F16', 305, 0x0067],
  ['F17', 306, 0x0068],
  ['F18', 307, 0x0069],
  ['F19', 308, 0x006A],
  ['F20', 309, 0x006B],
  ['F21', 310, 0x006C],
  ['F22', 311, 0x006D],
  ['F23', 312, 0x006E],
  ['F24', 313, 0x0076],
  ['F25', 314, 0x0077],
  ['Numpad0', 320, 0x0052],
  ['Numpad1', 321, 0x004F],
  ['Numpad2', 322, 0x0050],
  ['Numpad3', 323, 0x0051],
  ['Numpad4', 324, 0x004B],
  ['Numpad5', 325, 0x004C],
  ['Numpad6', 326, 0x004D],
  ['Numpad7', 327, 0x0047],
  ['Numpad8', 328, 0x0048],
  ['Numpad9', 329, 0x0049],
  ['NumpadDecimal', 330, 0x0053],
  ['NumpadDivide', 331, 0xE035],
  ['NumpadMultiply', 332, 0x0037],
  ['NumpadSubtract', 333, 0x004A],
  ['NumpadAdd', 334, 0x004E],
  ['NumpadEnter', 335, 0xE01C],
  ['NumpadEqual', 336, 0x0059],
  ['ShiftLeft', 340, 0x002A],
  ['ControlLeft', 341, 0x001D],
  ['AltLeft', 342, 0x0038],
  ['MetaLeft', 343, 0xE05B],
  ['ShiftRight', 344, 0x0036],
  ['ControlRight', 345, 0xE01D],
  ['AltRight', 346, 0xE038],
  ['MetaRight', 347, 0xE05C],
  ['ContextMenu', 348, 0xE05D],
  ['OSLeft', 343, 0xE05B],
  ['OSRight', 347, 0xE05C],
];

const keyInfo = new Map(KEY_TABLE.map(([code, key, scan]) => [code, [key, scan]]));

// ---------------------------------------------------------------------------------------------------------------------
// state

let canvas = null;
let getHeapU8 = null;
let initialized = false;

let queue = null; // ArrayBuffer
let dv = null;
let u8 = null;
let count = 0;
let dropped = 0;

// applied canvas size (what C# knows) and the size measured by the ResizeObserver but not yet applied
let cssW = 1, cssH = 1, fbW = 1, fbH = 1, dpr = 1;
let pending = null;

let locked = false;       // document.pointerLockElement === canvas
let wantLock = false;     // the game asked for GLFW_CURSOR_DISABLED
let wantRaw = false;      // ... with unadjusted movement
let cursorX = 0, cursorY = 0; // virtual cursor position (CSS px); accumulates movementX/Y while locked
let preLockX = 0, preLockY = 0;
const keysDown = new Map(); // KeyboardEvent.code -> [glfwKey, scancode] currently held (for the Meta-release workaround)
let clipboardText = '';

// ---------------------------------------------------------------------------------------------------------------------
// queue

function push(type, action, mods, a, b, c, d, x, y) {
  if (count >= CAPACITY) { dropped++; return; }
  const o = count * REC;
  dv.setUint8(o, type);
  dv.setUint8(o + 1, action);
  dv.setUint16(o + 2, mods, true);
  dv.setInt32(o + 4, a, true);
  dv.setInt32(o + 8, b, true);
  dv.setInt32(o + 12, c, true);
  dv.setInt32(o + 16, d, true);
  dv.setFloat64(o + 24, x, true);
  dv.setFloat64(o + 32, y, true);
  count++;
}

let sentX = NaN, sentY = NaN; // last cursor position queued (clicks without movement must not produce cursor events)

function pushCursor() {
  if (cursorX === sentX && cursorY === sentY) return;
  sentX = cursorX; sentY = cursorY;
  if (count > 0) {
    const o = (count - 1) * REC;
    if (dv.getUint8(o) === EV.CURSOR_POS) { // coalesce consecutive moves: only the latest position matters
      dv.setFloat64(o + 24, cursorX, true);
      dv.setFloat64(o + 32, cursorY, true);
      return;
    }
  }
  push(EV.CURSOR_POS, 0, 0, 0, 0, 0, 0, cursorX, cursorY);
}

function pushScroll(dx, dy, flags, mods) {
  if (count > 0) {
    const o = (count - 1) * REC;
    if (dv.getUint8(o) === EV.SCROLL && dv.getUint8(o + 1) === flags && dv.getUint16(o + 2, true) === mods) {
      dv.setFloat64(o + 24, dv.getFloat64(o + 24, true) + dx, true); // accumulate
      dv.setFloat64(o + 32, dv.getFloat64(o + 32, true) + dy, true);
      return;
    }
  }
  push(EV.SCROLL, flags, mods, 0, 0, 0, 0, dx, dy);
}

const mods = (e) => (e.shiftKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.altKey ? 4 : 0) | (e.metaKey ? 8 : 0);

// ---------------------------------------------------------------------------------------------------------------------
// canvas size

function measure(entry) {
  const rect = canvas.getBoundingClientRect();
  const r = window.devicePixelRatio || 1;
  const w = Math.max(1, Math.round(rect.width));
  const h = Math.max(1, Math.round(rect.height));
  let pw = Math.max(1, Math.round(rect.width * r));
  let ph = Math.max(1, Math.round(rect.height * r));
  // devicePixelContentBoxSize is the exact snapped pixel size; trust it only when it agrees with css * devicePixelRatio
  // (browsers/emulators with an overridden devicePixelRatio can report physical pixels instead)
  const dp = entry && entry.devicePixelContentBoxSize && entry.devicePixelContentBoxSize[0];
  if (dp && Math.abs(dp.inlineSize - rect.width * r) <= 2 && Math.abs(dp.blockSize - rect.height * r) <= 2) {
    pw = Math.max(1, Math.round(dp.inlineSize));
    ph = Math.max(1, Math.round(dp.blockSize));
  }
  pending = { w, h, pw, ph, dpr: r };
}

// Applies the pending size: writes canvas.width/height (this clears the canvas, so it is done only from pollEvents,
// right before the frame that re-renders) and optionally queues a SIZE record.
function applyPending(emit) {
  const p = pending;
  if (!p) return;
  pending = null;
  if (p.w === cssW && p.h === cssH && p.pw === fbW && p.ph === fbH && p.dpr === dpr) return;
  cssW = p.w; cssH = p.h; fbW = p.pw; fbH = p.ph; dpr = p.dpr;
  if (canvas.width !== fbW) canvas.width = fbW;
  if (canvas.height !== fbH) canvas.height = fbH;
  if (emit) push(EV.SIZE, 0, 0, cssW, cssH, fbW, fbH, dpr, 0);
}

function watchDpr() {
  // devicePixelRatio changes (browser zoom, moving between displays) do not change the CSS box, so ResizeObserver can
  // stay silent: re-arm a resolution media query after each change.
  try {
    const mq = window.matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`);
    mq.addEventListener('change', () => { measure(); watchDpr(); }, { once: true });
  } catch { /* matchMedia unavailable */ }
}

// ---------------------------------------------------------------------------------------------------------------------
// keyboard

function isEditable(t) {
  if (!t || t === document.body || t === canvas) return false;
  const tag = t.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || t.isContentEditable === true;
}

const PREVENT_CODES = new Set([
  'Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'PageUp', 'PageDown', 'Home', 'End', 'Tab', 'Backspace',
  'Slash', 'Quote', 'ContextMenu', 'F1', 'F2', 'F3', 'F4', 'F6', 'F7', 'F8', 'F9', 'F10',
]);
const PASS_CODES = new Set(['F5', 'F11', 'F12']); // reload / fullscreen / devtools stay available even when pointer locked

function shouldPrevent(e) {
  const cmd = e.ctrlKey || e.metaKey;
  if (cmd && (e.code === 'KeyC' || e.code === 'KeyX' || e.code === 'KeyV')) return false; // clipboard shortcuts
  if (PASS_CODES.has(e.code)) return false;
  if (locked) return true;
  if (PREVENT_CODES.has(e.code)) return true;
  if (cmd && (e.code === 'KeyF' || e.code === 'KeyS' || e.code === 'KeyP' || e.code === 'KeyG' || e.code === 'KeyD' || e.code === 'KeyA')) return true;
  if (e.code === 'AltLeft' || e.code === 'AltRight') return true; // Alt alone would focus the menu bar on Windows/Linux
  return false;
}

function onKey(e, down) {
  if (isEditable(e.target)) return;
  const info = keyInfo.get(e.code);
  const key = info ? info[0] : -1;
  const scan = info ? info[1] : 0;
  const m = mods(e);

  if (down) {
    keysDown.set(e.code, [key, scan]);
    push(EV.KEY, e.repeat ? 2 : 1, m, key, scan, 0, 0, 0, 0);
    if (!e.ctrlKey && !e.metaKey && !e.isComposing && e.key && e.key.length <= 2) {
      const cp = e.key.codePointAt(0);
      if (cp !== undefined && String.fromCodePoint(cp).length === e.key.length) push(EV.CHAR, 0, m, cp, 0, 0, 0, 0, 0);
    }
    maybeRelock();
  } else {
    keysDown.delete(e.code);
    push(EV.KEY, 0, m, key, scan, 0, 0, 0, 0);
    // With Cmd/Win held the browser never sends keyup for the other keys: release them when Meta is released.
    if (e.code === 'MetaLeft' || e.code === 'MetaRight' || e.code === 'OSLeft' || e.code === 'OSRight') {
      for (const [code, [k, s]] of keysDown) push(EV.KEY, 0, m, k, s, 0, 0, 0, 0);
      keysDown.clear();
    }
  }
  if (shouldPrevent(e)) e.preventDefault();
}

// ---------------------------------------------------------------------------------------------------------------------
// pointer, wheel, pointer lock

function mapButton(domButton) {
  // DOM: 0 left, 1 middle, 2 right, 3 back, 4 forward -> GLFW: 0 left, 2 middle, 1 right, 3, 4
  switch (domButton) { case 0: return 0; case 1: return 2; case 2: return 1; case 3: return 3; case 4: return 4; default: return Math.min(domButton, 7); }
}

function updateCursor(e) {
  if (locked) {
    cursorX += e.movementX || 0;
    cursorY += e.movementY || 0;
  } else {
    const r = canvas.getBoundingClientRect();
    cursorX = e.clientX - r.left;
    cursorY = e.clientY - r.top;
  }
}

function onPointerDown(e) {
  if (e.pointerType === 'touch' && !e.isPrimary) return;
  try { canvas.focus({ preventScroll: true }); } catch { /* ignore */ }
  updateCursor(e);
  pushCursor();
  try { canvas.setPointerCapture(e.pointerId); } catch { /* ignore */ }
  push(EV.MOUSE_BUTTON, 1, mods(e), e.pointerType === 'mouse' ? mapButton(e.button) : 0, 0, 0, 0, 0, 0);
  maybeRelock();
}

function onPointerUp(e) {
  if (e.pointerType === 'touch' && !e.isPrimary) return;
  updateCursor(e);
  pushCursor();
  try { canvas.releasePointerCapture(e.pointerId); } catch { /* ignore */ }
  push(EV.MOUSE_BUTTON, 0, mods(e), e.pointerType === 'mouse' ? mapButton(e.button) : 0, 0, 0, 0, 0, 0);
}

function onPointerMove(e) {
  if (e.pointerType === 'touch' && !e.isPrimary) return;
  updateCursor(e);
  pushCursor();
}

function onWheel(e) {
  e.preventDefault(); // listener is registered { passive: false }
  // SDL / contrib.glfw3 normalisation: pixel x 1/100, line x 1/3, page x 80; GLFW +Y = away from the user (DOM +Y = down)
  const mul = e.deltaMode === 1 ? 1 / 3 : e.deltaMode === 2 ? 80 : 1 / 100;
  const dx = e.deltaX === 0 ? 0 : e.deltaX * mul;
  const dy = e.deltaY === 0 ? 0 : -e.deltaY * mul;
  // heuristic: pixel deltas that are not whole 100 px notches come from precise devices (trackpads)
  const precise = e.deltaMode === 0 && ((e.deltaX % 100) !== 0 || (e.deltaY % 100) !== 0) ? SCROLL_PRECISE : 0;
  pushScroll(dx, dy, precise, mods(e));
}

function tryLock() {
  if (document.pointerLockElement === canvas) return;
  try {
    const p = wantRaw ? canvas.requestPointerLock({ unadjustedMovement: true }) : canvas.requestPointerLock();
    if (p && typeof p.catch === 'function') {
      p.catch((err) => {
        if (wantRaw && err && err.name === 'NotSupportedError') { try { canvas.requestPointerLock(); } catch { /* ignore */ } }
        // other failures (no transient activation, Escape just pressed): retried on the next activation event
      });
    }
  } catch { /* retried on the next activation event */ }
}

function maybeRelock() {
  if (wantLock && !locked) tryLock();
}

function onPointerLockChange() {
  const now = document.pointerLockElement === canvas;
  if (now === locked) return;
  locked = now;
  if (now) {
    preLockX = cursorX; preLockY = cursorY;
  } else {
    wantLock = false; // Escape (or exitPointerLock): treat as user intent, the game must ask again
    cursorX = preLockX; cursorY = preLockY;
  }
  push(EV.POINTER_LOCK, now ? 1 : 0, 0, 0, 0, 0, 0, 0, 0);
  if (!now) pushCursor();
}

// ---------------------------------------------------------------------------------------------------------------------
// setup

function installListeners() {
  const opts = { capture: true };
  window.addEventListener('keydown', (e) => onKey(e, true), opts);
  window.addEventListener('keyup', (e) => onKey(e, false), opts);
  window.addEventListener('compositionend', (e) => {
    if (isEditable(e.target) || !e.data) return;
    for (const ch of e.data) push(EV.CHAR, 0, 0, ch.codePointAt(0), 0, 0, 0, 0, 0);
  });

  canvas.addEventListener('pointerdown', onPointerDown);
  canvas.addEventListener('pointerup', onPointerUp);
  canvas.addEventListener('pointercancel', onPointerUp);
  canvas.addEventListener('pointermove', onPointerMove);
  canvas.addEventListener('pointerenter', () => push(EV.CURSOR_ENTER, 1, 0, 0, 0, 0, 0, 0, 0));
  canvas.addEventListener('pointerleave', () => push(EV.CURSOR_ENTER, 0, 0, 0, 0, 0, 0, 0, 0));
  canvas.addEventListener('mousedown', (e) => { if (e.button === 1) e.preventDefault(); }); // stops middle-click autoscroll
  canvas.addEventListener('contextmenu', (e) => e.preventDefault());
  canvas.addEventListener('auxclick', (e) => e.preventDefault());
  canvas.addEventListener('dragstart', (e) => e.preventDefault());
  canvas.addEventListener('wheel', onWheel, { passive: false });

  document.addEventListener('pointerlockchange', onPointerLockChange);
  document.addEventListener('visibilitychange', () => push(EV.VISIBILITY, document.visibilityState === 'visible' ? 1 : 0, 0, 0, 0, 0, 0, 0, 0));
  window.addEventListener('focus', () => push(EV.FOCUS, 1, 0, 0, 0, 0, 0, 0, 0));
  window.addEventListener('blur', () => push(EV.FOCUS, 0, 0, 0, 0, 0, 0, 0, 0));
  window.addEventListener('pagehide', () => push(EV.CLOSE, 0, 0, 0, 0, 0, 0, 0, 0));
  window.addEventListener('paste', (e) => {
    const t = e.clipboardData && e.clipboardData.getData('text/plain');
    if (typeof t === 'string') clipboardText = t;
  });

  if (typeof ResizeObserver === 'function') {
    const ro = new ResizeObserver((entries) => measure(entries[entries.length - 1]));
    try { ro.observe(canvas, { box: 'device-pixel-content-box' }); } catch { ro.observe(canvas); }
  } else {
    window.addEventListener('resize', () => measure());
  }
  watchDpr();
}

/**
 * Attaches all listeners. `canvas` must be sized by CSS (e.g. width/height 100%): the module writes canvas.width/height.
 * `getHeapU8` must return a fresh `Uint8Array` over the wasm heap on every call (`() => runtime.localHeapViewU8()`).
 */
export function init({ canvas: c, getHeapU8: g }) {
  if (initialized) return;
  initialized = true;
  canvas = c;
  getHeapU8 = g;
  queue = new ArrayBuffer(CAPACITY * REC);
  dv = new DataView(queue);
  u8 = new Uint8Array(queue);
  canvas.style.touchAction = 'none';
  canvas.style.outline = 'none';
  if (!canvas.hasAttribute('tabindex')) canvas.tabIndex = 0;
  installListeners();
  measure();
  applyPending(false);

  const hooks = (globalThis.__brutal ??= {});
  hooks.browser = { queued: () => count, dropped: () => dropped, locked: () => locked, size: () => ({ cssW, cssH, fbW, fbH, dpr }) };
}

export const imports = {
  /** performance.now() in milliseconds. */
  now: () => performance.now(),

  /** Writes { cssW, cssH, fbW, fbH } as four int32 at ptr, applying (without an event) any pending resize. */
  getCanvasSize: (ptr) => {
    measure();
    applyPending(false);
    const heap = getHeapU8();
    const view = new DataView(heap.buffer, heap.byteOffset + ptr, 16);
    view.setInt32(0, cssW, true); view.setInt32(4, cssH, true); view.setInt32(8, fbW, true); view.setInt32(12, fbH, true);
  },

  getDevicePixelRatio: () => dpr,

  /**
   * Applies a pending canvas resize (queueing a SIZE record), then copies up to maxBytes of queued records to ptr in the
   * wasm heap and returns the number of bytes written (a multiple of 40).
   */
  pollEvents: (ptr, maxBytes) => {
    applyPending(true);
    const n = Math.min(count, Math.floor(maxBytes / REC));
    if (n === 0) return 0;
    getHeapU8().set(u8.subarray(0, n * REC), ptr);
    if (n < count) u8.copyWithin(0, n * REC, count * REC);
    count -= n;
    return n * REC;
  },

  isVisible: () => document.visibilityState === 'visible',
  hasFocus: () => document.hasFocus(),

  requestPointerLock: (unadjustedMovement) => { wantLock = true; wantRaw = !!unadjustedMovement; tryLock(); },
  exitPointerLock: () => { wantLock = false; if (document.pointerLockElement === canvas) document.exitPointerLock(); },
  isPointerLocked: () => locked,

  setCursorVisible: (visible) => { canvas.style.cursor = visible ? '' : 'none'; },
  setCursorStyle: (css) => { canvas.style.cursor = css || ''; },
  setCursorPos: (x, y) => { cursorX = x; cursorY = y; pushCursor(); }, // virtual only: browsers cannot warp the pointer

  focusCanvas: () => { try { canvas.focus({ preventScroll: true }); } catch { /* ignore */ } },
  setTitle: (title) => { document.title = title; },
  setCanvasCssSize: (w, h) => { canvas.style.width = `${w}px`; canvas.style.height = `${h}px`; measure(); },
  requestFullscreen: () => { try { const p = canvas.requestFullscreen(); if (p && p.catch) p.catch(() => { }); } catch { /* ignore */ } },
  exitFullscreen: () => { try { if (document.fullscreenElement) document.exitFullscreen(); } catch { /* ignore */ } },

  /** Synchronous cache: updated by paste events and setClipboard (the async Clipboard API is write-only here). */
  getClipboard: () => clipboardText,
  setClipboard: (text) => {
    clipboardText = text;
    try { if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).catch(() => { }); } catch { /* ignore */ }
  },
};
