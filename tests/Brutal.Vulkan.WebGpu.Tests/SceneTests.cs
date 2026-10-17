using System.Text.Json;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;
using WebGpuIcdSmoke;

namespace WebGpuIcd.Tests;

/// <summary>The smoke app's game-shaped program (desktop hello-mesh recipe) against the recording bridge.</summary>
public sealed class SceneTests
{
    private readonly RecordingBridge _bridge = TestIcd.Reset();

    private static TriangleScene NewScene() =>
        new(TestIcd.Resource("Shaders/tri.vert.spv"), TestIcd.Resource("Shaders/tri.frag.spv"));

    private JsonElement Json(RecordingBridge.Call call) => JsonDocument.Parse(call.Json!).RootElement;

    [Fact]
    public void CreationProducesTheExpectedWebGpuObjects()
    {
        using var scene = NewScene();

        var configure = _bridge.Named("configureCanvas").Single();
        Assert.Equal(["bgra8unorm", GpuTextureUsage.RenderAttachment | GpuTextureUsage.CopySrc, "[\"bgra8unorm-srgb\"]", "opaque"], configure.Args!);

        var textures = _bridge.Named("createTexture").Select(Json).ToArray();
        var depth = textures.Single(t => t.GetProperty("format").GetString() == "depth32float");
        Assert.Equal(GpuTextureUsage.RenderAttachment, depth.GetProperty("usage").GetInt32());
        Assert.Equal([640, 480, 1], depth.GetProperty("size").EnumerateArray().Select(e => e.GetInt32()));
        var texture = textures.Single(t => t.GetProperty("format").GetString() == "rgba8unorm");
        Assert.Equal(GpuTextureUsage.CopyDst | GpuTextureUsage.TextureBinding, texture.GetProperty("usage").GetInt32());
        Assert.Equal("rgba8unorm-srgb", texture.GetProperty("viewFormats")[0].GetString());

        var sampler = Json(_bridge.Named("createSampler").Single());
        Assert.Equal("nearest", sampler.GetProperty("magFilter").GetString());
        Assert.Equal("clamp-to-edge", sampler.GetProperty("addressModeU").GetString());

        Assert.Equal(["tri.vert", "tri.frag"], _bridge.Named("createShaderModule").Select(c => (string)c.Args![0]!));

        var bgl = _bridge.Named("createBindGroupLayout").Single();
        var entries = Json(bgl).GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Equal("uniform", entries[0].GetProperty("buffer").GetProperty("type").GetString());
        Assert.True(entries[0].GetProperty("buffer").GetProperty("hasDynamicOffset").GetBoolean());
        Assert.Equal(GpuShaderStage.Vertex | GpuShaderStage.Fragment, entries[0].GetProperty("visibility").GetInt32());
        Assert.Equal("float", entries[1].GetProperty("texture").GetProperty("sampleType").GetString());
        Assert.Equal("2d", entries[1].GetProperty("texture").GetProperty("viewDimension").GetString());
        Assert.Equal("filtering", entries[2].GetProperty("sampler").GetProperty("type").GetString());

        // set 0, empty groups 1 and 2 (unused), push constants (uniform) in group 3
        var layout = Json(_bridge.Named("createPipelineLayout").Single()).GetProperty("bindGroupLayouts").EnumerateArray().Select(e => e.GetInt32());
        Assert.Equal([bgl.Handle, ReservedHandles.EmptyBindGroupLayout, ReservedHandles.EmptyBindGroupLayout, ReservedHandles.PushUniformLayout], layout);

        var pipeline = Json(_bridge.Named("createRenderPipeline").Single());
        var buffer = pipeline.GetProperty("vertex").GetProperty("buffers")[0];
        Assert.Equal(20, buffer.GetProperty("arrayStride").GetInt32());
        Assert.Equal(["float32x3", "float32x2"], buffer.GetProperty("attributes").EnumerateArray().Select(a => a.GetProperty("format").GetString()));
        Assert.Equal("triangle-list", pipeline.GetProperty("primitive").GetProperty("topology").GetString());
        Assert.Equal("back", pipeline.GetProperty("primitive").GetProperty("cullMode").GetString());
        Assert.Equal("ccw", pipeline.GetProperty("primitive").GetProperty("frontFace").GetString());
        var ds = pipeline.GetProperty("depthStencil");
        Assert.Equal("depth32float", ds.GetProperty("format").GetString());
        Assert.True(ds.GetProperty("depthWriteEnabled").GetBoolean());
        Assert.Equal("less-equal", ds.GetProperty("depthCompare").GetString());
        Assert.Equal("bgra8unorm", pipeline.GetProperty("fragment").GetProperty("targets")[0].GetProperty("format").GetString());
        Assert.Equal(15, pipeline.GetProperty("fragment").GetProperty("targets")[0].GetProperty("writeMask").GetInt32());

        // StagingPool: map + copy + unmap uploads each staging buffer once (writeBuffer from the shadow)
        var vertexBytes = TriangleScene.Vertices.Length * 20;
        Assert.Contains(_bridge.Writes, w => w.Data.Length == vertexBytes);
        Assert.Contains(_bridge.Writes, w => w.Data.Length == 16 && BitConverter.ToUInt32(w.Data, 12) == TriangleScene.Texels[3]);

        // one replay for the staging submit: buffer copy, then the texel upload as writeTexture (8-byte rows need no 256 alignment)
        var upload = Streams.Decode(_bridge.Streams.Single());
        Assert.Equal([Op.CopyBuffer, Op.Barrier, Op.WriteTexture, Op.Barrier, Op.End], upload.Select(r => r.Op));
        var copy = upload[0].Payload;
        Assert.Equal((uint)vertexBytes, copy[4]);
        var write = upload[2].Payload;
        Assert.Equal([8u, 2u], write[2..4]);                 // bytesPerRow, rowsPerImage
        Assert.Equal([2u, 2u, 1u], write[10..13]);           // extent
        Assert.Empty(TestIcd.Errors());
    }

    [Fact]
    public void AFrameIsOneReplayBetweenAcquireAndPresent()
    {
        using var scene = NewScene();
        scene.Frame(); // the first bind builds the GPUBindGroup (lazily, once)
        Assert.Single(_bridge.Named("createBindGroup"));
        var before = _bridge.Calls.Count;
        for (var i = 0; i < 3; i++)
            Assert.True(scene.Frame());
        var frameCalls = _bridge.Calls.Skip(before).Select(c => c.Name).ToArray();
        Assert.Equal(Enumerable.Repeat(new[] { "acquireCanvasTexture", "replayCommandStream", "present" }, 3).SelectMany(x => x), frameCalls);

        var records = Streams.Decode(_bridge.Streams[^1]);
        Assert.Equal(
        [
            Op.WriteBuffer, // coherent upload of the persistently mapped uniform buffer
            Op.PushDebugGroup, Op.Barrier, Op.BeginRenderPass, Op.BindPipeline, Op.SetViewport, Op.SetScissor, Op.BindVertexBuffers,
            Op.PushDebugGroup,
            Op.PushConstants, Op.BindDescriptorSets, Op.Draw,
            Op.PushConstants, Op.BindDescriptorSets, Op.Draw,
            Op.PopDebugGroup, Op.EndRenderPass, Op.Barrier, Op.PopDebugGroup, Op.End,
        ], records.Select(r => r.Op));

        var upload = records[0].Payload;
        Assert.Equal(512u, upload[3]);
        records.RemoveAll(r => r.Op is Op.PushDebugGroup or Op.PopDebugGroup);

        var pass = records[2].Payload;
        Assert.Equal(1u, pass[0]);                                         // one colour attachment
        Assert.Equal(StreamCodes.RenderPassHasDepthStencil, pass[1]);
        Assert.Equal([0u, 0u, 640u, 480u, 640u, 480u], pass[2..8]);       // render area, attachment size
        Assert.Equal((uint)ReservedHandles.CanvasView, pass[8]);          // swapchain view resolved at replay
        Assert.Equal(StreamCodes.LoadClear, pass[10]);
        Assert.Equal(StreamCodes.StoreStore, pass[11]);
        Assert.Equal(0.1f, Streams.F(pass[13]), 5);
        Assert.Equal(0.3f, Streams.F(pass[15]), 5);
        Assert.Equal(StreamCodes.LoadClear, pass[18]);                    // depth: clear to 1.0, DONT_CARE -> discard
        Assert.Equal(StreamCodes.StoreDiscard, pass[19]);
        Assert.Equal(1f, Streams.F(pass[20]));
        Assert.Equal(StreamCodes.LoadOmit, pass[21]);                     // D32: no stencil ops

        var bind = records[3].Payload;
        Assert.Equal([StreamCodes.BindGraphics, StreamCodes.PushUniform, 0b0110u, 4u], bind[1..5]);
        Assert.Equal([0f, 0f, 640f, 480f, 0f, 1f], records[4].Payload.Select(Streams.F));

        var push = records[7].Payload;
        Assert.Equal([0u, 16u], push[..2]);
        Assert.Equal(TriangleScene.FrontPushTint.Y, Streams.F(push[3]));

        var sets = records[8].Payload;
        Assert.Equal([StreamCodes.BindGraphics, 0u, 1u], sets[..3]);
        Assert.NotEqual(0u, sets[3]);                                      // the bind group
        Assert.Equal([1u, 0u], sets[4..6]);                                // one dynamic offset: 0
        Assert.Equal([1u, 256u], records[11].Payload[4..6]);               // second draw: slot 1
        Assert.Equal([3u, 1u, 0u, 0u], records[9].Payload);

        // the bind group is built once and reused across frames
        Assert.Single(_bridge.Named("createBindGroup"));
        Assert.Empty(TestIcd.Errors());
    }

    [Fact]
    public unsafe void ReadbackOfTheSwapchainLandsOnALaterFrame()
    {
        using var scene = NewScene();
        scene.ReadbackAtFrame = 1;
        scene.Frame();
        scene.Frame(); // frame 1: main submit + readback submit (copy from the canvas texture, readback into the shadow)
        var readback = Streams.Decode(_bridge.Streams[^1]);
        // every submit re-uploads the persistently mapped (coherent) uniform buffer first
        Assert.Equal([Op.WriteBuffer, Op.CopyTextureToBuffer, Op.Readback, Op.End], readback.Select(r => r.Op));
        readback.RemoveAt(0);
        Assert.Equal((uint)ReservedHandles.CanvasTexture, readback[0].Payload[0]);
        Assert.Equal([320u, 240u], readback[0].Payload[2..4]);
        scene.Frame();
        Assert.Null(scene.ReadbackPixel); // JS has not delivered the data yet: the fence stays unsignalled, nothing blocks

        // what webgpu-glue.js does when mapAsync resolves: copy into the shadow, then set the fence's status word
        var fence = HandleTable.LiveObjects().OfType<FenceObject>().Single(f => f.Tickets.Any(t => !t.Completed));
        var buffer = HandleTable.LiveObjects().OfType<BufferObject>().Single(b => b.Gpu == readback[1].Payload[0]);
        new byte[] { 0, 128, 255, 255 }.CopyTo(new Span<byte>(buffer.ShadowAddress(0), 4));
        *(int*)fence.Tickets.Single().Status = 1;
        scene.Frame();
        Assert.Equal([0, 128, 255, 255], scene.ReadbackPixel);
        Assert.Equal(2, scene.ReadbackLatency);
        Assert.Empty(TestIcd.Errors());
    }

    [Fact]
    public unsafe void ComputeDispatchUsesTheComputeBindPointAndThePushRing()
    {
        using var scene = NewScene();
        using var compute = new ComputeCheck(scene.Device, scene.Queue, TestIcd.Resource("Shaders/fill.comp.spv"));
        var pipeline = JsonDocument.Parse(_bridge.Named("createComputePipeline").Single().Json!).RootElement;
        Assert.Equal("main", pipeline.GetProperty("compute").GetProperty("entryPoint").GetString());
        var layout = JsonDocument.Parse(_bridge.Named("createPipelineLayout").Last().Json!).RootElement.GetProperty("bindGroupLayouts").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Equal([ReservedHandles.EmptyBindGroupLayout, ReservedHandles.EmptyBindGroupLayout, ReservedHandles.PushUniformLayout], layout[1..]);
        var bgl = JsonDocument.Parse(_bridge.Named("createBindGroupLayout").Last().Json!).RootElement.GetProperty("entries")[0];
        Assert.Equal("storage", bgl.GetProperty("buffer").GetProperty("type").GetString()); // read_write in the WGSL
        Assert.Equal(GpuShaderStage.Compute, bgl.GetProperty("visibility").GetInt32());

        var records = Streams.Decode(_bridge.Streams[^1]);
        Assert.Equal([Op.WriteBuffer, Op.BindPipeline, Op.BindDescriptorSets, Op.PushConstants, Op.Dispatch, Op.Barrier, Op.CopyBuffer, Op.Readback, Op.End],
            records.Select(r => r.Op));
        Assert.Equal([StreamCodes.BindCompute, StreamCodes.PushUniform, 0b0110u, 4u], records[1].Payload[1..5]);
        Assert.Equal(StreamCodes.BindCompute, records[2].Payload[0]);
        Assert.Equal([1u, 1u, 1u], records[4].Payload);
        Assert.False(compute.Poll());

        var fence = HandleTable.LiveObjects().OfType<FenceObject>().Single(f => f.Tickets.Any(t => !t.Completed));
        var buffer = HandleTable.LiveObjects().OfType<BufferObject>().Single(b => b.Gpu == records[7].Payload[0]);
        for (var i = 0; i < ComputeCheck.Count; i++)
            ((uint*)buffer.ShadowAddress(0))[i] = (uint)i * 3 + ComputeCheck.Bias;
        *(int*)fence.Tickets.Single().Status = 1;
        Assert.True(compute.Poll());
        Assert.True(compute.IsCorrect);
        Assert.Empty(TestIcd.Errors());
    }

    [Fact]
    public void CanvasResizeMakesAcquireOutOfDateAndTheSceneRecreatesItsSwapchain()
    {
        using var scene = NewScene();
        scene.Frame();
        _bridge.CanvasWidth = 800;
        _bridge.CanvasHeight = 600;
        scene.Frame(); // acquire → VK_ERROR_OUT_OF_DATE_KHR → recreate
        Assert.Equal(1, scene.SwapchainRecreations);
        Assert.Equal(2, _bridge.Named("configureCanvas").Count());
        scene.Frame();
        var pass = Streams.Decode(_bridge.Streams[^1]).First(r => r.Op == Op.BeginRenderPass).Payload;
        Assert.Equal([800u, 600u], pass[6..8]);
        Assert.Equal(2, scene.FramesRendered);
    }

    [Fact]
    public void DisposeReleasesEverythingItCreated()
    {
        var live = HandleTable.LiveCount;
        var scene = NewScene();
        scene.Frame();
        scene.Dispose();
        Assert.Equal(live, HandleTable.LiveCount);
        var released = _bridge.Released.ToHashSet();
        foreach (var kind in new[] { "createBuffer", "createTexture", "createTextureView", "createSampler", "createRenderPipeline", "createBindGroup" })
            Assert.All(_bridge.Named(kind), c => Assert.Contains(c.Handle, released));
        Assert.Empty(TestIcd.Errors());
    }

    [Fact]
    public void SteadyStateFramesDoNotAllocateInTheIcd()
    {
        TestIcd.Reset();
        WebIcd.Bridge = new NullBridge();
        using var scene = NewScene();
        for (var i = 0; i < 10; i++)
            scene.Frame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
            scene.Frame();
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 100.0;
        // BRUTAL's own wrappers (Queue lock, collection expressions) may allocate a little; the ICD itself must not.
        Console.WriteLine($"steady-state allocation: {perFrame} bytes per frame");
        Assert.True(perFrame < 64, $"{perFrame} bytes allocated per frame");
    }
}
