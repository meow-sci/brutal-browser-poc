using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Brutal.Vulkan.WebGpu;

/// <summary>[JSImport] declarations of <c>wwwroot/webgpu-glue.js</c> (module name <c>webgpu</c>, docs/architecture.md section 4).</summary>
[SupportedOSPlatform("browser")]
internal static partial class WebGpuImports
{
    private const string Module = "webgpu";

    [JSImport("getAdapterInfoJson", Module)] internal static partial string GetAdapterInfoJson();
    [JSImport("getLimitsJson", Module)] internal static partial string GetLimitsJson();
    [JSImport("getFeaturesJson", Module)] internal static partial string GetFeaturesJson();
    [JSImport("getPreferredCanvasFormat", Module)] internal static partial string GetPreferredCanvasFormat();
    [JSImport("getCanvasSize", Module)] internal static partial int GetCanvasSize();
    [JSImport("configureCanvas", Module)] internal static partial void ConfigureCanvas(string format, int usage, string viewFormatsJson, string alphaMode);
    [JSImport("acquireCanvasTexture", Module)] internal static partial int AcquireCanvasTexture();
    [JSImport("present", Module)] internal static partial int Present();

    [JSImport("createBuffer", Module)] internal static partial int CreateBuffer(double size, int usage, string label);
    [JSImport("createTexture", Module)] internal static partial int CreateTexture(string json);
    [JSImport("createTextureView", Module)] internal static partial int CreateTextureView(int texture, string json);
    [JSImport("createSampler", Module)] internal static partial int CreateSampler(string json);
    [JSImport("createShaderModule", Module)] internal static partial int CreateShaderModule(string wgsl, string label);
    [JSImport("createBindGroupLayout", Module)] internal static partial int CreateBindGroupLayout(string json);
    [JSImport("createPipelineLayout", Module)] internal static partial int CreatePipelineLayout(string json);
    [JSImport("createBindGroup", Module)] internal static partial int CreateBindGroup(string json);
    [JSImport("createRenderPipeline", Module)] internal static partial int CreateRenderPipeline(string json);
    [JSImport("createComputePipeline", Module)] internal static partial int CreateComputePipeline(string json);
    [JSImport("setLabel", Module)] internal static partial void SetLabel(int handle, string label);
    [JSImport("release", Module)] internal static partial void Release(int handle);

    [JSImport("writeBuffer", Module)]
    internal static partial void WriteBuffer(int buffer, double offset, [JSMarshalAs<JSType.Number>] nint ptr, int size);

    [JSImport("replayCommandStream", Module)]
    internal static partial int ReplayCommandStream([JSMarshalAs<JSType.Number>] nint ptr, int length);

    [JSImport("submitEmpty", Module)] internal static partial void SubmitEmpty();
    [JSImport("pollErrors", Module)] internal static partial string PollErrors();
    [JSImport("setDebugMode", Module)] internal static partial void SetDebugMode(bool errorScopes);
}
