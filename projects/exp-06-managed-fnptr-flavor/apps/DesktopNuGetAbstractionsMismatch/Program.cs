// exp-06: managed-flavor Brutal.Vulkan + NuGet Brutal.Vulkan.Abstractions 2026.10.2 (compiled against unmanaged fnptrs).
using System.Reflection;
using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Exp06;

unsafe
{
    void Try(string what, Action a)
    {
        try { a(); Console.WriteLine($"OK      {what}"); }
        catch (Exception e) { Console.WriteLine($"THROWS  {what}: {e.GetType().FullName}: {e.Message}"); }
    }
    Console.WriteLine($"Brutal.Vulkan={typeof(Vulkan).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
    Console.WriteLine($"Brutal.Vulkan.Abstractions={typeof(DeviceEx).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} ({typeof(DeviceEx).Assembly.Location})");
    ManagedIcd.Install();
    var instance = Vulkan.CreateInstance(new Instance.CreateInfo { AppInfo = new VkApplicationInfo { ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3 }, EnabledExtensions = [], EnabledLayers = [] });
    Console.WriteLine($"OK      Vulkan.CreateInstance (Brutal.Vulkan only): physicalDevices={instance.PhysicalDevices.Length}");
    DeviceEx? device = null;
    Try("PhysicalDevice.CreateDevice (NuGet Abstractions DeviceEx)", () =>
    {
        var priority = 1.0f;
        Span<VkDeviceQueueCreateInfo> q = [new VkDeviceQueueCreateInfo { QueueFamilyIndex = 0, QueueCount = 1, QueuePriorities = &priority }];
        device = instance.PhysicalDevices[0].CreateDevice(new DeviceEx.CreateInfo { QueueCreateInfos = q });
    });
    Try("DeviceEx.AllocateBuffer + FreeAllocation (NuGet Abstractions -> Brutal.Vulkan extension methods)", () =>
    {
        var b = device!.AllocateBuffer("x", new VkBufferCreateInfo { Size = new ByteSize64(64), Usage = VkBufferUsageFlags.StorageBufferBit },
            new AllocationInfo.CreateInfo(MemoryPreference.PreferCpu, VkMemoryPropertyFlags.HostVisibleBit, VkMemoryPropertyFlags.None, 1.0f, null));
        device.FreeAllocation(b);
    });
    var cb = new CommandBuffer(device!, new VkCommandBuffer(0x5000));
    Try("CommandBufferExtensions.PushConstants<float> (NuGet Abstractions IL: ldfld VkCmdPushConstants::Invoke + calli unmanaged cdecl)", () =>
        cb.PushConstants(new VkPipelineLayout(0x7002), VkShaderStageFlags.VertexBit, ByteSize.Zero, 1.5f));
    Console.WriteLine($"        ICD saw vkCmdPushConstants size={ManagedIcd.Probe.PcSize} firstFloat={ManagedIcd.Probe.PcFirstFloat}");
    device?.Dispose();
    instance.Dispose();
    Console.WriteLine("DONE");
}
