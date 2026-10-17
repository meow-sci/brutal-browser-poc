using System;
using System.Runtime.InteropServices.JavaScript;

namespace WebGpuTriangle;

/// <summary>Texture format enum shared with webgpu-glue.js (FORMATS array index).</summary>
public enum TextureFormat { Undefined = 0, Bgra8Unorm = 1, Rgba8Unorm = 2, Rgba16Float = 3, Bgra8UnormSrgb = 4, Rgba8UnormSrgb = 5 }

/// <summary>GPUBufferUsage flag values (WebGPU spec; NOT the same bits as VkBufferUsageFlags).</summary>
[Flags]
public enum BufferUsage
{
    MapRead = 0x1, MapWrite = 0x2, CopySrc = 0x4, CopyDst = 0x8, Index = 0x10,
    Vertex = 0x20, Uniform = 0x40, Storage = 0x80, Indirect = 0x100, QueryResolve = 0x200,
}

[Flags]
public enum ShaderStage { Vertex = 0x1, Fragment = 0x2, Compute = 0x4 }

public enum LoadOp { Clear = 0, Load = 1 }
public enum StoreOp { Store = 0, Discard = 1 }

/// <summary>
/// [JSImport] declarations for the "webgpu" JS module (webgpu-glue.js, registered via
/// setModuleImports('webgpu', ...)). All WebGPU objects are integer handles into a JS-side table.
/// Pointers are passed as nint (JS number); bulk data is read by JS from the wasm heap.
/// </summary>
public static partial class WebGpu
{
    const string M = "webgpu";

    // ---- benchmark probes
    [JSImport("noop", M)] public static partial int Noop(int i);
    [JSImport("noop5", M)] public static partial int Noop5(int a, int b, int c, int d, int e);
    [JSImport("noopObj", M)] public static partial int NoopObj(JSObject o);
    [JSImport("getDeviceObject", M)] public static partial JSObject GetDeviceObject();
    [JSImport("newObject", M)] public static partial JSObject NewObject();

    // ---- device / queue / canvas
    [JSImport("getDevice", M)] public static partial int GetDevice();
    [JSImport("getQueue", M)] public static partial int GetQueue();
    [JSImport("getPreferredFormat", M)] public static partial int GetPreferredFormat();
    [JSImport("getCanvasWidth", M)] public static partial int GetCanvasWidth();
    [JSImport("getCanvasHeight", M)] public static partial int GetCanvasHeight();

    // ---- buffers
    [JSImport("createBuffer", M)]
    public static partial int CreateBuffer(double size, int usage, bool mappedAtCreation);

    [JSImport("copyToMappedRange", M)]
    public static partial void CopyToMappedRange(int buffer, double offset, [JSMarshalAs<JSType.Number>] nint ptr, int byteLength);

    [JSImport("unmapBuffer", M)] public static partial void UnmapBuffer(int buffer);

    [JSImport("writeBuffer", M)]
    public static partial void WriteBuffer(int buffer, double bufferOffset, [JSMarshalAs<JSType.Number>] nint ptr, int byteLength);

    [JSImport("writeBufferSpan", M)]
    public static partial void WriteBufferSpan(int buffer, double bufferOffset, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    // ---- shaders / layouts / pipelines
    [JSImport("createShaderModule", M)] public static partial int CreateShaderModule(string wgsl);

    [JSImport("createBindGroupLayoutUniform", M)]
    public static partial int CreateBindGroupLayoutUniform(int visibility, bool hasDynamicOffset, double minBindingSize);

    [JSImport("createBindGroupUniform", M)]
    public static partial int CreateBindGroupUniform(int layout, int buffer, double offset, double size);

    [JSImport("createRenderPipelineJson", M)] public static partial int CreateRenderPipelineJson(string json);

    [JSImport("createRenderPipelineJsonAsync", M)]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    public static partial System.Threading.Tasks.Task<int> CreateRenderPipelineJsonAsync(string json);

    // ---- per-frame commands (one JS call each)
    [JSImport("createCommandEncoder", M)] public static partial int CreateCommandEncoder();
    [JSImport("getCurrentTextureView", M)] public static partial int GetCurrentTextureView();

    [JSImport("beginRenderPass", M)]
    public static partial int BeginRenderPass(int encoder, int colorView, double r, double g, double b, double a, int loadOp, int storeOp);

    [JSImport("setPipeline", M)] public static partial void SetPipeline(int pass, int pipeline);
    [JSImport("setVertexBuffer", M)] public static partial void SetVertexBuffer(int pass, int slot, int buffer, double offset);
    [JSImport("setBindGroup", M)] public static partial void SetBindGroup(int pass, int index, int bindGroup);
    [JSImport("setBindGroupDyn", M)] public static partial void SetBindGroupDyn(int pass, int index, int bindGroup, int dynamicOffset);
    [JSImport("draw", M)] public static partial void Draw(int pass, int vertexCount, int instanceCount, int firstVertex, int firstInstance);
    [JSImport("endPass", M)] public static partial void EndPass(int pass);
    [JSImport("finish", M)] public static partial int Finish(int encoder);
    [JSImport("submit", M)] public static partial void Submit(int commandBuffer);
    [JSImport("release", M)] public static partial void Release(int handle);

    // ---- batched path
    [JSImport("replayCommandStream", M)]
    public static partial int ReplayCommandStream([JSMarshalAs<JSType.Number>] nint ptr, int byteLength);
}
