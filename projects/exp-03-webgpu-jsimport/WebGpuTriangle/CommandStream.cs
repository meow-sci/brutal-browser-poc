using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WebGpuTriangle;

/// <summary>
/// Binary command stream in unmanaged wasm memory, replayed by webgpu-glue.js
/// `replayCommandStream(ptr, byteLength)` with ONE JS interop call.
/// Format: little-endian u32 words, [opcode][args...]; see OP in webgpu-glue.js.
/// </summary>
public sealed unsafe class CommandStream : IDisposable
{
    public const uint OpEnd = 0, OpBeginPass = 1, OpEndPass = 2, OpSetPipeline = 3, OpSetVertexBuffer = 4,
        OpSetBindGroup = 5, OpDraw = 6, OpWriteBuffer = 7, OpSubmit = 8;

    uint* _base;
    uint* _cur;
    uint* _end;

    public CommandStream(int capacityBytes)
    {
        _base = (uint*)NativeMemory.AlignedAlloc((nuint)capacityBytes, 16);
        _cur = _base;
        _end = _base + capacityBytes / 4;
    }

    public int ByteLength => (int)((byte*)_cur - (byte*)_base);
    public nint Pointer => (nint)_base;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Ensure(int words)
    {
        if (_cur + words > _end) throw new InvalidOperationException("command stream overflow");
    }

    public void BeginPass(int viewHandle, LoadOp load, StoreOp store, float r, float g, float b, float a)
    {
        Ensure(8);
        uint* p = _cur;
        p[0] = OpBeginPass; p[1] = (uint)viewHandle; p[2] = (uint)load; p[3] = (uint)store;
        float* f = (float*)p;
        f[4] = r; f[5] = g; f[6] = b; f[7] = a;
        _cur = p + 8;
    }

    public void EndPass() { Ensure(1); *_cur++ = OpEndPass; }

    public void SetPipeline(int pipeline) { Ensure(2); _cur[0] = OpSetPipeline; _cur[1] = (uint)pipeline; _cur += 2; }

    public void SetVertexBuffer(int slot, int buffer, uint offset)
    {
        Ensure(4);
        _cur[0] = OpSetVertexBuffer; _cur[1] = (uint)slot; _cur[2] = (uint)buffer; _cur[3] = offset; _cur += 4;
    }

    public void SetBindGroup(int index, int bindGroup)
    {
        Ensure(4);
        _cur[0] = OpSetBindGroup; _cur[1] = (uint)index; _cur[2] = (uint)bindGroup; _cur[3] = 0; _cur += 4;
    }

    public void SetBindGroup(int index, int bindGroup, uint dynamicOffset)
    {
        Ensure(5);
        _cur[0] = OpSetBindGroup; _cur[1] = (uint)index; _cur[2] = (uint)bindGroup; _cur[3] = 1; _cur[4] = dynamicOffset; _cur += 5;
    }

    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        Ensure(5);
        uint* p = _cur;
        p[0] = OpDraw; p[1] = vertexCount; p[2] = instanceCount; p[3] = firstVertex; p[4] = firstInstance;
        _cur = p + 5;
    }

    public void WriteBuffer(int buffer, uint bufferOffset, void* src, uint size)
    {
        Ensure(5);
        _cur[0] = OpWriteBuffer; _cur[1] = (uint)buffer; _cur[2] = bufferOffset; _cur[3] = (uint)(nint)src; _cur[4] = size; _cur += 5;
    }

    public void Submit() { Ensure(1); *_cur++ = OpSubmit; }

    /// <summary>Terminates the stream, replays it with one JS call and resets it. Returns commands executed.</summary>
    public int Flush()
    {
        Ensure(1);
        *_cur++ = OpEnd;
        int n = WebGpu.ReplayCommandStream(Pointer, ByteLength);
        _cur = _base;
        return n;
    }

    public void Dispose()
    {
        if (_base != null) { NativeMemory.AlignedFree(_base); _base = _cur = _end = null; }
    }
}
