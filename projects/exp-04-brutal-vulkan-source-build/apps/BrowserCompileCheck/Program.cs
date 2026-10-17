// exp-04 browser entry point. main.js passes "mode=js" (default) or "mode=uco", plus "callnull" for ?callnull=1.
//   mode=uco : hand Brutal.Vulkan the [UnmanagedCallersOnly] vkGetInstanceProcAddr pointer (desktop-style managed ICD)
//   mode=js  : hand it a JS Module.addFunction trampoline that forwards into [JSExport] C# (BrowserIcd)
using Exp04;

var mode = args.FirstOrDefault(a => a.StartsWith("mode="))?.Substring(5) ?? "js";
Console.WriteLine($"EXP04 BrowserCompileCheck mode={mode} args=[{string.Join(",", args)}]");
var rc = 1;
try
{
    nint gipa = mode == "uco" ? UcoEntryPoints.Register() : FakeIcd.EntryPoints.GetValueOrDefault("vkGetInstanceProcAddr");
    Console.WriteLine($"EXP04 vkGetInstanceProcAddr pointer for mode={mode}: 0x{gipa:x}; registered entry points={FakeIcd.EntryPoints.Count} ({FakeIcd.EntryPoints.Count(kv => kv.Value == 0)} are 0)");
    rc = ManagedLoaderScenario.Run(Console.WriteLine, gipa, callNullFunction: args.Contains("callnull"));
    Console.WriteLine("EXP04 instance-proc-names BEGIN");
    foreach (var l in FakeIcd.InstanceProcNames) Console.WriteLine("EXP04I " + l);
    Console.WriteLine("EXP04 device-proc-names BEGIN");
    foreach (var l in FakeIcd.DeviceProcNames) Console.WriteLine("EXP04D " + l);
}
catch (Exception e)
{
    Console.WriteLine($"EXP04 EXCEPTION {e.GetType().FullName}: {e.Message}");
    for (var inner = e.InnerException; inner != null; inner = inner.InnerException)
        Console.WriteLine($"EXP04   inner {inner.GetType().FullName}: {inner.Message}");
}
Console.WriteLine($"EXP04 DONE rc={rc}");
return rc;
