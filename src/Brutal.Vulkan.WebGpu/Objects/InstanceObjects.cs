using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

internal sealed class InstanceObject : IcdObject
{
    public override string VkTypeName => "VkInstance";
    public readonly HashSet<string> Extensions = new(StringComparer.Ordinal);
    public int ApiVersion;
    public PhysicalDeviceObject PhysicalDevice = null!;
    /// <summary>Messenger from VkInstanceCreateInfo.pNext (active during vkCreateInstance/vkDestroyInstance and the instance lifetime).</summary>
    public DebugMessengerObject? CreateInfoMessenger;
}

internal sealed class PhysicalDeviceObject : IcdObject
{
    public override string VkTypeName => "VkPhysicalDevice";
    public InstanceObject Instance = null!;
}

internal sealed class DeviceObject : IcdObject
{
    public override string VkTypeName => "VkDevice";
    public PhysicalDeviceObject PhysicalDevice = null!;
    public QueueObject Queue = null!;
    public readonly HashSet<string> Extensions = new(StringComparer.Ordinal);
}

internal sealed class QueueObject : IcdObject
{
    public override string VkTypeName => "VkQueue";
    public DeviceObject Device = null!;
    /// <summary>The concatenated stream of the submit being replayed (reused across submits).</summary>
    public readonly CommandStream SubmitStream = new(4096);
}

internal sealed unsafe class DebugMessengerObject : IcdObject
{
    public override string VkTypeName => "VkDebugUtilsMessengerEXT";
    public VkDebugUtilsMessageSeverityFlagsEXT Severities;
    public VkDebugUtilsMessageTypeFlagsEXT Types;
    public PfnVkDebugUtilsMessengerCallbackEXT Callback;
    public void* UserData;
}

internal sealed class SurfaceObject : IcdObject
{
    public override string VkTypeName => "VkSurfaceKHR";
    public nint CanvasHandle;
    public SwapchainObject? Swapchain;
}
