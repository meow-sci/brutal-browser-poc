using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace HelloMesh.Rendering;

/// <summary>Instance, window surface, physical device choice, Vulkan 1.3 device and the single graphics+present queue.</summary>
public sealed class VulkanContext : IDisposable
{
    public Instance Instance { get; }
    public VkSurfaceKHR Surface { get; }
    public PhysicalDevice PhysicalDevice { get; }
    public DeviceEx Device { get; }
    public Queue Queue { get; }
    public int QueueFamily { get; }
    public string DeviceName { get; }

    /// <summary><c>minUniformBufferOffsetAlignment</c> of the device; dynamic uniform offsets must be multiples of it.</summary>
    public int UniformOffsetAlignment { get; }

    public unsafe VulkanContext(GlfwWindow window)
    {
        Instance = CreateInstance();
        if (Glfw.CreateWindowSurface(Instance.VkHandle, window, out var rawSurface) != 0)
            throw new InvalidOperationException("Could not create the window surface.");
        Surface = new VkSurfaceKHR((nint)rawSurface);

        (PhysicalDevice, QueueFamily) = ChoosePhysicalDevice();
        DeviceName = PhysicalDevice.GetDeviceName();
        UniformOffsetAlignment = (int)PhysicalDevice.GetProperties().Limits.MinUniformBufferOffsetAlignment.Value;

        var extensions = new HashSet<string>();
        VulkanHelpers.AddSwapchainExtensions(extensions);
        VulkanHelpers.AddMandatoryIfSupportedDeviceExtensions(extensions, PhysicalDevice);
        var features = new VkPhysicalDeviceVulkan13Features { DynamicRendering = true, Synchronization2 = true };
        var priority = 1f;
        var queueInfo = new VkDeviceQueueCreateInfo { QueueFamilyIndex = QueueFamily, QueueCount = 1, QueuePriorities = &priority };
        Device = PhysicalDevice.CreateDevice(new DeviceEx.CreateInfo
        {
            Extensions = extensions,
            QueueCreateInfos = new ReadOnlySpan<VkDeviceQueueCreateInfo>(&queueInfo, 1),
            Next = &features,
        });
        Queue = Device.GetQueue(QueueFamily, 0);
    }

    private static unsafe Instance CreateInstance()
    {
        var extensions = new HashSet<string>();
        VulkanHelpers.AddPlatformRequiredExtensions(extensions);
        VulkanHelpers.AddSurfaceExtensions(extensions);
        var required = Glfw.GetRequiredInstanceExtensions(out var count);
        for (var i = 0; i < count; i++)
            extensions.Add(Marshal.PtrToStringUTF8((nint)required[i])!);

        var layers = new HashSet<string>();
        if (GraphicsConfig.EnableValidation)
        {
            if (VulkanHelpers.Validation.IsSupported)
            {
                VulkanHelpers.AddValidationLayers(layers);
                extensions.Add(VK.EXT_DEBUG_UTILS_EXTENSION_NAME);
            }
            else
            {
                Console.WriteLine("Validation layer not available; continuing without it.");
            }
        }

        fixed (byte* name = "HelloMesh"u8)
        {
            return Vulkan.CreateInstance(new Instance.CreateInfo
            {
                AppInfo = new VkApplicationInfo { ApplicationName = name, EngineName = name, ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3 },
                Flags = VulkanHelpers.GetPlatformRequiredInstanceCreateFlags(),
                EnabledExtensions = extensions,
                EnabledLayers = layers,
            });
        }
    }

    private (PhysicalDevice Device, int QueueFamily) ChoosePhysicalDevice()
    {
        (PhysicalDevice Device, int QueueFamily)? best = null;
        var bestScore = -1;
        foreach (var candidate in Instance.PhysicalDevices)
        {
            if (!candidate.SupportsExtension(VK.KHR_SWAPCHAIN_EXTENSION_NAME) || FindPresentQueueFamily(candidate) is not { } family)
                continue;
            var score = candidate.GetProperties().DeviceType switch
            {
                VkPhysicalDeviceType.DiscreteGpu => 3,
                VkPhysicalDeviceType.IntegratedGpu => 2,
                VkPhysicalDeviceType.VirtualGpu => 1,
                _ => 0,
            };
            if (score > bestScore)
                (best, bestScore) = ((candidate, family), score);
        }
        return best ?? throw new InvalidOperationException("No Vulkan device can render to the window.");
    }

    private int? FindPresentQueueFamily(PhysicalDevice device)
    {
        var properties = new VkQueueFamilyProperties[device.GetQueueFamilyProperties_Count()];
        device.GetQueueFamilyProperties_Write(properties);
        for (var family = 0; family < properties.Length; family++)
            if (properties[family].QueueFlags.HasFlag(VkQueueFlags.GraphicsBit) && device.GetSurfaceSupportKHR(family, Surface))
                return family;
        return null;
    }

    public void Dispose()
    {
        Device.Dispose();
        Instance.DestroySurfaceKHR(Surface, null);
        Instance.Dispose();
    }
}
