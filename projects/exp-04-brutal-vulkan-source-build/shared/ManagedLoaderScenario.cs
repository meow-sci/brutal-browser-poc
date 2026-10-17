// exp-04: drives the source-built Brutal.Vulkan (+ NuGet Brutal.Vulkan.Abstractions) through FakeIcd.
using System.Reflection;
using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace Exp04;

public static unsafe class ManagedLoaderScenario
{
    /// <param name="getInstanceProcAddr">The vkGetInstanceProcAddr pointer to install (UCO pointer or JS trampoline index).</param>
    public static int Run(Action<string> print, nint getInstanceProcAddr, bool callNullFunction = false)
    {
        print($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} / {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}; IsBrowser={OperatingSystem.IsBrowser()}; nint={sizeof(nint) * 8}-bit");

        // 1. Install the managed loader BEFORE anything touches Brutal.VulkanApi.Vulkan.
        FakeIcd.Install(getInstanceProcAddr);
        print($"AppContext[{FakeIcd.AppContextKey}] = 0x{getInstanceProcAddr:x}");

        var vkAsm = typeof(Vulkan).Assembly;
        print($"Brutal.Vulkan: {vkAsm.GetName().FullName}; informational={vkAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        var absAsm = typeof(DeviceEx).Assembly;
        print($"Brutal.Vulkan.Abstractions: {absAsm.GetName().FullName}; informational={absAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");

        // 2. Global commands (routed through vkGetInstanceProcAddr(NULL, ...)).
        int apiVersion;
        Vulkan.EnumerateInstanceVersion(&apiVersion);
        print($"vkEnumerateInstanceVersion -> {VulkanDeviceInfo.FormatApiVersion(apiVersion)}");
        print($"vkEnumerateInstanceExtensionProperties count -> {Vulkan.EnumerateInstanceExtensionProperties_Count([])}");
        print($"Abstractions VulkanHelpers.Validation.IsSupported -> {VulkanHelpers.Validation.IsSupported}");
        print($"Vulkan.GetInstanceProcAddr.Invoke == supplied pointer: {(nint)Vulkan.GetInstanceProcAddr.Invoke == getInstanceProcAddr}");

        // 3. Instance.
        var appName = "exp-04 ManagedLoaderSmoke\0"u8;
        Instance instance;
        fixed (byte* pAppName = appName)
        {
            instance = Vulkan.CreateInstance(
                new Instance.CreateInfo
                {
                    AppInfo = new VkApplicationInfo
                    {
                        ApplicationName = pAppName,
                        ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3,
                    },
                    Flags = VkInstanceCreateFlags.EnumeratePortabilityBitKHR,
                    EnabledExtensions = [VK.KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME, VK.KHR_GET_PHYSICAL_DEVICE_PROPERTIES_2_EXTENSION_NAME],
                    EnabledLayers = [],
                }
            );
        }
        print($"Instance created: handle=0x{instance.VkHandle:x}; physical devices={instance.PhysicalDevices.Length}");
        var instanceTableNames = FakeIcd.InstanceProcNames.Count;

        foreach (var pd in instance.PhysicalDevices)
        {
            var props = pd.GetProperties();
            print($"  PhysicalDevice 0x{pd.VkHandle:x}: '{pd.GetDeviceName()}' api={VulkanDeviceInfo.FormatApiVersion(props.ApiVersion)} type={props.DeviceType}");
        }

        if (callNullFunction)
        {
            print("Calling physicalDevice.GetFeatures() -> vkGetPhysicalDeviceFeatures, for which the ICD returned NULL ...");
            var features = instance.PhysicalDevices[0].GetFeatures();
            print($"  survived?! robustBufferAccess={features.RobustBufferAccess}");
            return 2;
        }

        // 4. Device (via the NuGet Abstractions DeviceEx helper), queue, buffer.
        var physicalDevice = instance.PhysicalDevices[0];
        var family = physicalDevice.FindQueueFamily(VkQueueFlags.GraphicsBit);
        print($"FindQueueFamily(Graphics) -> {family}");
        var priority = 1.0f;
        Span<VkDeviceQueueCreateInfo> queues =
        [
            new VkDeviceQueueCreateInfo
            {
                QueueFamilyIndex = family,
                QueueCount = 1,
                QueuePriorities = &priority,
            },
        ];
        var device = physicalDevice.CreateDevice(new DeviceEx.CreateInfo { QueueCreateInfos = queues });
        print($"DeviceEx created: handle=0x{device.VkHandle:x}");
        var queue = device.GetQueue(family, 0);
        print($"Queue: handle=0x{queue.VkHandle:x}");

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
        print($"AllocateBuffer: buffer=0x{buffer.VkBuffer.VkHandle:x} memory=0x{buffer.AllocationInfo.Memory.VkHandle:x} memoryType={buffer.AllocationInfo.MemoryTypeIndex} size={buffer.AllocationInfo.AllocSize}");
        device.FreeAllocation(buffer);
        device.Dispose();
        instance.Dispose();
        print("Disposed device + instance.");

        // 5. Summaries.
        var instNull = FakeIcd.InstanceProcNames.Count(l => l.EndsWith("\tNULL"));
        var devNull = FakeIcd.DeviceProcNames.Count(l => l.EndsWith("\tNULL"));
        print($"vkGetInstanceProcAddr requests: {FakeIcd.InstanceProcNames.Count} ({instNull} answered NULL); after instance ctor: {instanceTableNames}");
        print($"vkGetDeviceProcAddr requests: {FakeIcd.DeviceProcNames.Count} ({devNull} answered NULL)");
        print("ICD call log:");
        foreach (var c in FakeIcd.Calls)
            print("  " + c);
        print("RESULT: OK");
        return 0;
    }
}
