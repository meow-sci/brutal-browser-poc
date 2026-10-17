using Brutal.GlfwApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using FrameResources = HelloMesh.Rendering.FrameResources;

namespace HelloMesh.Rendering;

/// <summary>The swapchain image of the current frame and its depth buffer, as handed to <see cref="IFrameRecorder.Record"/>.</summary>
/// <param name="FrameIndex">Frame-in-flight slot (0 .. <see cref="FrameResources.FramesInFlight"/> - 1): index per-frame buffers with it.</param>
public readonly record struct RenderTarget(
    int FrameIndex, VkExtent2D Extent, VkFormat ColorFormat, VkImage ColorImage, VkImageView ColorView, VkImage DepthImage, VkImageView DepthView);

/// <summary>What a game scene implements: record the commands of one frame.</summary>
public interface IFrameRecorder
{
    /// <summary>
    /// Records the frame into <paramref name="cmd"/> (already begun). The colour image arrives in COLOR_ATTACHMENT_OPTIMAL and the
    /// depth image in DEPTH_ATTACHMENT_OPTIMAL (contents undefined); leave the colour image in COLOR_ATTACHMENT_OPTIMAL.
    /// </summary>
    void Record(CommandBuffer cmd, in RenderTarget target);
}

/// <summary>Optional step between the scene and the present, e.g. a desktop screenshot (see <see cref="ScreenshotCapture"/>).</summary>
public interface IFrameCapture : IDisposable
{
    /// <summary>Records after the scene; the colour image is in COLOR_ATTACHMENT_OPTIMAL and must end in PRESENT_SRC_KHR.</summary>
    void Record(CommandBuffer cmd, in RenderTarget target);

    /// <summary>Called once the frame's fence has signalled (the copy is complete).</summary>
    void Complete();
}

/// <summary>
/// The reusable part of a BRUTAL game's renderer, identical on desktop and in the browser: window surface, instance, device and
/// queue (<see cref="VulkanContext"/>), swapchain with depth buffer, <see cref="FrameResources.FramesInFlight"/> frames in flight,
/// and the per-frame acquire → record → submit → present sequence with swapchain recreation on resize or OUT_OF_DATE.
/// A frame acquires once, presents once and returns (the browser host requires it); nothing in here blocks on the GPU in the browser.
/// </summary>
public sealed class RenderHost : IDisposable
{
    private const int WaitForever = -1;

    private readonly GlfwWindow _window;
    private readonly Swapchain _swapchain;
    private readonly FrameResources _frames;
    private int _frameIndex;
    private bool _recreateSwapchain;

    /// <summary>Creates the Vulkan objects for <paramref name="window"/>.</summary>
    public RenderHost(GlfwWindow window)
    {
        _window = window;
        Context = new VulkanContext(window);
        _swapchain = new Swapchain(Context, window);
        _frames = new FrameResources(Context);
        window.OnFramebufferSize += (_, _) => _recreateSwapchain = true;
    }

    /// <summary>Instance, device, queue and device limits.</summary>
    public VulkanContext Context { get; }

    /// <summary>Format of the swapchain images (the colour attachment format of pipelines that draw to the screen).</summary>
    public VkFormat ColorFormat => _swapchain.Format;

    /// <summary>Format of the depth buffer.</summary>
    public static VkFormat DepthFormat => Swapchain.DepthFormat;

    /// <summary>True when the swapchain images can be copied out (<see cref="Capture"/>); false in the browser.</summary>
    public bool SupportsCapture => _swapchain.SupportsReadback;

    /// <summary>Total number of frames presented.</summary>
    public long FramesRendered { get; private set; }

    /// <summary>Raised after each presented frame with <see cref="FramesRendered"/>.</summary>
    public event Action<long>? FrameRendered;

    /// <summary>A capture recorded into the next rendered frame only; cleared when it completes.</summary>
    public IFrameCapture? Capture { get; set; }

    /// <summary>
    /// Renders one frame with <paramref name="scene"/>: waits for this frame slot's fence, recreates the swapchain if needed, acquires,
    /// records, submits and presents. Returns false when nothing was rendered (zero-sized window, or the swapchain was out of date).
    /// </summary>
    public bool RenderFrame(IFrameRecorder scene)
    {
        var framebuffer = _window.FramebufferSize;
        if (framebuffer.X == 0 || framebuffer.Y == 0)
            return false;

        var device = Context.Device;
        var frame = _frames[_frameIndex];
        device.WaitForFence(frame.Fence, WaitForever);
        if (_recreateSwapchain)
        {
            _frames.WaitAll();
            _swapchain.Recreate();
            _recreateSwapchain = false;
        }
        if (!TryAcquire(frame, out var imageIndex))
            return false;

        device.ResetFences([frame.Fence]);
        var capture = Capture;
        Record(scene, capture, frame, imageIndex);
        Context.Queue.Submit([frame.ImageAvailable], [VkPipelineStageFlags.ColorAttachmentOutputBit], [frame.CommandBuffer], [_swapchain.RenderFinished[imageIndex]], frame.Fence);
        Present(imageIndex);
        if (capture is not null)
        {
            device.WaitForFence(frame.Fence, WaitForever);
            capture.Complete();
            Capture = null;
        }

        _frameIndex = (_frameIndex + 1) % FrameResources.FramesInFlight;
        FrameRendered?.Invoke(++FramesRendered);
        return true;
    }

    private void Record(IFrameRecorder scene, IFrameCapture? capture, FrameResources.Frame frame, int imageIndex)
    {
        var cmd = frame.CommandBuffer;
        var colorImage = _swapchain.Images[imageIndex];
        cmd.Reset();
        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        Barriers.Image(cmd, colorImage, VkImageAspectFlags.ColorBit, VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal,
            VkPipelineStageFlags2.ColorAttachmentOutputBit, VkAccessFlags2.None, VkPipelineStageFlags2.ColorAttachmentOutputBit, VkAccessFlags2.ColorAttachmentWriteBit);
        Barriers.Image(cmd, _swapchain.DepthImage, VkImageAspectFlags.DepthBit, VkImageLayout.Undefined, VkImageLayout.DepthAttachmentOptimal,
            VkPipelineStageFlags2.EarlyFragmentTestsBit | VkPipelineStageFlags2.LateFragmentTestsBit, VkAccessFlags2.DepthStencilAttachmentWriteBit,
            VkPipelineStageFlags2.EarlyFragmentTestsBit | VkPipelineStageFlags2.LateFragmentTestsBit, VkAccessFlags2.DepthStencilAttachmentReadBit | VkAccessFlags2.DepthStencilAttachmentWriteBit);

        var target = new RenderTarget(_frameIndex, _swapchain.Extent, _swapchain.Format, colorImage, _swapchain.ImageViews[imageIndex],
            _swapchain.DepthImage, _swapchain.DepthView);
        scene.Record(cmd, target);

        if (capture is not null)
            capture.Record(cmd, target);
        else
            Barriers.Image(cmd, colorImage, VkImageAspectFlags.ColorBit, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.PresentSrcKHR,
                VkPipelineStageFlags2.ColorAttachmentOutputBit, VkAccessFlags2.ColorAttachmentWriteBit, VkPipelineStageFlags2.None, VkAccessFlags2.None);
        cmd.End();
    }

    private bool TryAcquire(FrameResources.Frame frame, out int imageIndex)
    {
        try
        {
            var result = Context.Device.AcquireNextImageKHR(_swapchain.Handle, WaitForever, frame.ImageAvailable, default, out imageIndex);
            _recreateSwapchain |= result == VkResult.SuboptimalKHR && SizeChanged();
            return true;
        }
        catch (VkResultError error) when (error.Result == VkResult.ErrorOutOfDateKHR)
        {
            _recreateSwapchain = true;
            imageIndex = 0;
            return false;
        }
    }

    private unsafe void Present(int imageIndex)
    {
        var wait = _swapchain.RenderFinished[imageIndex];
        var swapchain = _swapchain.Handle;
        var info = new VkPresentInfoKHR
        {
            WaitSemaphoreCount = 1, WaitSemaphores = &wait,
            SwapchainCount = 1, Swapchains = &swapchain, ImageIndices = &imageIndex,
        };
        try
        {
            _recreateSwapchain |= Context.Queue.PresentKHR(info) == VkResult.SuboptimalKHR && SizeChanged();
        }
        catch (VkResultError error) when (error.Result == VkResult.ErrorOutOfDateKHR)
        {
            _recreateSwapchain = true;
        }
    }

    private bool SizeChanged() =>
        _window.FramebufferSize.X != _swapchain.Extent.Width || _window.FramebufferSize.Y != _swapchain.Extent.Height;

    /// <summary>
    /// Waits until the GPU has finished every frame in flight (frame fences, then <c>vkDeviceWaitIdle</c>). Call it before destroying
    /// resources the frames use. In the browser both return at once (the web ICD has no blocking waits; WebGPU keeps resources alive).
    /// </summary>
    public void WaitIdle()
    {
        _frames.WaitAll();
        Context.Device.WaitIdle();
    }

    /// <summary>Waits for the frames in flight (<see cref="WaitIdle"/>), then destroys everything it created.</summary>
    public void Dispose()
    {
        WaitIdle();
        Capture?.Dispose();
        _frames.Dispose();
        _swapchain.Dispose();
        Context.Dispose();
    }
}
