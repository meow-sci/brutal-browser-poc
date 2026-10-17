using Brutal.GlfwApi;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using HelloMesh.Assets;
using HelloMesh.Input;
using HelloMesh.Rendering;

namespace HelloMesh.Scene;

/// <summary>
/// The sample's game content: the Khronos "Sunglasses" model lit by one directional light, an orbit camera driven by mouse and
/// keyboard, and a key that plays the sound. A real game puts its state into <see cref="Update"/> and its draws into
/// <see cref="Record"/>; the <see cref="RenderHost"/> owns everything around them.
/// </summary>
public sealed class SunglassesScene : IFrameRecorder, IDisposable
{
    private const string ModelResource = "Assets/SunglassesKhronos.glb";
    private static readonly float3 LightDirection = new(0.4f, 0.8f, 0.6f);
    private static readonly float4 ClearColor = new(0.10f, 0.20f, 0.30f, 1f);

    private readonly MeshRenderer _renderer;
    private readonly OrbitCamera _camera;
    private readonly InputController _input;

    /// <summary>Loads the model, uploads it and builds the pipeline for the host's swapchain format; maps the window's input.</summary>
    /// <param name="playSound">Called when the player presses Space.</param>
    public SunglassesScene(RenderHost host, GlfwWindow window, Action playSound)
    {
        var model = ModelLoader.Load(EmbeddedAssets.Read(ModelResource));
        _renderer = new MeshRenderer(host.Context, model, host.ColorFormat);
        var (min, max) = model.Bounds;
        _camera = new OrbitCamera((min + max) * 0.5f, (max - min).Length() * 0.5f);
        _input = new InputController(window, _camera, playSound);
    }

    /// <summary>
    /// Advances the scene by <paramref name="dt"/> seconds; call once per frame after <c>Glfw.PollEvents()</c>. The input events of
    /// that poll have already moved the camera; this reports the change (<c>CAMERA</c>). The scene has no animation, so
    /// <paramref name="dt"/> is unused.
    /// </summary>
    public void Update(double dt) => _input.ReportCamera();

    /// <summary>One dynamic-rendering pass into the swapchain image: clear, then the model.</summary>
    public unsafe void Record(CommandBuffer cmd, in RenderTarget target)
    {
        var colorAttachment = new VkRenderingAttachmentInfo
        {
            ImageView = target.ColorView, ImageLayout = VkImageLayout.ColorAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.Store,
            ClearValue = new VkClearColorValue { Float32 = ClearColor },
        };
        var depthAttachment = new VkRenderingAttachmentInfo
        {
            ImageView = target.DepthView, ImageLayout = VkImageLayout.DepthAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.Store,
            ClearValue = new VkClearDepthStencilValue { Depth = 1f },
        };
        var extent = target.Extent;
        cmd.BeginRendering(new VkRenderingInfo
        {
            RenderArea = new VkRect2D(extent), LayerCount = 1,
            ColorAttachmentCount = 1, ColorAttachments = &colorAttachment, DepthAttachment = &depthAttachment,
        });
        var projection = _camera.GetProjection(extent.Width / (float)extent.Height);
        _renderer.Draw(cmd, target.FrameIndex, extent, _camera.GetView(), projection, LightDirection);
        cmd.EndRendering();
    }

    /// <summary>Destroys the GPU resources; the frames that use them must have finished (<see cref="RenderHost.WaitIdle"/>).</summary>
    public void Dispose() => _renderer.Dispose();
}
