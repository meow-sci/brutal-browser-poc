using Brutal;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace WebGpuIcd.Tests;

/// <summary>Instance / physical device / device per profile VP_BRUTAL_web_poc_2026.</summary>
public sealed unsafe class ProfileTests
{
    private readonly RecordingBridge _bridge = TestIcd.Reset();

    [Fact]
    public void PhysicalDeviceReportsTheProfile()
    {
        using var dev = new TestDevice();
        var pd = dev.PhysicalDevice;
        var props = pd.GetProperties();
        Assert.Equal((1 << 22) | (3 << 12), props.ApiVersion); // non-conformant Vulkan 1.3
        Assert.Equal(0x106B, props.VendorID); // "apple" from adapter info
        Assert.Contains("WebGPU", pd.GetDeviceName());
        ref readonly var l = ref props.Limits;
        Assert.Equal(3, l.MaxBoundDescriptorSets);
        Assert.Equal(128, l.MaxPushConstantsSize);
        Assert.Equal(8, l.MaxVertexInputBindings);
        Assert.Equal(256UL, l.MinUniformBufferOffsetAlignment.Value);
        Assert.Equal(256UL, l.OptimalBufferCopyRowPitchAlignment.Value);
        Assert.Equal(1, l.MaxViewports);
        Assert.Equal(VkSampleCountFlags._1Bit | VkSampleCountFlags._4Bit, l.FramebufferColorSampleCounts);

        var memory = pd.GetMemoryProperties();
        Assert.Equal(2, memory.MemoryTypeCount);
        Assert.Equal(VkMemoryPropertyFlags.DeviceLocalBit, memory.MemoryTypes[0].PropertyFlags);
        Assert.Equal(VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit, memory.MemoryTypes[1].PropertyFlags);
        Assert.Equal(0, pd.FindQueueFamily(VkQueueFlags.GraphicsBit | VkQueueFlags.ComputeBit | VkQueueFlags.TransferBit));

        Assert.Equal(
            ["VK_KHR_swapchain", "VK_KHR_portability_subset", "VK_KHR_dynamic_rendering", "VK_KHR_synchronization2",
             "VK_KHR_maintenance1", "VK_KHR_maintenance2", "VK_KHR_maintenance3", "VK_KHR_maintenance4"],
            pd.ExtensionNames.ToArray());
        Assert.False(VulkanHelpers.Validation.IsSupported); // no layers

        var features = pd.GetFeatures();
        Assert.True(features.SamplerAnisotropy);
        Assert.False(features.GeometryShader);
        var f13 = new VkPhysicalDeviceVulkan13Features();
        var f2 = new VkPhysicalDeviceFeatures2 { Next = &f13 };
        Icd.vkGetPhysicalDeviceFeatures2(pd.Handle, &f2);
        Assert.True(f13.DynamicRendering);
        Assert.True(f13.Synchronization2);
        Assert.False(f13.InlineUniformBlock);
        Assert.False(f13.PrivateData); // non-conformant 1.3: required by the spec, not implemented
        Assert.False(f13.PipelineCreationCacheControl);
        Assert.True(f13.RobustImageAccess && f13.ShaderDemoteToHelperInvocation && f13.ShaderTerminateInvocation && f13.ShaderZeroInitializeWorkgroupMemory && f13.Maintenance4);
        var f12 = new VkPhysicalDeviceVulkan12Features();
        f2 = new VkPhysicalDeviceFeatures2 { Next = &f12 };
        Icd.vkGetPhysicalDeviceFeatures2(pd.Handle, &f2);
        Assert.False(f12.BufferDeviceAddress);
        Assert.False(f12.DescriptorIndexing);
        Assert.True(f12.ImagelessFramebuffer && f12.SeparateDepthStencilLayouts && f12.HostQueryReset);

        var format = pd.GetFormatProperties(VkFormat.D24UNormS8UInt);
        Assert.True((format.OptimalTilingFeatures & VkFormatFeatureFlags.DepthStencilAttachmentBit) != 0);
        Assert.Equal(VkFormatFeatureFlags.None, format.LinearTilingFeatures);
    }

    [Fact]
    public void Vulkan13PropertyStructsAreFilled()
    {
        using var dev = new TestDevice();
        var p11 = new VkPhysicalDeviceVulkan11Properties();
        var p12 = new VkPhysicalDeviceVulkan12Properties { Next = &p11 };
        var p13 = new VkPhysicalDeviceVulkan13Properties { Next = &p12 };
        var p2 = new VkPhysicalDeviceProperties2 { Next = &p13 };
        Icd.vkGetPhysicalDeviceProperties2(dev.PhysicalDevice.Handle, &p2);
        Assert.Equal((1 << 22) | (3 << 12), p2.Properties.ApiVersion);
        Assert.Equal(268435456UL, p13.MaxBufferSize.Value);
        Assert.Equal(0, p13.MaxInlineUniformBlockSize); // no inline uniform blocks
        Assert.Equal(Profile.MaxPerSetDescriptors, p11.MaxPerSetDescriptors);
        Assert.Equal(0, p11.SubgroupSize);
        Assert.Equal(VkSampleCountFlags._1Bit, p12.FramebufferIntegerColorSampleCounts);
        Assert.Equal(0, p12.MaxUpdateAfterBindDescriptorsInAllPools);
        Assert.Equal(0, p12.ConformanceVersion.Major + p12.ConformanceVersion.Minor + p12.ConformanceVersion.Subminor + p12.ConformanceVersion.Patch);
        Assert.Contains("BRUTAL web ICD", Icd.Str(p12.DriverName));
        Assert.Equal(Profile.Name, Icd.Str(p12.DriverInfo));
        Assert.DoesNotContain(TestIcd.Log, l => l.Contains("not filled"));
    }

    [Fact]
    public void PromotedVulkan13FeatureStructsAgreeWithVulkan13Features()
    {
        using var dev = new TestDevice();
        var robust = new VkPhysicalDeviceImageRobustnessFeatures();
        var terminate = new VkPhysicalDeviceShaderTerminateInvocationFeatures { Next = &robust };
        var zeroInit = new VkPhysicalDeviceZeroInitializeWorkgroupMemoryFeatures { Next = &terminate };
        var privateData = new VkPhysicalDevicePrivateDataFeatures { PrivateData = true, Next = &zeroInit }; // cleared by the query
        var f2 = new VkPhysicalDeviceFeatures2 { Next = &privateData };
        Icd.vkGetPhysicalDeviceFeatures2(dev.PhysicalDevice.Handle, &f2);
        Assert.True(robust.RobustImageAccess);
        Assert.True(terminate.ShaderTerminateInvocation);
        Assert.True(zeroInit.ShaderZeroInitializeWorkgroupMemory);
        Assert.False(privateData.PrivateData);

        // vkCreateDevice: the supported promoted structs pass, an unsupported one (privateData) is rejected like the aggregate struct
        var ok = new VkPhysicalDeviceImageRobustnessFeatures { RobustImageAccess = true };
        Assert.Equal(VkResult.Success, TryCreateDevice(dev.PhysicalDevice, null, &ok));
        var bad = new VkPhysicalDevicePrivateDataFeatures { PrivateData = true };
        Assert.Equal(VkResult.ErrorFeatureNotPresent, TryCreateDevice(dev.PhysicalDevice, null, &bad));
        var bad13 = new VkPhysicalDeviceVulkan13Features { PrivateData = true };
        Assert.Equal(VkResult.ErrorFeatureNotPresent, TryCreateDevice(dev.PhysicalDevice, null, &bad13));
    }

    [Fact]
    public void InstanceGlobalsFollowTheProfile()
    {
        int version;
        Icd.vkEnumerateInstanceVersion(&version);
        Assert.Equal((1 << 22) | (3 << 12), version);
        int count;
        Icd.vkEnumerateInstanceExtensionProperties(null, &count, null);
        var props = stackalloc VkExtensionProperties[count];
        Assert.Equal(VkResult.Success, Icd.vkEnumerateInstanceExtensionProperties(null, &count, props));
        var names = Enumerable.Range(0, count).Select(i => Icd.Str(props[i].ExtensionName)).ToArray();
        Assert.Equal(["VK_KHR_surface", "VK_KHR_get_physical_device_properties2", "VK_KHR_portability_enumeration", "VK_EXT_debug_utils", "VK_BRUTAL_web_surface"], names);
        Icd.vkEnumerateInstanceLayerProperties(&count, null);
        Assert.Equal(0, count);
    }

    private static VkResult TryCreateDevice(PhysicalDevice pd, VkPhysicalDeviceFeatures* features, void* next)
    {
        var priority = 1f;
        var queueInfo = new VkDeviceQueueCreateInfo { QueueFamilyIndex = 0, QueueCount = 1, QueuePriorities = &priority };
        try
        {
            using var device = pd.CreateDevice(new DeviceEx.CreateInfo
            {
                QueueCreateInfos = new ReadOnlySpan<VkDeviceQueueCreateInfo>(&queueInfo, 1),
                Features = features,
                Next = next,
            });
            return VkResult.Success;
        }
        catch (VkResultError e)
        {
            return e.Result;
        }
    }

    [Fact]
    public void CreateDeviceRejectsUnsupportedFeatures()
    {
        using var dev = new TestDevice();
        var features = new VkPhysicalDeviceFeatures { GeometryShader = true };
        Assert.Equal(VkResult.ErrorFeatureNotPresent, TryCreateDevice(dev.PhysicalDevice, &features, null));
        Assert.Contains(TestIcd.Errors(), l => l.Contains("VkPhysicalDeviceFeatures.GeometryShader"));

        var f12 = new VkPhysicalDeviceVulkan12Features { BufferDeviceAddress = true };
        Assert.Equal(VkResult.ErrorFeatureNotPresent, TryCreateDevice(dev.PhysicalDevice, null, &f12));
        Assert.Contains(TestIcd.Errors(), l => l.Contains("BufferDeviceAddress"));

        // dynamic rendering + synchronization2, also through the KHR feature structs, and Vulkan13Features are accepted
        var dr = new VkPhysicalDeviceDynamicRenderingFeatures { DynamicRendering = true };
        var s2 = new VkPhysicalDeviceSynchronization2Features { Synchronization2 = true, Next = &dr };
        Assert.Equal(VkResult.Success, TryCreateDevice(dev.PhysicalDevice, null, &s2));
        var f13 = new VkPhysicalDeviceVulkan13Features { DynamicRendering = true, Synchronization2 = true, Maintenance4 = true };
        var anisotropy = new VkPhysicalDeviceFeatures { SamplerAnisotropy = true };
        Assert.Equal(VkResult.Success, TryCreateDevice(dev.PhysicalDevice, &anisotropy, &f13));
    }

    [Fact]
    public void LimitsAreClampedByTheAdapter()
    {
        try
        {
            IcdTestHooks.ResetAdapter();
            _bridge.LimitsJson = "{\"maxTextureDimension2D\":4096,\"maxBindGroups\":4,\"minUniformBufferOffsetAlignment\":512,\"maxColorAttachments\":4}";
            Profile.EnsureInitialized();
            VkPhysicalDeviceProperties props;
            Profile.FillProperties(&props);
            Assert.Equal(4096, props.Limits.MaxImageDimension2D);
            Assert.Equal(4096, props.Limits.MaxFramebufferWidth);
            Assert.Equal(512UL, props.Limits.MinUniformBufferOffsetAlignment.Value);
            Assert.Equal(4, props.Limits.MaxColorAttachments);
        }
        finally
        {
            IcdTestHooks.ResetAdapter();
            _bridge.LimitsJson = new RecordingBridge().LimitsJson;
            Profile.EnsureInitialized();
        }
    }

    [Fact]
    public void SurfaceQueriesDescribeTheCanvas()
    {
        using var dev = new TestDevice();
        Assert.Equal(0, Brutal.Browser.BrowserSurfaces.CreateSurface!(dev.Instance.VkHandle, 1, out var raw));
        var surface = new VkSurfaceKHR((nint)raw);
        var pd = dev.PhysicalDevice;
        Assert.True(pd.GetSurfaceSupportKHR(0, surface));
        var caps = pd.GetSurfaceCapabilitiesKHR(surface);
        Assert.Equal((640, 480), (caps.CurrentExtent.Width, caps.CurrentExtent.Height));
        Assert.Equal((2, 3), (caps.MinImageCount, caps.MaxImageCount));
        Assert.Equal(VkCompositeAlphaFlagsKHR.OpaqueBitKHR, caps.SupportedCompositeAlpha);
        Span<VkSurfaceFormatKHR> formats = stackalloc VkSurfaceFormatKHR[pd.GetSurfaceFormatsKHR_Count(surface)];
        pd.GetSurfaceFormatsKHR_Write(surface, formats);
        Assert.Equal([VkFormat.B8G8R8A8UNorm, VkFormat.B8G8R8A8SRGB], formats.ToArray().Select(f => f.Format));
        Assert.Equal(1, pd.GetSurfacePresentModesKHR_Count(surface));
        Assert.NotEqual(0, Brutal.Browser.BrowserSurfaces.CreateSurface!(dev.Instance.VkHandle, 2, out _)); // one canvas only
        dev.Instance.DestroySurfaceKHR(surface, null);
    }

    [Fact]
    public void SrgbSwapchainsConfigureALinearCanvasWithAnSrgbViewFormat()
    {
        using var dev = new TestDevice();
        Assert.Equal(0, Brutal.Browser.BrowserSurfaces.CreateSurface!(dev.Instance.VkHandle, 1, out var raw));
        var surface = new VkSurfaceKHR((nint)raw);
        var swapchain = dev.Device.CreateSwapchainKHR(new VkSwapchainCreateInfoKHR
        {
            Surface = surface, MinImageCount = 3, ImageFormat = VkFormat.B8G8R8A8SRGB, ImageColorSpace = VkColorSpaceKHR.SRGBNonlinearKHR,
            ImageExtent = new VkExtent2D(640, 480), ImageArrayLayers = 1, ImageUsage = VkImageUsageFlags.ColorAttachmentBit,
            PreTransform = VkSurfaceTransformFlagsKHR.IdentityBitKHR, CompositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueBitKHR, PresentMode = VkPresentModeKHR.FifoKHR,
        }, null);
        Assert.Equal(["bgra8unorm", GpuTextureUsage.RenderAttachment | GpuTextureUsage.CopySrc, "[\"bgra8unorm-srgb\"]", "opaque"],
            _bridge.Named("configureCanvas").Single().Args!);
        var images = new VkImage[dev.Device.GetSwapchainImagesKHR_Count(swapchain)];
        dev.Device.GetSwapchainImagesKHR_Write(swapchain, images);
        Assert.Equal(3, images.Length);
        var view = dev.Device.CreateImageView(new VkImageViewCreateInfo
        {
            Image = images[0], ViewType = VkImageViewType._2D, Format = VkFormat.B8G8R8A8SRGB, SubresourceRange = Subresources.Full(VkImageAspectFlags.ColorBit),
        }, null);
        Assert.Equal(ReservedHandles.CanvasViewSrgb, HandleTable.Get<ImageViewObject>(view.VkHandle)!.AttachmentGpu);
        Assert.Empty(_bridge.Named("createTextureView")); // nothing is created: resolved to the frame's canvas texture at replay

        Assert.Equal(VkResult.Success, dev.Device.AcquireNextImageKHR(swapchain, -1, default, default, out var index));
        Assert.Equal(0, index);
        Assert.Equal(VkResult.Success, dev.Device.AcquireNextImageKHR(swapchain, -1, default, default, out index));
        Assert.Equal(1, index);
        dev.Device.DestroyImageView(view, null);
        dev.Device.DestroySwapchainKHR(swapchain, null);
        dev.Instance.DestroySurfaceKHR(surface, null);
    }

    [Fact]
    public void MissingShadersFailWithAnErrorNamingTheHash()
    {
        using var dev = new TestDevice();
        var code = new uint[] { 0x07230203, 0x00010300, 0, 1, 0 };
        fixed (uint* p = code)
        {
            var info = new VkShaderModuleCreateInfo { CodeSize = (ByteSize64)(ulong)(code.Length * 4), Code = (int*)p };
            VkShaderModule module;
            Assert.Equal(VkResult.ErrorInvalidShaderNV, Icd.vkCreateShaderModule(dev.Device.Handle, &info, null, &module));
        }
        var hash = ShaderHash.Hex(System.Runtime.InteropServices.MemoryMarshal.AsBytes(code.AsSpan()));
        Assert.Contains(TestIcd.Errors(), l => l.Contains(hash) && l.Contains("not in the shader manifest"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(55)]
    [InlineData(56)]
    [InlineData(64)]
    [InlineData(1000)]
    public void ManagedSha256MatchesTheFramework(int length)
    {
        var data = Enumerable.Range(0, length).Select(i => (byte)(i * 31)).ToArray();
        Span<byte> managed = stackalloc byte[32];
        ShaderHash.ManagedSha256(data, managed);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(data), managed.ToArray());
    }
}
