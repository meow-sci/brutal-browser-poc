using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

internal sealed class CommandPoolObject : IcdObject
{
    public override string VkTypeName => "VkCommandPool";
    public DeviceObject Device = null!;
    public readonly List<CommandBufferObject> CommandBuffers = new();
}

internal struct ReadbackRequest
{
    public BufferObject Buffer;
    public ulong Offset, Size;
}

/// <summary>A VkCommandBuffer: an unmanaged replay stream plus the recording state the encoder needs.</summary>
internal sealed class CommandBufferObject : IcdObject
{
    public override string VkTypeName => "VkCommandBuffer";
    public CommandPoolObject Pool = null!;
    public VkCommandBufferLevel Level;
    public readonly CommandStream Stream = new(2048);
    /// <summary>Host-visible buffers written by GPU copies in this command buffer (read back after submit).</summary>
    public readonly List<ReadbackRequest> Readbacks = new();
    public bool Recording;

    // recording state
    public PipelineObject? GraphicsPipeline;
    public PipelineObject? ComputePipeline;
    public uint IndexFormat;
    public bool IndexFormatSet;
    public uint EmittedStripVariant;
    public bool InRenderPass;

    public void ResetRecording()
    {
        Stream.Reset();
        Readbacks.Clear();
        GraphicsPipeline = ComputePipeline = null;
        IndexFormatSet = false;
        EmittedStripVariant = 0;
        InRenderPass = false;
    }

    public DeviceObject Device => Pool.Device;
}

internal sealed class SwapchainObject : IcdObject
{
    public override string VkTypeName => "VkSwapchainKHR";
    public SurfaceObject Surface = null!;
    public DeviceObject Device = null!;
    public VkFormat Format;
    public string CanvasFormat = "";
    public bool SrgbView;
    public int Width, Height;
    public ImageObject[] Images = [];
    public int NextIndex;
    public int CurrentIndex = -1;
    public bool Retired;
}
