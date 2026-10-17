using System.Runtime.InteropServices;
using System.Text.Json;
using Brutal.Vulkan.WebGpu;

namespace WebGpuIcd.Tests;

/// <summary>A <see cref="WebGpuBridge"/> that records every call and fabricates handles (desktop tests; no WebGPU).</summary>
internal sealed class RecordingBridge : WebGpuBridge
{
    public sealed record Call(string Name, int Handle, string? Json = null, object?[]? Args = null);

    public readonly List<Call> Calls = new();
    /// <summary>Copies of every replayed stream (u32 words).</summary>
    public readonly List<uint[]> Streams = new();
    /// <summary>Immediate writeBuffer calls (flush/unmap): buffer, offset, bytes.</summary>
    public readonly List<(int Buffer, double Offset, byte[] Data)> Writes = new();
    public readonly List<int> Released = new();
    public readonly Dictionary<int, string> Kinds = new();

    public int CanvasWidth = 640, CanvasHeight = 480;
    public string PreferredFormat = "bgra8unorm";
    public string LimitsJson = "{\"maxBindGroups\":4,\"maxTextureDimension2D\":8192,\"maxVertexBuffers\":8,\"minUniformBufferOffsetAlignment\":256,\"maxUniformBufferBindingSize\":65536}";
    public string FeaturesJson = "[\"core-features-and-limits\"]";
    public string PendingErrors = "";
    public int ErrorsToReport;
    /// <summary>Extra <see cref="BridgeStatus"/> flags the next replays return (e.g. <see cref="BridgeStatus.ReplayOutOfMemory"/>).</summary>
    public int ReplayFlags;
    /// <summary>Simulates <c>device.lost</c>: replays return <see cref="BridgeStatus.ReplayDeviceLost"/>, acquire/present <see cref="BridgeStatus.CanvasDeviceLost"/>.</summary>
    public bool DeviceLost;
    private int _next = 16;

    public IEnumerable<Call> Named(string name) => Calls.Where(c => c.Name == name);

    public JsonElement JsonOf(int handle) => JsonDocument.Parse(Calls.Single(c => c.Handle == handle && c.Json is not null).Json!).RootElement;

    private int Create(string name, string? json, params object?[] args)
    {
        var h = _next++;
        Kinds[h] = name;
        Calls.Add(new Call(name, h, json, args));
        return h;
    }

    private void Record(string name, params object?[] args) => Calls.Add(new Call(name, 0, null, args));

    internal override string GetAdapterInfoJson() => "{\"vendor\":\"apple\",\"architecture\":\"metal-3\",\"device\":\"\",\"description\":\"\",\"isFallbackAdapter\":false}";
    internal override string GetLimitsJson() => LimitsJson;
    internal override string GetFeaturesJson() => FeaturesJson;
    internal override string GetPreferredCanvasFormat() => PreferredFormat;
    internal override int GetCanvasSize() => (CanvasWidth << 16) | CanvasHeight;
    internal override void ConfigureCanvas(string format, int usage, string viewFormatsJson, string alphaMode) =>
        Record("configureCanvas", format, usage, viewFormatsJson, alphaMode);
    internal override int AcquireCanvasTexture()
    {
        Record("acquireCanvasTexture");
        return DeviceLost ? BridgeStatus.CanvasDeviceLost : GetCanvasSize();
    }
    internal override int Present()
    {
        Record("present");
        return DeviceLost ? BridgeStatus.CanvasDeviceLost : GetCanvasSize();
    }

    internal override int CreateBuffer(double size, int usage, string label) => Create("createBuffer", null, size, usage);
    internal override int CreateTexture(string json) => Create("createTexture", json);
    internal override int CreateTextureView(int texture, string json) => Create("createTextureView", json, texture);
    internal override int CreateSampler(string json) => Create("createSampler", json);
    internal override int CreateShaderModule(string wgsl, string label) => Create("createShaderModule", null, label);
    internal override int CreateBindGroupLayout(string json) => Create("createBindGroupLayout", json);
    internal override int CreatePipelineLayout(string json) => Create("createPipelineLayout", json);
    internal override int CreateBindGroup(string json) => Create("createBindGroup", json);
    internal override int CreateRenderPipeline(string json) => Create("createRenderPipeline", json);
    internal override int CreateComputePipeline(string json) => Create("createComputePipeline", json);
    internal override void SetLabel(int handle, string label) => Record("setLabel", handle, label);
    internal override void Release(int handle)
    {
        Released.Add(handle);
        Record("release", handle);
    }

    internal override unsafe void WriteBuffer(int buffer, double offset, nint ptr, int size)
    {
        Writes.Add((buffer, offset, new ReadOnlySpan<byte>((void*)ptr, size).ToArray()));
        Record("writeBuffer", buffer, offset, size);
    }

    internal override unsafe int ReplayCommandStream(nint ptr, int length)
    {
        Streams.Add(MemoryMarshal.Cast<byte, uint>(new ReadOnlySpan<byte>((void*)ptr, length)).ToArray());
        Record("replayCommandStream", length);
        return ErrorsToReport | ReplayFlags | (DeviceLost ? BridgeStatus.ReplayDeviceLost : 0);
    }

    internal override void SubmitEmpty() => Record("submitEmpty");

    internal override string PollErrors()
    {
        var e = PendingErrors;
        PendingErrors = "";
        ErrorsToReport = 0;
        return e;
    }

    internal override void SetDebugMode(bool errorScopes) => Record("setDebugMode", errorScopes);
}

/// <summary>A bridge that does nothing (allocation measurements).</summary>
internal sealed class NullBridge : WebGpuBridge
{
    private int _next = 16;
    internal override string GetAdapterInfoJson() => "{}";
    internal override string GetLimitsJson() => "{}";
    internal override string GetFeaturesJson() => "[]";
    internal override string GetPreferredCanvasFormat() => "bgra8unorm";
    internal override int GetCanvasSize() => (640 << 16) | 480;
    internal override void ConfigureCanvas(string format, int usage, string viewFormatsJson, string alphaMode) { }
    internal override int AcquireCanvasTexture() => GetCanvasSize();
    internal override int Present() => GetCanvasSize();
    internal override int CreateBuffer(double size, int usage, string label) => _next++;
    internal override int CreateTexture(string json) => _next++;
    internal override int CreateTextureView(int texture, string json) => _next++;
    internal override int CreateSampler(string json) => _next++;
    internal override int CreateShaderModule(string wgsl, string label) => _next++;
    internal override int CreateBindGroupLayout(string json) => _next++;
    internal override int CreatePipelineLayout(string json) => _next++;
    internal override int CreateBindGroup(string json) => _next++;
    internal override int CreateRenderPipeline(string json) => _next++;
    internal override int CreateComputePipeline(string json) => _next++;
    internal override void SetLabel(int handle, string label) { }
    internal override void Release(int handle) { }
    internal override void WriteBuffer(int buffer, double offset, nint ptr, int size) { }
    internal override int ReplayCommandStream(nint ptr, int length) => 0;
    internal override void SubmitEmpty() { }
    internal override string PollErrors() => "";
    internal override void SetDebugMode(bool errorScopes) { }
}
