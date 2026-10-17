using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace HelloMesh.Rendering;

/// <summary>Per-frame-in-flight command buffer, fence and image-available semaphore.</summary>
public sealed class FrameResources : IDisposable
{
    public const int FramesInFlight = 2;

    public readonly record struct Frame(CommandBuffer CommandBuffer, VkFence Fence, VkSemaphore ImageAvailable);

    private readonly Device _device;
    private readonly VkCommandPool _pool;
    private readonly Frame[] _frames = new Frame[FramesInFlight];

    public FrameResources(VulkanContext context)
    {
        _device = context.Device;
        _pool = _device.CreateCommandPool(new VkCommandPoolCreateInfo
        {
            QueueFamilyIndex = context.QueueFamily,
            Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit,
        }, null);
        for (var i = 0; i < FramesInFlight; i++)
            _frames[i] = new Frame(
                _device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = _pool, Level = VkCommandBufferLevel.Primary }),
                _device.CreateFence(VkFenceCreateFlags.SignaledBit, null),
                _device.CreateSemaphore(new VkSemaphoreCreateInfo(), null));
    }

    public Frame this[int index] => _frames[index];

    /// <summary>Blocks until every frame in flight has finished on the GPU (waits on the frame fences only).</summary>
    public void WaitAll()
    {
        foreach (var frame in _frames)
            _device.WaitForFence(frame.Fence, -1);
    }

    public void Dispose()
    {
        foreach (var frame in _frames)
        {
            _device.DestroyFence(frame.Fence, null);
            _device.DestroySemaphore(frame.ImageAvailable, null);
        }
        _device.DestroyCommandPool(_pool, null);
    }
}
