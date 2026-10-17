// exp-06 host script: nothing Vulkan-specific happens in JS. The managed ICD is plain C# called through managed
// function pointers, so there are no Module.addFunction trampolines and no [JSExport] registrations (contrast exp-04).
import { dotnet } from './_framework/dotnet.js'

const qs = new URLSearchParams(location.search);
const appArgs = [...qs.entries()].map(([k, v]) => `${k}=${v}`);
const runtime = await dotnet.withApplicationArguments(...appArgs).create();
const exitCode = await runtime.runMain();
console.log(`EXP06 runMain exitCode=${exitCode}`);
