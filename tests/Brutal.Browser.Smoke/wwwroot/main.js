// Host page logic for the Brutal.Browser smoke app (architecture.md section 5, without WebGPU):
// create the runtime, init + register the two JS modules, run Main, then drive BrowserHost.Tick from requestAnimationFrame.
import { dotnet } from './_framework/dotnet.js';
import * as browserJs from './brutal-browser.js';
import * as audioJs from './brutal-browser-audio.js';

const canvas = document.getElementById('canvas');
const startButton = document.getElementById('start');
const params = new URLSearchParams(location.search);
const forceManagedDecoder = params.has('managed');

let runtime = null;
const getHeapU8 = () => runtime.localHeapViewU8(); // fresh view on every access: the heap can grow and detach old views

// listeners first so no early input is lost; both modules only store the heap getter until the runtime exists
browserJs.init({ canvas, getHeapU8 });
audioJs.init({ getHeapU8 });

runtime = await dotnet.withApplicationArguments(...(params.has('crash') ? ['crash'] : [])).create();
const { setModuleImports, getAssemblyExports, getConfig, runMain } = runtime;
setModuleImports('brutal-browser', browserJs.imports);
setModuleImports('brutal-browser-audio', audioJs.imports);

const hostExports = (await getAssemblyExports('Brutal.Browser.dll')).Brutal.Browser.BrowserHost;
const appExports = (await getAssemblyExports(getConfig().mainAssemblyName)).Brutal.Browser.Smoke.SmokeApp;

await runMain(); // Main initialises the host, creates the GLFW window and registers the frame callback, then returns

const state = (globalThis.__brutal ??= {});
state.smoke = { frames: 0, stopped: false, error: null };

function frame(timestamp) {
  let keepGoing = false;
  try {
    keepGoing = hostExports.Tick(timestamp);
  } catch (e) {
    state.smoke.error = String(e);
    console.error('HOST Tick threw: ' + (e && e.stack || e));
  }
  state.smoke.frames++;
  if (keepGoing) requestAnimationFrame(frame);
  else { state.smoke.stopped = true; console.log('HOST loop stopped'); }
}
requestAnimationFrame(frame);

startButton.disabled = false;
startButton.textContent = 'Click to start';
startButton.addEventListener('click', async () => {
  // the AudioContext must be created/resumed inside the user gesture
  const running = await audioJs.imports.resume();
  console.log(`HOST audio resume -> ${running}`);
  startButton.hidden = true;
  canvas.focus();
  await appExports.StartAudio(forceManagedDecoder);
});
console.log('HOST ready');
