// exp-06: body copied from projects/exp-04-brutal-vulkan-source-build/apps/DesktopInstanceSmoke/Program.cs (native MoltenVK path).
using System.Reflection;
using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace Exp06;

public static unsafe class NativeSmoke
{
    public static int Run()
    {
        Console.WriteLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} / {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
        foreach (var k in new[] { "VULKAN_SDK", "VK_ICD_FILENAMES", "VK_DRIVER_FILES", "DYLD_LIBRARY_PATH", "VK_ADD_LAYER_PATH" })
            Console.WriteLine($"env {k}={Environment.GetEnvironmentVariable(k) ?? "<unset>"}");
        Console.WriteLine($"AppContext[BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR]={AppContext.GetData("BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR") ?? "<unset>"}");
    
        var vkAsm = typeof(Vulkan).Assembly;
        Console.WriteLine($"Brutal.Vulkan: {vkAsm.GetName().FullName}; informational={vkAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}; location={vkAsm.Location}");
        var absAsm = typeof(DeviceEx).Assembly;
        Console.WriteLine($"Brutal.Vulkan.Abstractions: {absAsm.GetName().FullName}; informational={absAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
    
        int apiVersion;
        Vulkan.EnumerateInstanceVersion(&apiVersion);
        Console.WriteLine($"vkEnumerateInstanceVersion -> {VulkanDeviceInfo.FormatApiVersion(apiVersion)}");
        Console.WriteLine($"vkGetInstanceProcAddr (native) = 0x{(nint)Vulkan.GetInstanceProcAddr.Invoke:x}");
    
        Span<VkExtensionProperties> exts = new VkExtensionProperties[Vulkan.EnumerateInstanceExtensionProperties_Count([])];
        Vulkan.EnumerateInstanceExtensionProperties_Write([], exts);
        var extNames = new List<string>();
        foreach (ref var e in exts)
            fixed (byte* n = e.ExtensionName)
                extNames.Add(System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)n)!);
        Console.WriteLine($"Instance extensions ({extNames.Count}): {string.Join(", ", extNames)}");
        Console.WriteLine($"Abstractions VulkanHelpers.Validation.IsSupported -> {VulkanHelpers.Validation.IsSupported}");
    
        var appName = "exp-04 DesktopInstanceSmoke\0"u8;
        Instance instance;
        fixed (byte* pAppName = appName)
        {
            instance = Vulkan.CreateInstance(
                new Instance.CreateInfo
                {
                    AppInfo = new VkApplicationInfo { ApplicationName = pAppName, ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3 },
                    Flags = VkInstanceCreateFlags.EnumeratePortabilityBitKHR,
                    EnabledExtensions = [VK.KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME, VK.KHR_GET_PHYSICAL_DEVICE_PROPERTIES_2_EXTENSION_NAME],
                    EnabledLayers = [],
                }
            );
        }
        Console.WriteLine($"Instance created: handle=0x{instance.VkHandle:x}; physical devices={instance.PhysicalDevices.Length}");
        Console.WriteLine($"Instance table: vkCreateDevice={(instance.PInvoke.vkCreateDevice.Invoke != null ? "set" : "NULL")}, vkCreateMetalSurfaceEXT={(instance.PInvoke.vkCreateMetalSurfaceEXT.Invoke != null ? "set" : "NULL")}, vkCreateWin32SurfaceKHR={(instance.PInvoke.vkCreateWin32SurfaceKHR.Invoke != null ? "set" : "NULL")}");
    
        foreach (var pd in instance.PhysicalDevices)
        {
            var props = pd.GetProperties();
            Console.WriteLine($"  PhysicalDevice: '{pd.GetDeviceName()}' api={VulkanDeviceInfo.FormatApiVersion(props.ApiVersion)} driver={VulkanDeviceInfo.FormatDriverVersion(props)} vendor=0x{props.VendorID:x} type={props.DeviceType}");
        }
    
        var physicalDevice = instance.PhysicalDevices[0];
        var family = physicalDevice.FindQueueFamily(VkQueueFlags.GraphicsBit);
        var portabilitySubset = physicalDevice.SupportsExtension(VK.KHR_PORTABILITY_SUBSET_EXTENSION_NAME);
        Console.WriteLine($"Graphics queue family={family}; device extensions={physicalDevice.ExtensionNames.Count}; VK_KHR_portability_subset={portabilitySubset}");
    
        var priority = 1.0f;
        Span<VkDeviceQueueCreateInfo> queues = [new VkDeviceQueueCreateInfo { QueueFamilyIndex = family, QueueCount = 1, QueuePriorities = &priority }];
        var device = physicalDevice.CreateDevice(
            new DeviceEx.CreateInfo
            {
                QueueCreateInfos = queues,
                Extensions = portabilitySubset ? [VK.KHR_PORTABILITY_SUBSET_EXTENSION_NAME] : [],
            }
        );
        Console.WriteLine($"DeviceEx created: handle=0x{device.VkHandle:x}");
        var queue = device.GetQueue(family, 0);
        Console.WriteLine($"Queue: handle=0x{queue.VkHandle:x}");
        Console.WriteLine($"Device table: vkCreateBuffer={(device.PInvoke.vkCreateBuffer.Invoke != null ? "set" : "NULL")}, vxDestroyBuffers={(device.PInvoke.vxDestroyBuffers.Invoke != null ? "set" : "NULL")}, vxDestroyImages={(device.PInvoke.vxDestroyImages.Invoke != null ? "set" : "NULL")}");
    
        var buffer = device.AllocateBuffer(
            "exp04-buffer",
            new VkBufferCreateInfo
            {
                Size = new ByteSize64(1024),
                Usage = VkBufferUsageFlags.StorageBufferBit | VkBufferUsageFlags.TransferDstBit,
                SharingMode = VkSharingMode.Exclusive,
            },
            new AllocationInfo.CreateInfo(MemoryPreference.PreferCpu, VkMemoryPropertyFlags.HostVisibleBit, VkMemoryPropertyFlags.None, 1.0f, null)
        );
        Console.WriteLine($"AllocateBuffer: buffer=0x{buffer.VkBuffer.VkHandle:x} memory=0x{buffer.AllocationInfo.Memory.VkHandle:x} memoryType={buffer.AllocationInfo.MemoryTypeIndex} allocSize={buffer.AllocationInfo.AllocSize}");
        device.FreeAllocation(buffer);
        device.Dispose();
        instance.Dispose();
        Console.WriteLine("Disposed device + instance.");
        Console.WriteLine("RESULT: OK");
        return 0;
    }
}
