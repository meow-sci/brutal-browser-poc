namespace Brutal.Vulkan.WebGpu;

/// <summary>WebGPU flag values (WebGPU spec; not the Vulkan bit values).</summary>
internal static class GpuBufferUsage
{
    public const int MapRead = 0x1, MapWrite = 0x2, CopySrc = 0x4, CopyDst = 0x8, Index = 0x10, Vertex = 0x20,
        Uniform = 0x40, Storage = 0x80, Indirect = 0x100, QueryResolve = 0x200;
}

internal static class GpuTextureUsage
{
    public const int CopySrc = 0x1, CopyDst = 0x2, TextureBinding = 0x4, StorageBinding = 0x8, RenderAttachment = 0x10;
}

internal static class GpuShaderStage
{
    public const int Vertex = 0x1, Fragment = 0x2, Compute = 0x4;
}

/// <summary>
/// Ids that <c>webgpu-glue.js</c> reserves in its handle table at <c>init</c>. They are resolved during replay, so command
/// streams can refer to the canvas texture of the frame being replayed. Must match <c>RESERVED</c> in webgpu-glue.js.
/// </summary>
internal static class ReservedHandles
{
    /// <summary>View of the current canvas texture in the configured (linear) format.</summary>
    public const int CanvasView = 1;
    /// <summary>View of the current canvas texture in the sRGB view format.</summary>
    public const int CanvasViewSrgb = 2;
    /// <summary>The current canvas texture itself (copy source/destination).</summary>
    public const int CanvasTexture = 3;
    /// <summary>A bind group layout with no entries.</summary>
    public const int EmptyBindGroupLayout = 4;
    /// <summary>A bind group of <see cref="EmptyBindGroupLayout"/>, bound to pipeline-layout groups the shaders do not use.</summary>
    public const int EmptyBindGroup = 5;
    /// <summary>Push-constant group layout: binding 0 = uniform buffer with dynamic offset (all stages).</summary>
    public const int PushUniformLayout = 6;
    /// <summary>Push-constant group layout: binding 0 = read-only storage buffer with dynamic offset (all stages).</summary>
    public const int PushStorageLayout = 7;
    /// <summary>First id handed out for created objects.</summary>
    public const int FirstDynamic = 16;
}

/// <summary>
/// Status values of the bridge (must match <c>STATUS</c> in webgpu-glue.js). <c>replayCommandStream</c> returns the number of WebGPU
/// errors waiting in <c>pollErrors</c> in its low 16 bits plus the flags below; <c>acquireCanvasTexture</c> and <c>present</c> return
/// <see cref="CanvasDeviceLost"/> instead of the packed canvas size once the device is lost.
/// </summary>
internal static class BridgeStatus
{
    /// <summary>Mask of the error count in the replay result.</summary>
    public const int ReplayErrorCountMask = 0xFFFF;
    /// <summary>The <c>GPUDevice</c> was lost (<c>device.lost</c> resolved); the stream was not replayed.</summary>
    public const int ReplayDeviceLost = 1 << 16;
    /// <summary>The push-constant ring could not grow to hold this submit's updates (beyond <c>maxBufferSize</c>); the submit was dropped.</summary>
    public const int ReplayOutOfMemory = 1 << 17;
    /// <summary>Returned by <c>acquireCanvasTexture</c> / <c>present</c> after device loss.</summary>
    public const int CanvasDeviceLost = -1;
}
