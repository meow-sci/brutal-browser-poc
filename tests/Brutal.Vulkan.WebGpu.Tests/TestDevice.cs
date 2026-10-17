using Brutal;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace WebGpuIcd.Tests;

/// <summary>Instance + DeviceEx + queue created through BRUTAL's API against the ICD (for focused tests).</summary>
internal sealed unsafe class TestDevice : IDisposable
{
    public readonly Instance Instance;
    public readonly PhysicalDevice PhysicalDevice;
    public readonly DeviceEx Device;
    public readonly Queue Queue;

    public TestDevice()
    {
        Instance = Vulkan.CreateInstance(new Instance.CreateInfo
        {
            AppInfo = new VkApplicationInfo { ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3 },
            EnabledExtensions = ["VK_KHR_surface", "VK_BRUTAL_web_surface"],
            EnabledLayers = [],
        });
        PhysicalDevice = Instance.PhysicalDevices[0];
        var priority = 1f;
        var queueInfo = new VkDeviceQueueCreateInfo { QueueFamilyIndex = 0, QueueCount = 1, QueuePriorities = &priority };
        Device = PhysicalDevice.CreateDevice(new DeviceEx.CreateInfo
        {
            Extensions = [VK.KHR_SWAPCHAIN_EXTENSION_NAME],
            QueueCreateInfos = new ReadOnlySpan<VkDeviceQueueCreateInfo>(&queueInfo, 1),
        });
        Queue = Device.GetQueue(0, 0);
    }

    public BufferEx HostBuffer(int size, VkBufferUsageFlags usage) => Device.CreateBuffer(new BufferEx.CreateInfo
    {
        Name = "host", BufferSize = ByteSize.Of<byte>(size), BufferUsage = usage, AllocPreference = MemoryPreference.PreferCpu,
        AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,
    });

    public BufferEx DeviceBuffer(int size, VkBufferUsageFlags usage) => Device.CreateBuffer(new BufferEx.CreateInfo
    {
        Name = "device", BufferSize = ByteSize.Of<byte>(size), BufferUsage = usage, AllocPreference = MemoryPreference.PreferGpu,
        AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
    });

    public CommandBuffer CommandBuffer(out VkCommandPool pool)
    {
        pool = Device.CreateCommandPool(new VkCommandPoolCreateInfo { QueueFamilyIndex = 0 }, null);
        return Device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = pool, Level = VkCommandBufferLevel.Primary });
    }

    public void Dispose()
    {
        Device.Dispose();
        Instance.Dispose();
    }
}

/// <summary>Decoded replay streams.</summary>
internal static class Streams
{
    public static List<(Op Op, uint[] Payload)> Decode(uint[] words) => IcdTestHooks.Decode(words);

    public static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);
}
