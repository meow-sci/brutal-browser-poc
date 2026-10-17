// exp-06 DesktopUnmanagedSmoke: the unmanaged flavor of the same source-built projects still behaves like exp-04.
//   --uco     (default) [UnmanagedCallersOnly] managed ICD through the AppContext hook
//   --native  native Vulkan loader + MoltenVK (no AppContext key)
using System.Reflection;
using Brutal.VulkanApi;
using Exp04;
using Exp06;

var info = typeof(Vulkan).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
var absInfo = typeof(Brutal.VulkanApi.Abstractions.DeviceEx).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
var field = typeof(VkCmdDraw).GetField("Invoke")!.FieldType;
Console.WriteLine($"exp-06 DesktopUnmanagedSmoke: Brutal.Vulkan={info}; Abstractions={absInfo}; VkCmdDraw.Invoke IsUnmanagedFunctionPointer={field.IsUnmanagedFunctionPointer}");
if (args.Contains("--native"))
    return NativeSmoke.Run();
return ManagedLoaderScenario.Run(Console.WriteLine, UcoEntryPoints.Register());
