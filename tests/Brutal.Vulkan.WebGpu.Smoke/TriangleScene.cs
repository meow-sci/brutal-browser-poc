using System.Runtime.InteropServices;
using Brutal;
using Brutal.Browser;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace WebGpuIcdSmoke;

/// <summary>
/// A game-shaped Vulkan program written only against BRUTAL's API (the desktop hello-mesh recipe, dynamic rendering): instance
/// with VK_BRUTAL_web_surface → surface → DeviceEx → swapchain → depth image → vertex buffer (BufferEx + StagingPool) → 2x2
/// texture (staging + vkCmdCopyBufferToImage) → sampler → descriptor set (dynamic UBO + texture + sampler) → pipeline with
/// depth test and a push constant → per frame: acquire, record, submit, present. Two triangles: a front one (z 0.25) and a
/// larger back one drawn after it (z 0.75) that must be hidden by the depth test where they overlap.
/// Shared by the browser smoke app and the desktop unit tests (which run it against a recording bridge).
/// </summary>
public sealed unsafe class TriangleScene : IDisposable
{
    public const int FramesInFlight = 2;
    public static readonly float4 ClearColor = new(0.1f, 0.2f, 0.3f, 1f);
    /// <summary>Uniform slots are 256 bytes apart (minUniformBufferOffsetAlignment); a slot holds mat4 mvp + vec4 tint.</summary>
    public const int UniformSlot = 256, UniformSize = 80;

    /// <summary>Texels of the 2x2 RGBA8 texture, row-major: red, green / blue, yellow.</summary>
    public static readonly uint[] Texels = [0xFF0000FF, 0xFF00FF00, 0xFFFF0000, 0xFF00FFFF];
    public static readonly float4 FrontPushTint = new(1f, 0.5f, 1f, 1f), BackPushTint = new(0.25f, 1f, 1f, 1f);
    public static readonly float4 FrontUboTint = new(1f, 1f, 1f, 1f), BackUboTint = new(1f, 0.5f, 1f, 1f);

    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex(float3 pos, float2 uv)
    {
        public float3 Pos = pos;
        public float2 Uv = uv;
    }

    /// <summary>One triangle in Vulkan clip space (y down), uv chosen so the screen centre samples texel (1,1) away from texel edges.</summary>
    public static readonly Vertex[] Vertices =
    [
        new(new float3(0f, -0.8f, 0f), new float2(0.75f, 0.25f)),
        new(new float3(-0.8f, 0.8f, 0f), new float2(0.25f, 1.25f)),
        new(new float3(0.8f, 0.8f, 0f), new float2(1.25f, 1.25f)),
    ];

    private readonly Instance _instance;
    private readonly VkDebugUtilsMessengerEXT _messenger;
    private readonly VkSurfaceKHR _surface;
    private readonly PhysicalDevice _physicalDevice;
    private readonly DeviceEx _device;
    private readonly Queue _queue;
    private readonly int _family;

    private VkSwapchainKHR _swapchain;
    private VkFormat _colorFormat;
    private VkExtent2D _extent;
    private VkImage[] _swapImages = [];
    private VkImageView[] _swapViews = [];
    private VkSemaphore[] _renderDone = [];
    private ImageEx _depth;
    private ImageViewEx _depthView;

    private readonly BufferEx _vertexBuffer;
    private readonly BufferEx _uniformBuffer;
    private readonly MappedMemory _uniformMap;
    private readonly ImageEx _texture;
    private readonly ImageViewEx _textureView;
    private readonly VkSampler _sampler;
    private readonly DescriptorSetLayoutEx _setLayout;
    private readonly DescriptorPoolEx _pool;
    private readonly VkDescriptorSet _set;
    private readonly VkPipelineLayout _pipelineLayout;
    private readonly VkPipeline _pipeline;
    private readonly VkCommandPool _commandPool;
    private readonly SyncObjects[] _sync = new SyncObjects[FramesInFlight];

    public int FramesRendered { get; private set; }

    /// <summary>When set, the frame with this index also copies the centre pixel of the swapchain image into host-visible memory.</summary>
    public int ReadbackAtFrame { get; set; } = -1;
    /// <summary>The read-back centre pixel as stored in the swapchain format (B, G, R, A for B8G8R8A8); null until the data landed.</summary>
    public byte[]? ReadbackPixel { get; private set; }
    /// <summary>Frames between the readback submit and the fence signalling.</summary>
    public int ReadbackLatency { get; private set; }
    public VkFormat ColorFormat => _colorFormat;
    public DeviceEx Device => _device;
    public Queue Queue => _queue;
    private BufferEx? _readbackBuffer;
    private VkFence _readbackFence;
    private bool _readbackPending;
    public int SwapchainRecreations { get; private set; }
    public static int MessengerErrors;
    /// <summary>
    /// Extra draws of the front triangle per frame, each after its own push-constant update (same tint, so the picture does not
    /// change). More than 4096 in one submit makes the web ICD grow its push-constant ring.
    /// </summary>
    public int ExtraFrontDraws { get; set; }

    public TriangleScene(ReadOnlySpan<byte> vertSpv, ReadOnlySpan<byte> fragSpv)
    {
        // ---- instance (+ debug messenger) ----------------------------------------------------------------------------
        var extensions = new HashSet<string> { "VK_KHR_surface", "VK_BRUTAL_web_surface", "VK_EXT_debug_utils" };
        VulkanHelpers.AddPlatformRequiredExtensions(extensions);
        _instance = Vulkan.CreateInstance(new Instance.CreateInfo
        {
            AppInfo = new VkApplicationInfo { ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3 },
            Flags = VulkanHelpers.GetPlatformRequiredInstanceCreateFlags(),
            EnabledExtensions = extensions,
            EnabledLayers = new HashSet<string>(),
        });
        _messenger = _instance.CreateDebugUtilsMessengerEXT(new VkDebugUtilsMessengerCreateInfoEXT
        {
            MessageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.WarningBitEXT | VkDebugUtilsMessageSeverityFlagsEXT.ErrorBitEXT,
            MessageType = VkDebugUtilsMessageTypeFlagsEXT.GeneralBitEXT | VkDebugUtilsMessageTypeFlagsEXT.ValidationBitEXT,
            PfnUserCallback = new PfnVkDebugUtilsMessengerCallbackEXT(&OnDebugMessage),
        }, null);

        // ---- surface (the Brutal.Browser hook installed by WebIcd.Install) ---------------------------------------------
        if (BrowserSurfaces.CreateSurface is not { } createSurface || createSurface(_instance.VkHandle, 1, out var rawSurface) != 0)
            throw new InvalidOperationException("surface creation failed (WebIcd.Install not called?)");
        _surface = new VkSurfaceKHR((nint)rawSurface);

        // ---- physical device, device, queue ---------------------------------------------------------------------------
        _physicalDevice = _instance.PhysicalDevices.First(p => p.SupportsExtension(VK.KHR_SWAPCHAIN_EXTENSION_NAME));
        _family = _physicalDevice.FindQueueFamily(VkQueueFlags.GraphicsBit);
        if (!_physicalDevice.GetSurfaceSupportKHR(_family, _surface))
            throw new InvalidOperationException("no present support");
        Console.WriteLine($"GPU: {_physicalDevice.GetDeviceName()}");

        var deviceExtensions = new HashSet<string> { VK.KHR_SWAPCHAIN_EXTENSION_NAME };
        VulkanHelpers.AddMandatoryIfSupportedDeviceExtensions(deviceExtensions, _physicalDevice);
        var features13 = new VkPhysicalDeviceVulkan13Features { DynamicRendering = true, Synchronization2 = true };
        var priority = 1f;
        var queueInfo = new VkDeviceQueueCreateInfo { QueueFamilyIndex = _family, QueueCount = 1, QueuePriorities = &priority };
        _device = _physicalDevice.CreateDevice(new DeviceEx.CreateInfo
        {
            Extensions = deviceExtensions,
            QueueCreateInfos = new ReadOnlySpan<VkDeviceQueueCreateInfo>(&queueInfo, 1),
            Next = &features13,
        });
        _queue = _device.GetQueue(_family, 0);

        CreateSwapchain();

        // ---- vertex buffer + texture through a StagingPool ------------------------------------------------------------
        _vertexBuffer = _device.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "triangle.vb", BufferSize = ByteSize.Of<Vertex>(Vertices.Length),
            BufferUsage = VkBufferUsageFlags.VertexBufferBit | VkBufferUsageFlags.TransferDstBit,
            AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        _texture = _device.CreateImage(new ImageEx.CreateInfo
        {
            Name = "texture", ImageType = VkImageType._2D, ImageFormat = VkFormat.R8G8B8A8UNorm, ImageExtent = new VkExtent3D(2, 2, 1),
            ImageMipLevels = 1, ImageArrayLayers = 1, ImageSamples = VkSampleCountFlags._1Bit, ImageTiling = VkImageTiling.Optimal,
            ImageUsage = VkImageUsageFlags.TransferDstBit | VkImageUsageFlags.SampledBit, ImageSharingMode = VkSharingMode.Exclusive,
            AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        _textureView = _texture.CreateImageView(VkImageViewType._2D, Subresources.Full(VkImageAspectFlags.ColorBit), null);
        using (var staging = _device.CreateStagingPool(_queue, 1))
        {
            var stagedVertices = staging.AddStagingBuffer<Vertex>(Vertices.AsSpan());
            var stagedTexels = staging.AddStagingBuffer<uint>(Texels.AsSpan());
            var upload = staging.NextCommandBuffer();
            upload.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
            upload.CopyBuffer(stagedVertices.VkBuffer, _vertexBuffer.VkBuffer, _vertexBuffer.BufferSize);
            upload.PipelineBarrier(VkPipelineStageFlags.TopOfPipeBit, VkPipelineStageFlags.TransferBit, VkDependencyFlags.None, [], [],
                [ImageBarrier(_texture.VkImage, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal)]);
            upload.CopyBufferToImage(stagedTexels.VkBuffer, _texture.VkImage, VkImageLayout.TransferDstOptimal,
            [
                new VkBufferImageCopy
                {
                    ImageSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 },
                    ImageExtent = new VkExtent3D(2, 2, 1),
                },
            ]);
            upload.PipelineBarrier(VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.FragmentShaderBit, VkDependencyFlags.None, [], [],
                [ImageBarrier(_texture.VkImage, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal)]);
            upload.End();
            staging.Submit().Wait(); // vkQueueSubmit + vkWaitForFences(-1): signalled at submit on the web ICD
        }
        _sampler = _device.CreateSampler(new VkSamplerCreateInfo
        {
            MagFilter = VkFilter.Nearest, MinFilter = VkFilter.Nearest, MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.ClampToEdge, AddressModeV = VkSamplerAddressMode.ClampToEdge, AddressModeW = VkSamplerAddressMode.ClampToEdge,
            MaxLod = 1f,
        }, null);

        // ---- uniforms: one host-visible, persistently mapped buffer with two 256-byte slots (dynamic offsets) ----------
        _uniformBuffer = _device.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "ubo", BufferSize = ByteSize.Of<byte>(2 * UniformSlot), BufferUsage = VkBufferUsageFlags.UniformBufferBit,
            AllocPreference = MemoryPreference.PreferCpu,
            AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,
        });
        _uniformMap = _uniformBuffer.Map();
        WriteUniforms();

        // ---- descriptors -----------------------------------------------------------------------------------------------
        Span<VkDescriptorSetLayoutBinding> bindings =
        [
            new() { Binding = 0, DescriptorType = VkDescriptorType.UniformBufferDynamic, DescriptorCount = 1, StageFlags = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit },
            new() { Binding = 1, DescriptorType = VkDescriptorType.SampledImage, DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit },
            new() { Binding = 2, DescriptorType = VkDescriptorType.Sampler, DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit },
        ];
        _setLayout = _device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = bindings }, null);
        Span<VkDescriptorPoolSize> poolSizes =
        [
            new() { Type = VkDescriptorType.UniformBufferDynamic, DescriptorCount = 1 },
            new() { Type = VkDescriptorType.SampledImage, DescriptorCount = 1 },
            new() { Type = VkDescriptorType.Sampler, DescriptorCount = 1 },
        ];
        _pool = _device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets = 1, PoolSizes = poolSizes }, null);
        _set = _device.AllocateDescriptorSet(_pool, _setLayout);
        var bufferInfo = new VkDescriptorBufferInfo { Buffer = _uniformBuffer.VkBuffer, Offset = ByteSize.Zero, Range = ByteSize.Of<byte>(UniformSize) };
        var imageInfo = new VkDescriptorImageInfo { ImageView = _textureView.VkImageView, ImageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var samplerInfo = new VkDescriptorImageInfo { Sampler = _sampler };
        _device.UpdateDescriptorSets(
        [
            new VkWriteDescriptorSet { DstSet = _set, DstBinding = 0, DescriptorCount = 1, DescriptorType = VkDescriptorType.UniformBufferDynamic, BufferInfo = &bufferInfo },
            new VkWriteDescriptorSet { DstSet = _set, DstBinding = 1, DescriptorCount = 1, DescriptorType = VkDescriptorType.SampledImage, ImageInfo = &imageInfo },
            new VkWriteDescriptorSet { DstSet = _set, DstBinding = 2, DescriptorCount = 1, DescriptorType = VkDescriptorType.Sampler, ImageInfo = &samplerInfo },
        ], []);

        // ---- shaders + pipeline (dynamic rendering, depth test, push constants) ----------------------------------------
        var vertModule = ShaderModuleExtensions.FromSpirv(_device, vertSpv, null);
        var fragModule = ShaderModuleExtensions.FromSpirv(_device, fragSpv, null);
        using (var stages = new ShaderStages(_device, [Presets.ShaderStage.Vertex(vertModule), Presets.ShaderStage.Fragment(fragModule)], null))
        {
            _pipelineLayout = _device.CreatePipelineLayout([_setLayout.Handle],
                [new VkPushConstantRange { StageFlags = VkShaderStageFlags.FragmentBit, Offset = new ByteSize32(0), Size = new ByteSize32(16) }], null);
            var vertexInput = VertexInput.For<Vertex>();
            var colorFormat = _colorFormat;
            var renderingInfo = new VkPipelineRenderingCreateInfo { ColorAttachmentCount = 1, ColorAttachmentFormats = &colorFormat, DepthAttachmentFormat = VkFormat.D32SFloat };
            var dynamicStates = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
            var dynamicState = new VkPipelineDynamicStateCreateInfo { DynamicStateCount = 2, DynamicStates = dynamicStates };
            var viewportState = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };
            _pipeline = _device.CreateGraphicsPipeline(default, new VkGraphicsPipelineCreateInfo
            {
                Next = &renderingInfo,
                StageCount = stages.Count, Stages = stages,
                VertexInputState = vertexInput,
                InputAssemblyState = Presets.InputAssembly.TriangleList,
                ViewportState = &viewportState,
                RasterizationState = Presets.Rasterization.Fill.CullBack, // front face CCW: also checks Y flip + winding
                MultisampleState = Presets.Multisample.None,
                DepthStencilState = Presets.DepthStencil.DepthTestWrite,
                ColorBlendState = Presets.BlendState.BlendNone,
                DynamicState = &dynamicState,
                Layout = _pipelineLayout,
            }, null);
        }

        // ---- per-frame command buffers and sync -----------------------------------------------------------------------
        _commandPool = _device.CreateCommandPool(new VkCommandPoolCreateInfo { QueueFamilyIndex = _family, Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit }, null);
        for (var i = 0; i < FramesInFlight; i++)
            _sync[i] = new SyncObjects(_device)
            {
                ImageReadySemaphore = _device.CreateSemaphore(new VkSemaphoreCreateInfo(), null),
                RenderingFinishedSemaphore = _device.CreateSemaphore(new VkSemaphoreCreateInfo(), null),
                Fence = _device.CreateFence(VkFenceCreateFlags.SignaledBit, null),
                CommandBuffer = _device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = _commandPool, Level = VkCommandBufferLevel.Primary }),
                CommandPool = _commandPool,
                Allocator = null,
            };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060")]
    private static Bool32 OnDebugMessage(VkDebugUtilsMessageSeverityFlagsEXT severity, VkDebugUtilsMessageTypeFlagsEXT types,
        VkDebugUtilsMessengerCallbackDataEXT* data, void* userData)
    {
        if ((severity & VkDebugUtilsMessageSeverityFlagsEXT.ErrorBitEXT) != 0)
            Interlocked.Increment(ref MessengerErrors);
        return false;
    }

    private static VkImageMemoryBarrier ImageBarrier(VkImage image, VkImageLayout from, VkImageLayout to) => new()
    {
        Image = image, OldLayout = from, NewLayout = to, SrcQueueFamilyIndex = -1, DstQueueFamilyIndex = -1,
        SubresourceRange = Subresources.Full(VkImageAspectFlags.ColorBit),
    };

    private void CreateSwapchain()
    {
        var caps = _physicalDevice.GetSurfaceCapabilitiesKHR(_surface);
        Span<VkSurfaceFormatKHR> formats = stackalloc VkSurfaceFormatKHR[_physicalDevice.GetSurfaceFormatsKHR_Count(_surface)];
        _physicalDevice.GetSurfaceFormatsKHR_Write(_surface, formats);
        var surfaceFormat = formats[0];
        foreach (var f in formats)
            if (f.Format is VkFormat.B8G8R8A8UNorm or VkFormat.R8G8B8A8UNorm)
            {
                surfaceFormat = f;
                break;
            }
        _colorFormat = surfaceFormat.Format;
        _extent = caps.CurrentExtent;
        _swapchain = _device.CreateSwapchainKHR(new VkSwapchainCreateInfoKHR
        {
            Surface = _surface,
            MinImageCount = Math.Max(caps.MinImageCount, FramesInFlight + 1),
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = _extent,
            ImageArrayLayers = 1,
            ImageUsage = VkImageUsageFlags.ColorAttachmentBit | VkImageUsageFlags.TransferSrcBit,
            ImageSharingMode = VkSharingMode.Exclusive,
            PreTransform = caps.CurrentTransform,
            CompositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueBitKHR,
            PresentMode = VkPresentModeKHR.FifoKHR,
            Clipped = true,
        }, null);
        _swapImages = new VkImage[_device.GetSwapchainImagesKHR_Count(_swapchain)];
        _device.GetSwapchainImagesKHR_Write(_swapchain, _swapImages);
        _swapViews = new VkImageView[_swapImages.Length];
        _renderDone = new VkSemaphore[_swapImages.Length];
        for (var i = 0; i < _swapImages.Length; i++)
        {
            _swapViews[i] = _device.CreateImageView(new VkImageViewCreateInfo
            {
                Image = _swapImages[i], ViewType = VkImageViewType._2D, Format = _colorFormat,
                SubresourceRange = Subresources.Full(VkImageAspectFlags.ColorBit),
            }, null);
            _renderDone[i] = _device.CreateSemaphore(new VkSemaphoreCreateInfo(), null);
        }
        _depth = _device.CreateImage(new ImageEx.CreateInfo
        {
            Name = "depth", ImageType = VkImageType._2D, ImageFormat = VkFormat.D32SFloat,
            ImageExtent = new VkExtent3D(_extent.Width, _extent.Height, 1), ImageMipLevels = 1, ImageArrayLayers = 1,
            ImageSamples = VkSampleCountFlags._1Bit, ImageTiling = VkImageTiling.Optimal,
            ImageUsage = VkImageUsageFlags.DepthStencilAttachmentBit, ImageSharingMode = VkSharingMode.Exclusive,
            AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        _depthView = _depth.CreateImageView(VkImageViewType._2D, Subresources.Full(VkImageAspectFlags.DepthBit), null);
    }

    private void DestroySwapchain()
    {
        _depthView.Dispose();
        _depth.Dispose();
        foreach (var v in _swapViews)
            _device.DestroyImageView(v, null);
        foreach (var s in _renderDone)
            _device.DestroySemaphore(s, null);
        _device.DestroySwapchainKHR(_swapchain, null);
    }

    /// <summary>Slot 0: front triangle (z 0.25); slot 1: back triangle (scaled 1.2, z 0.75). Column-major mat4 + vec4 tint.</summary>
    private void WriteUniforms()
    {
        var f = _uniformMap.AsSpan<float>();
        f.Clear();
        WriteSlot(f[..20], 1f, 0.25f, FrontUboTint);
        WriteSlot(f[(UniformSlot / 4)..(UniformSlot / 4 + 20)], 1.2f, 0.75f, BackUboTint);
    }

    private static void WriteSlot(Span<float> slot, float scale, float z, float4 tint)
    {
        slot[0] = scale;  // column 0
        slot[5] = scale;  // column 1
        slot[10] = 1f;    // column 2
        slot[14] = z;     // column 3: translation z
        slot[15] = 1f;
        slot[16] = tint.X; slot[17] = tint.Y; slot[18] = tint.Z; slot[19] = tint.W;
    }

    /// <summary>Renders one frame; returns false only on an unrecoverable error.</summary>
    public bool Frame()
    {
        PollReadback();
        var s = _sync[FramesRendered % FramesInFlight];
        _device.WaitForFence(s.Fence, -1);
        int imageIndex;
        try
        {
            _device.AcquireNextImageKHR(_swapchain, -1, s.ImageReadySemaphore, default, out imageIndex);
        }
        catch (VkResultError e) when (e.Result == VkResult.ErrorOutOfDateKHR)
        {
            _device.WaitIdle();
            DestroySwapchain();
            CreateSwapchain();
            SwapchainRecreations++;
            return true;
        }
        _device.ResetFences([s.Fence]);

        var cmd = s.CommandBuffer;
        cmd.Reset();
        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        var frameLabel = cmd.DebugScope("frame");
        cmd.PipelineBarrier(VkPipelineStageFlags.TopOfPipeBit, VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.EarlyFragmentTestsBit,
            VkDependencyFlags.None, [], [], [ImageBarrier(_swapImages[imageIndex], VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal)]);
        var colorAttachment = new VkRenderingAttachmentInfo
        {
            ImageView = _swapViews[imageIndex], ImageLayout = VkImageLayout.ColorAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.Store,
            ClearValue = new VkClearColorValue { Float32 = ClearColor },
        };
        var depthAttachment = new VkRenderingAttachmentInfo
        {
            ImageView = _depthView.VkImageView, ImageLayout = VkImageLayout.DepthAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.DontCare,
            ClearValue = new VkClearDepthStencilValue { Depth = 1f },
        };
        cmd.BeginRendering(new VkRenderingInfo
        {
            RenderArea = new VkRect2D(_extent), LayerCount = 1,
            ColorAttachmentCount = 1, ColorAttachments = &colorAttachment, DepthAttachment = &depthAttachment,
        });
        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        cmd.SetViewport(0, [new VkViewport { Width = _extent.Width, Height = _extent.Height, MaxDepth = 1f }]);
        cmd.SetScissor(0, [new VkRect2D(_extent)]);
        cmd.BindVertexBuffer(0, _vertexBuffer);
        using (cmd.DebugScope("triangles"))
        {
            // front triangle first, then the back one: the depth test must keep the front one where they overlap
            cmd.PushConstants(_pipelineLayout, VkShaderStageFlags.FragmentBit, ByteSize.Zero, FrontPushTint);
            cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, _pipelineLayout, 0, [_set], [new ByteSize32(0)]);
            cmd.Draw(3, 1, 0, 0);
            for (var i = 0; i < ExtraFrontDraws; i++)
            {
                cmd.PushConstants(_pipelineLayout, VkShaderStageFlags.FragmentBit, ByteSize.Zero, FrontPushTint);
                cmd.Draw(3, 1, 0, 0);
            }
            cmd.PushConstants(_pipelineLayout, VkShaderStageFlags.FragmentBit, ByteSize.Zero, BackPushTint);
            cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, _pipelineLayout, 0, [_set], [new ByteSize32(UniformSlot)]);
            cmd.Draw(3, 1, 0, 0);
        }
        cmd.EndRendering();
        cmd.PipelineBarrier(VkPipelineStageFlags.ColorAttachmentOutputBit, VkPipelineStageFlags.BottomOfPipeBit, VkDependencyFlags.None, [], [],
            [ImageBarrier(_swapImages[imageIndex], VkImageLayout.ColorAttachmentOptimal, VkImageLayout.PresentSrcKHR)]);
        frameLabel.Dispose();
        cmd.End();

        _queue.Submit([s.ImageReadySemaphore], [VkPipelineStageFlags.ColorAttachmentOutputBit], [cmd], [_renderDone[imageIndex]], s.Fence);
        if (FramesRendered == ReadbackAtFrame)
            SubmitReadback(imageIndex);
        var waitSemaphore = _renderDone[imageIndex];
        var swapchain = _swapchain;
        var presentIndex = imageIndex;
        _queue.PresentKHR(new VkPresentInfoKHR
        {
            WaitSemaphoreCount = 1, WaitSemaphores = &waitSemaphore,
            SwapchainCount = 1, Swapchains = &swapchain, ImageIndices = &presentIndex,
        });
        FramesRendered++;
        return true;
    }

    /// <summary>
    /// GPU→CPU: copy the centre pixel of the swapchain image into a host-visible buffer with its own fence. On the web ICD the
    /// data lands on a later frame (mapAsync), so the fence is polled with a zero timeout at the start of each frame.
    /// </summary>
    private void SubmitReadback(int imageIndex)
    {
        _readbackBuffer ??= _device.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "readback", BufferSize = ByteSize.Of<byte>(256), BufferUsage = VkBufferUsageFlags.TransferDstBit,
            AllocPreference = MemoryPreference.PreferCpu,
            AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,
        });
        if (_readbackFence.IsNull())
            _readbackFence = _device.CreateFence(VkFenceCreateFlags.None, null);
        var cmd = _device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = _commandPool, Level = VkCommandBufferLevel.Primary });
        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        cmd.CopyImageToBuffer(_swapImages[imageIndex], VkImageLayout.TransferSrcOptimal, _readbackBuffer.Value.VkBuffer,
        [
            new VkBufferImageCopy
            {
                ImageSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageOffset = new VkOffset3D(_extent.Width / 2, _extent.Height / 2, 0),
                ImageExtent = new VkExtent3D(1, 1, 1),
            },
        ]);
        cmd.End();
        _queue.Submit([], [], [cmd], [], _readbackFence);
        _readbackPending = true;
        _readbackSubmittedAt = FramesRendered;
    }

    private int _readbackSubmittedAt;

    private void PollReadback()
    {
        if (!_readbackPending || _device.WaitForFence(_readbackFence, 0) != VkResult.Success)
            return;
        _readbackPending = false;
        ReadbackLatency = FramesRendered - _readbackSubmittedAt;
        using var map = _readbackBuffer!.Value.Map();
        ReadbackPixel = map.AsSpan<byte>()[..4].ToArray();
    }

    public void Dispose()
    {
        _device.WaitIdle();
        if (!_readbackFence.IsNull())
            _device.DestroyFence(_readbackFence, null);
        _readbackBuffer?.Dispose();
        foreach (var s in _sync)
            s.Dispose();
        _device.DestroyCommandPool(_commandPool, null);
        _device.DestroyPipeline(_pipeline, null);
        _device.DestroyPipelineLayout(_pipelineLayout, null);
        _pool.Dispose();
        _setLayout.Dispose();
        _device.DestroySampler(_sampler, null);
        _uniformMap.Dispose();
        _uniformBuffer.Dispose();
        _textureView.Dispose();
        _texture.Dispose();
        _vertexBuffer.Dispose();
        DestroySwapchain();
        _device.Dispose();
        _instance.DestroySurfaceKHR(_surface, null);
        _instance.DestroyDebugUtilsMessengerEXT(_messenger, null);
        _instance.Dispose();
    }
}
