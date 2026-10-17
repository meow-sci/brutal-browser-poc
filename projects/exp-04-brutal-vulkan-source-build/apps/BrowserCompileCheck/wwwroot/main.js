// exp-04 host script. Creates JS trampolines (Module.addFunction) that forward Vulkan calls into C# [JSExport]
// methods (BrowserIcd), registers their wasm function-table indices with C#, then runs Main.
import { dotnet } from './_framework/dotnet.js'

const qs = new URLSearchParams(location.search);
const mode = qs.get('mode') || 'js';
const appArgs = ['mode=' + mode, ...(qs.get('callnull') ? ['callnull'] : [])];
const runtime = await dotnet.withApplicationArguments(...appArgs).create();
const { getAssemblyExports, getConfig, runMain, Module } = runtime;
const exports = await getAssemblyExports(getConfig().mainAssemblyName);
const icd = exports.Exp04.BrowserIcd;

// emscripten signature strings (return type first): i = i32 (pointers, handles, enums, VkResult), j = i64, v = void
const sigs = {
  vkGetInstanceProcAddr: 'iii', vkGetDeviceProcAddr: 'iii',
  vkEnumerateInstanceVersion: 'ii', vkEnumerateInstanceExtensionProperties: 'iiii', vkEnumerateInstanceLayerProperties: 'iii',
  vkCreateInstance: 'iiii', vkDestroyInstance: 'vii', vkEnumeratePhysicalDevices: 'iiii',
  vkGetPhysicalDeviceProperties: 'vii', vkGetPhysicalDeviceQueueFamilyProperties: 'viii', vkGetPhysicalDeviceMemoryProperties: 'vii',
  vkEnumerateDeviceExtensionProperties: 'iiiii', vkCreateDevice: 'iiiii',
  vkDestroyDevice: 'vii', vkGetDeviceQueue: 'viiii', vkCreateBuffer: 'iiiii', vkDestroyBuffer: 'viii',
  vkGetBufferMemoryRequirements: 'viii', vkAllocateMemory: 'iiiii', vkFreeMemory: 'viii', vkBindBufferMemory: 'iiiij',
};
if (mode === 'js') {
  for (const [name, sig] of Object.entries(sigs)) {
    const target = icd[name.slice(2)];
    const index = Module.addFunction((...a) => target(...a), sig);
    icd.Register(name, index);
  }
  console.log(`EXP04 registered ${Object.keys(sigs).length} JS trampolines`);
}
const exitCode = await runMain();
console.log(`EXP04 runMain exitCode=${exitCode}`);
