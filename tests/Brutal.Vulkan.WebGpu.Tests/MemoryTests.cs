using Brutal;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace WebGpuIcd.Tests;

/// <summary>Host-visible shadows, uploads (unmap, flush, coherent at submit) and readback fences.</summary>
public sealed unsafe class MemoryTests
{
    private readonly RecordingBridge _bridge = TestIcd.Reset();

    private static int Gpu(BufferEx b) => HandleTable.Get<BufferObject>(b.VkBuffer.VkHandle)!.Gpu;

    [Fact]
    public void UnmapUploadsTheMappedRange()
    {
        using var dev = new TestDevice();
        using var buffer = dev.HostBuffer(64, VkBufferUsageFlags.TransferSrcBit);
        using (var map = buffer.Map())
            map.AsSpan<byte>().Fill(7);
        var write = _bridge.Writes.Single();
        Assert.Equal(Gpu(buffer), write.Buffer);
        Assert.Equal(0, write.Offset);
        Assert.Equal(Enumerable.Repeat((byte)7, 64), write.Data);
    }

    [Fact]
    public void FlushUploadsOnlyTheFlushedRange()
    {
        using var dev = new TestDevice();
        using var buffer = dev.HostBuffer(64, VkBufferUsageFlags.UniformBufferBit);
        using var map = buffer.Map();
        map.AsSpan<byte>().Fill(3);
        map.Offset(new MapRegion { Offset = ByteSize.Of<byte>(16), Size = ByteSize.Of<byte>(16) }).Flush();
        var write = _bridge.Writes.Single();
        Assert.Equal(16, write.Offset);
        Assert.Equal(16, write.Data.Length);
    }

    [Fact]
    public void PersistentlyMappedCoherentMemoryIsUploadedAtEverySubmitUntilUnmapped()
    {
        using var dev = new TestDevice();
        using var buffer = dev.HostBuffer(96, VkBufferUsageFlags.UniformBufferBit);
        var map = buffer.Map();
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        cmd.End();
        dev.Queue.Submit([], [], [cmd], [], default);
        dev.Queue.Submit([], [], [cmd], [], default);
        foreach (var stream in _bridge.Streams)
        {
            var upload = Streams.Decode(stream)[0];
            Assert.Equal(Op.WriteBuffer, upload.Op);
            Assert.Equal([(uint)Gpu(buffer), 0u], upload.Payload[..2]);
            Assert.Equal(96u, upload.Payload[3]);
        }
        map.Dispose(); // unmap: uploaded once more (writeBuffer), then no longer part of submits
        Assert.Single(_bridge.Writes);
        dev.Queue.Submit([], [], [cmd], [], default);
        Assert.Equal(2, _bridge.Streams.Count); // nothing left to replay: no JS call at all
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void EmptySubmitsDoNotCallJavaScript()
    {
        using var dev = new TestDevice();
        var fence = dev.Device.CreateFence(VkFenceCreateFlags.None, null);
        dev.Queue.Submit([], [], [], [], fence);
        Assert.Empty(_bridge.Named("replayCommandStream"));
        Assert.Equal(VkResult.Success, dev.Device.WaitForFence(fence, 0));
        dev.Device.DestroyFence(fence, null);
    }

    [Fact]
    public void DeviceLocalMemoryCannotBeMapped()
    {
        using var dev = new TestDevice();
        using var buffer = dev.DeviceBuffer(64, VkBufferUsageFlags.VertexBufferBit);
        void* p;
        var r = Icd.vkMapMemory(dev.Device.Handle, buffer.VkDeviceMemory, (ByteSize64)0UL, (ByteSize64)ulong.MaxValue, VkMemoryMapFlags.None, &p);
        Assert.Equal(VkResult.ErrorMemoryMapFailed, r);
        Assert.Contains(TestIcd.Errors(), l => l.Contains("not HOST_VISIBLE"));
    }

    [Fact]
    public void BufferMemoryRequirementsAllowBothTypes()
    {
        using var dev = new TestDevice();
        using var buffer = dev.DeviceBuffer(10, VkBufferUsageFlags.VertexBufferBit);
        var req = dev.Device.GetBufferMemoryRequirements(buffer.VkBuffer);
        Assert.Equal(12UL, req.Size.Value); // rounded up to 4
        Assert.Equal(256UL, req.Alignment.Value);
        Assert.Equal(0b11, req.MemoryTypeBits);
        var usage = (int)_bridge.Named("createBuffer").Single().Args![1]!;
        Assert.Equal(GpuBufferUsage.Vertex | GpuBufferUsage.CopyDst | GpuBufferUsage.CopySrc, usage);
    }

    [Fact]
    public void ReadbackFenceSignalsOnlyWhenTheDataLanded()
    {
        using var dev = new TestDevice();
        using var src = dev.DeviceBuffer(256, VkBufferUsageFlags.TransferSrcBit);
        using var dst = dev.HostBuffer(256, VkBufferUsageFlags.TransferDstBit);
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        cmd.CopyBuffer(src.VkBuffer, dst.VkBuffer, ByteSize.Of<byte>(256));
        cmd.End();
        var fence = dev.Device.CreateFence(VkFenceCreateFlags.None, null);
        dev.Queue.Submit([], [], [cmd], [], fence);

        var records = Streams.Decode(_bridge.Streams.Single());
        Assert.Equal([Op.CopyBuffer, Op.Readback, Op.End], records.Select(r => r.Op));
        Assert.Equal([(uint)Gpu(dst), 0u, 256u], records[1].Payload[..3]);

        Assert.Equal(VkResult.NotReady, Icd.vkGetFenceStatus(dev.Device.Handle, fence));
        Assert.Equal(VkResult.Timeout, dev.Device.WaitForFence(fence, -1)); // never blocks
        Assert.Contains("vkWaitForFences: no blocking waits on readback fences", IcdTestHooks.Violations);

        // what webgpu-glue.js does when mapAsync resolves: copy into the shadow, then set the status word
        var fenceObject = HandleTable.Get<FenceObject>(fence.VkHandle)!;
        var shadow = HandleTable.Get<BufferObject>(dst.VkBuffer.VkHandle)!.ShadowAddress(0);
        new Span<byte>(shadow, 256).Fill(0xAB);
        *(int*)fenceObject.Tickets.Single().Status = 1;

        Assert.Equal(VkResult.Success, dev.Device.WaitForFence(fence, -1));
        using (var map = dst.Map())
            Assert.All(map.AsSpan<byte>().ToArray(), b => Assert.Equal(0xAB, b));
        dev.Device.DestroyFence(fence, null);
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void FencesWithoutReadbackSignalAtSubmit()
    {
        using var dev = new TestDevice();
        using var src = dev.DeviceBuffer(64, VkBufferUsageFlags.TransferSrcBit);
        using var dst = dev.DeviceBuffer(64, VkBufferUsageFlags.TransferDstBit);
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        cmd.CopyBuffer(src.VkBuffer, dst.VkBuffer, ByteSize.Of<byte>(64));
        cmd.End();
        var fence = dev.Device.CreateFence(VkFenceCreateFlags.None, null);
        Assert.Equal(VkResult.NotReady, Icd.vkGetFenceStatus(dev.Device.Handle, fence));
        dev.Queue.Submit([], [], [cmd], [], fence);
        Assert.Equal(VkResult.Success, Icd.vkGetFenceStatus(dev.Device.Handle, fence));
        dev.Device.ResetFences([fence]);
        Assert.Equal(VkResult.NotReady, Icd.vkGetFenceStatus(dev.Device.Handle, fence));
        dev.Device.DestroyFence(fence, null);
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void StagingPoolUploadCompletesInOneSubmit()
    {
        using var dev = new TestDevice();
        using var target = dev.DeviceBuffer(128, VkBufferUsageFlags.VertexBufferBit | VkBufferUsageFlags.TransferDstBit);
        using (var staging = dev.Device.CreateStagingPool(dev.Queue, 1))
        {
            var data = Enumerable.Range(0, 32).Select(i => (float)i).ToArray();
            var staged = staging.AddStagingBuffer<float>(data.AsSpan());
            var cmd = staging.NextCommandBuffer();
            cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
            cmd.CopyBuffer(staged.VkBuffer, target.VkBuffer, target.BufferSize);
            cmd.End();
            staging.Submit().Wait();
        }
        Assert.Single(_bridge.Named("replayCommandStream"));
        Assert.Equal(128, _bridge.Writes.Single().Data.Length);
        Assert.Equal(31f, BitConverter.ToSingle(_bridge.Writes.Single().Data, 124));
        Assert.Empty(TestIcd.Errors());
    }
}
