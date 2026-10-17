using System.Runtime.Versioning;

namespace Brutal.Vulkan.WebGpu;

/// <summary>The browser bridge: forwards every operation to <c>webgpu-glue.js</c> through <see cref="WebGpuImports"/>.</summary>
[SupportedOSPlatform("browser")]
internal sealed class JsWebGpuBridge : WebGpuBridge
{
    internal override string GetAdapterInfoJson() => WebGpuImports.GetAdapterInfoJson();
    internal override string GetLimitsJson() => WebGpuImports.GetLimitsJson();
    internal override string GetFeaturesJson() => WebGpuImports.GetFeaturesJson();
    internal override string GetPreferredCanvasFormat() => WebGpuImports.GetPreferredCanvasFormat();
    internal override int GetCanvasSize() => WebGpuImports.GetCanvasSize();
    internal override void ConfigureCanvas(string format, int usage, string viewFormatsJson, string alphaMode) =>
        WebGpuImports.ConfigureCanvas(format, usage, viewFormatsJson, alphaMode);
    internal override int AcquireCanvasTexture() => WebGpuImports.AcquireCanvasTexture();
    internal override int Present() => WebGpuImports.Present();

    internal override int CreateBuffer(double size, int usage, string label) => WebGpuImports.CreateBuffer(size, usage, label);
    internal override int CreateTexture(string descriptorJson) => WebGpuImports.CreateTexture(descriptorJson);
    internal override int CreateTextureView(int texture, string descriptorJson) => WebGpuImports.CreateTextureView(texture, descriptorJson);
    internal override int CreateSampler(string descriptorJson) => WebGpuImports.CreateSampler(descriptorJson);
    internal override int CreateShaderModule(string wgsl, string label) => WebGpuImports.CreateShaderModule(wgsl, label);
    internal override int CreateBindGroupLayout(string descriptorJson) => WebGpuImports.CreateBindGroupLayout(descriptorJson);
    internal override int CreatePipelineLayout(string descriptorJson) => WebGpuImports.CreatePipelineLayout(descriptorJson);
    internal override int CreateBindGroup(string descriptorJson) => WebGpuImports.CreateBindGroup(descriptorJson);
    internal override int CreateRenderPipeline(string descriptorJson) => WebGpuImports.CreateRenderPipeline(descriptorJson);
    internal override int CreateComputePipeline(string descriptorJson) => WebGpuImports.CreateComputePipeline(descriptorJson);
    internal override void SetLabel(int handle, string label) => WebGpuImports.SetLabel(handle, label);
    internal override void Release(int handle) => WebGpuImports.Release(handle);

    internal override void WriteBuffer(int buffer, double offset, nint ptr, int size) => WebGpuImports.WriteBuffer(buffer, offset, ptr, size);
    internal override int ReplayCommandStream(nint ptr, int length) => WebGpuImports.ReplayCommandStream(ptr, length);
    internal override void SubmitEmpty() => WebGpuImports.SubmitEmpty();
    internal override string PollErrors() => WebGpuImports.PollErrors();
    internal override void SetDebugMode(bool errorScopes) => WebGpuImports.SetDebugMode(errorScopes);
}
