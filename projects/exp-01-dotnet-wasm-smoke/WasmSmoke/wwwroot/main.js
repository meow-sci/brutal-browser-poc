// exp-01-dotnet-wasm-smoke host script (ES module). Based on the `wasmbrowser` template main.js.
import { dotnet } from './_framework/dotnet.js'

const log = (...a) => console.log(...a);
globalThis.addEventListener('unhandledrejection', (e) => console.error('HOST unhandledrejection: ' + (e.reason && e.reason.stack || e.reason)));
const t0 = performance.now();

// .create() boots the runtime but does not run Main; we register JS imports first, then runMain().
// Page query string -> application arguments (test selection): ?tests=T07,T08  or  ?skip=T07
const qs = new URLSearchParams(location.search);
const appArgs = ['start'];
if (qs.get('tests')) appArgs.push('tests=' + qs.get('tests'));
if (qs.get('skip')) appArgs.push('skip=' + qs.get('skip'));

const runtime = await dotnet
    .withApplicationArguments(...appArgs)
    .create();

const { setModuleImports, getAssemblyExports, getConfig, runMain, Module } = runtime;
log(`HOST runtime created in ${(performance.now() - t0).toFixed(0)} ms; runtimeBuildInfo=${JSON.stringify(runtime.runtimeBuildInfo)}`);

// ---- JS-side "ICD": function pointers implemented in JS via Emscripten addFunction ----
let gipaCalls = 0, gipaNames = new Set(), enumCalls = 0;
let jsEnumPD = 0;
let trampEnumPD = 0;
const sigCalls = {};
const sigLast = {};

setModuleImports('main.js', {
    // T11
    hasWebGpu: () => !!(globalThis.navigator && navigator.gpu),
    sumBytes: (mv) => { const a = mv.slice(); let s = 0; for (let i = 0; i < a.length; i++) s += a[i]; return s; },
    sumPtr: (ptr, len) => {
        const heap = runtime.localHeapViewU8();          // short-lived view of WASM linear memory
        let s = 0; for (let i = 0; i < len; i++) s += heap[ptr + i]; return s;
    },
    heapApiSurvey: () => {
        const r = [];
        r.push(`runtime.localHeapViewU8=${typeof runtime.localHeapViewU8}`);
        r.push(`runtime.getHeapU8=${typeof runtime.getHeapU8}`);
        r.push(`runtime.setHeapI32=${typeof runtime.setHeapI32}`);
        r.push(`Module.HEAPU8=${typeof Module.HEAPU8}`);
        r.push(`Module.wasmExports=${typeof Module.wasmExports}`);
        r.push(`Module.wasmExports.memory=${Module.wasmExports && Module.wasmExports.memory ? Object.prototype.toString.call(Module.wasmExports.memory) : 'n/a'}`);
        r.push(`Module.wasmMemory=${typeof Module.wasmMemory}`);
        r.push(`Module.addFunction=${typeof Module.addFunction}`);
        r.push(`Module.UTF8ToString=${typeof Module.UTF8ToString}`);
        r.push(`Module._malloc=${typeof Module._malloc}`);
        r.push(`heapBytes=${runtime.localHeapViewU8().byteLength}`);
        r.push(`SharedArrayBuffer-backed=${runtime.localHeapViewU8().buffer instanceof (globalThis.SharedArrayBuffer || function(){})}`);
        r.push(`crossOriginIsolated=${globalThis.crossOriginIsolated}`);
        r.push(`wasmEnableSIMD=${runtime.runtimeBuildInfo.wasmEnableSIMD} wasmEnableThreads=${runtime.runtimeBuildInfo.wasmEnableThreads} wasmEnableEH=${runtime.runtimeBuildInfo.wasmEnableExceptionHandling}`);
        return r.join(' ');
    },
    webGpuAdapterInfo: async () => {
        if (!navigator.gpu) return 'navigator.gpu undefined';
        const adapter = await navigator.gpu.requestAdapter();
        if (!adapter) return 'requestAdapter() returned null';
        const i = adapter.info || {};
        return `adapter vendor='${i.vendor}' architecture='${i.architecture}' device='${i.device}' description='${i.description}' features=${adapter.features.size} maxBufferSize=${adapter.limits.maxBufferSize}`;
    },
    // T08c / T08d: JS functions exposed as wasm function-table entries
    makeJsAdder: () => Module.addFunction((a, b) => a + b * 100, 'iii'),
    makeJsGetProcAddr: () => {
        if (!jsEnumPD) {
            // VkResult vkEnumeratePhysicalDevices(VkInstance, uint32_t* pCount, VkPhysicalDevice* pDevices)
            jsEnumPD = Module.addFunction((instance, pCount, pDevices) => {
                enumCalls++;
                if (instance !== 0x00C0FFEE) return -3; // VK_ERROR_INITIALIZATION_FAILED
                runtime.setHeapI32(pCount, 1);
                if (pDevices) runtime.setHeapI32(pDevices, 0x7EB6); // fake VkPhysicalDevice (nint = 4 bytes on wasm32)
                return 0;
            }, 'iiii');
        }
        // PFN_vkVoidFunction vkGetInstanceProcAddr(VkInstance, const char* pName)
        return Module.addFunction((instance, pName) => {
            gipaCalls++;
            const name = Module.UTF8ToString(pName);
            gipaNames.add(name);
            return name === 'vkEnumeratePhysicalDevices' ? jsEnumPD : 0;
        }, 'iii');
    },
    // T08e / T09b / T13b3: JS trampolines that forward a native (wasm-table) call into C# [JSExport] methods
    makeTrampolineGipa: () => Module.addFunction((instance, pName) => exports.SmokeTests.ManagedGetInstanceProcAddr(instance, pName), 'iii'),
    trampolineEnumPdIndex: () => {
        if (!trampEnumPD) trampEnumPD = Module.addFunction((instance, pCount, pDevices) => exports.SmokeTests.ManagedEnumeratePhysicalDevices(instance, pCount, pDevices), 'iiii');
        return trampEnumPD;
    },
    makeTrampolineAdd: () => Module.addFunction((a, b) => exports.SmokeTests.ManagedAdd(a, b), 'iii'),
    // (i32, i32, i64, f32, f64, i32) -> i64 ; with WASM_BIGINT the i64 values arrive/leave as BigInt
    makeTrampolineMixed: () => Module.addFunction((a, b, c, d, e, f) => {
        const r = exports.SmokeTests.ManagedMixed(a, b, Number(c), d, e, f);
        return typeof c === 'bigint' ? BigInt(r) : r;
    }, 'jiijfdi'),
    // P01..P45 signature probes: a JS function with an arbitrary wasm signature (emscripten sig string, ret first)
    makeJsSig: (sig) => Module.addFunction((...args) => { sigCalls[sig] = (sigCalls[sig] || 0) + 1; return sig[0] === 'j' ? 0n : (sig[0] === 'v' ? undefined : 0); }, sig),
    jsSigCalls: (sig) => sigCalls[sig] || 0,
    makeJsSigRec: (sig) => Module.addFunction((...args) => {
        sigLast[sig] = args.map(a => typeof a === 'bigint' ? '0x' + a.toString(16) + 'n' : (Number.isInteger(a) ? '0x' + a.toString(16) : String(a))).join(', ');
        return sig[0] === 'j' ? 0n : (sig[0] === 'v' ? undefined : 0);
    }, sig),
    jsSigLastArgs: (sig) => sigLast[sig] || '(not called)',
    jsIcdStats: () => `jsGipaCalls=${gipaCalls} distinctNames=${gipaNames.size} jsEnumCalls=${enumCalls}`,
    // T13
    identity: (x) => x,
    spanLength: (mv) => mv.byteLength,
    spanSumCopy: (mv) => { const a = mv.slice(); let s = 0; for (let i = 0; i < a.length; i++) s += a[i]; return s; },
});

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

// Run C# Main(). runMain keeps the runtime alive after Main returns (unlike runMainAndExit / dotnet.run()).
const exitCode = await runMain();
log(`HOST runMain resolved: exitCode=${exitCode} after ${(performance.now() - t0).toFixed(0)} ms`);

// T13e: JS -> C# [JSExport] call cost
{
    const N = 1_000_000;
    for (let i = 0; i < 20000; i++) exports.SmokeTests.Noop(i);
    const s = performance.now();
    let acc = 0;
    for (let i = 0; i < N; i++) acc += exports.SmokeTests.Noop(i);
    const ms = performance.now() - s;
    log(`TEST T13e perf-jsexport (JS->C# [JSExport] int->int, 1M): PASS ${(ms * 1e6 / N).toFixed(1)} ns/call (total ${ms.toFixed(0)} ms, acc=${acc})`);
}

// T12: drive [JSExport] Tick from requestAnimationFrame for 10 frames after Main has returned
let frames = 0;
function frame(ts) {
    exports.SmokeTests.Tick(ts);
    frames++;
    if (frames < 10) {
        requestAnimationFrame(frame);
    } else {
        exports.SmokeTests.ReportTicks();
        log('TICKS DONE');
        document.title = 'TICKS DONE';
    }
}
requestAnimationFrame(frame);
