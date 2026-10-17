namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Everything the ICD asks of WebGPU. WebGPU objects live in a JS-side handle table and are referred to by positive integer
/// ids (0 = none); rare, structured descriptors travel as JSON with ids embedded; bulk data and command streams are read by
/// JS straight from the wasm heap (pointer + length). The browser implementation is <c>JsWebGpuBridge</c> over
/// <c>webgpu-glue.js</c>; unit tests use a recording implementation.
/// </summary>
internal abstract class WebGpuBridge
{
    // ---- adapter / device / canvas -------------------------------------------------------------------------------
    /// <summary><c>{"vendor","architecture","device","description","isFallbackAdapter"}</c> of the adapter.</summary>
    internal abstract string GetAdapterInfoJson();

    /// <summary>The device's <c>GPUSupportedLimits</c> as a JSON object.</summary>
    internal abstract string GetLimitsJson();

    /// <summary>The device's features as a JSON array of names.</summary>
    internal abstract string GetFeaturesJson();

    /// <summary><c>navigator.gpu.getPreferredCanvasFormat()</c>.</summary>
    internal abstract string GetPreferredCanvasFormat();

    /// <summary>Canvas drawing-buffer size packed as <c>width &lt;&lt; 16 | height</c>.</summary>
    internal abstract int GetCanvasSize();

    /// <summary><c>context.configure({device, format, usage, viewFormats, alphaMode})</c>; <paramref name="viewFormatsJson"/> is a JSON array.</summary>
    internal abstract void ConfigureCanvas(string format, int usage, string viewFormatsJson, string alphaMode);

    /// <summary>
    /// Caches <c>context.getCurrentTexture()</c> for this frame; returns the canvas size packed like <see cref="GetCanvasSize"/>, or
    /// <see cref="BridgeStatus.CanvasDeviceLost"/> once the device is lost.
    /// </summary>
    internal abstract int AcquireCanvasTexture();

    /// <summary>
    /// End-of-frame marker: drops the cached canvas texture and views (the browser presents when the task returns); returns the
    /// canvas size packed like <see cref="GetCanvasSize"/>, or <see cref="BridgeStatus.CanvasDeviceLost"/> once the device is lost.
    /// </summary>
    internal abstract int Present();

    // ---- object creation (each returns a new id, 0 on failure) -----------------------------------------------------
    internal abstract int CreateBuffer(double size, int usage, string label);
    internal abstract int CreateTexture(string descriptorJson);
    internal abstract int CreateTextureView(int texture, string descriptorJson);
    internal abstract int CreateSampler(string descriptorJson);
    internal abstract int CreateShaderModule(string wgsl, string label);
    internal abstract int CreateBindGroupLayout(string descriptorJson);
    internal abstract int CreatePipelineLayout(string descriptorJson);
    internal abstract int CreateBindGroup(string descriptorJson);
    internal abstract int CreateRenderPipeline(string descriptorJson);
    internal abstract int CreateComputePipeline(string descriptorJson);

    /// <summary>Sets <c>label</c> on the object (from <c>vkSetDebugUtilsObjectNameEXT</c>).</summary>
    internal abstract void SetLabel(int handle, string label);

    /// <summary>Drops the id; buffers and textures are also <c>destroy()</c>ed (work already submitted still completes).</summary>
    internal abstract void Release(int handle);

    // ---- queue -----------------------------------------------------------------------------------------------------
    /// <summary><c>queue.writeBuffer(buffer, offset, HEAPU8, ptr, size)</c>; ordered before every later submit.</summary>
    internal abstract void WriteBuffer(int buffer, double offset, nint ptr, int size);

    /// <summary>
    /// Replays a command stream (see <c>Opcodes.cs</c>) from the wasm heap into a fresh command encoder and submits it.
    /// Returns the number of WebGPU errors waiting in <see cref="PollErrors"/> plus the <see cref="BridgeStatus"/> replay flags.
    /// </summary>
    internal abstract int ReplayCommandStream(nint ptr, int length);

    /// <summary><c>queue.submit([])</c>: a submit without command buffers.</summary>
    internal abstract void SubmitEmpty();

    /// <summary>Takes the WebGPU errors collected since the last call (uncaptured errors, error scopes), newline separated.</summary>
    internal abstract string PollErrors();

    /// <summary>Enables WebGPU validation error scopes around replays.</summary>
    internal abstract void SetDebugMode(bool errorScopes);
}
