namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Record opcodes of the replay stream shared with <c>wwwroot/webgpu-glue.js</c>.
/// <para>
/// THE <c>OP</c> TABLE IN webgpu-glue.js MUST MATCH THIS ENUM (name and value); the unit test <c>OpcodeTableMatchesGlue</c>
/// parses the JS file and fails on any difference. The record layouts are documented in the README (opcode table) and in
/// the <see cref="CommandStream"/> writer methods.
/// </para>
/// Stream format: little-endian 32-bit words. Each record starts with a header word <c>opcode | (recordWords &lt;&lt; 16)</c>
/// where <c>recordWords</c> counts the header; inline data (push constants, labels, update data) is padded to 4 bytes.
/// </summary>
internal enum Op : ushort
{
    End = 0,
    BeginRenderPass = 1,
    EndRenderPass = 2,
    BindPipeline = 3,
    BindDescriptorSets = 4,
    BindVertexBuffers = 5,
    BindIndexBuffer = 6,
    SetViewport = 7,
    SetScissor = 8,
    SetBlendConstants = 9,
    SetStencilReference = 10,
    Draw = 11,
    DrawIndexed = 12,
    DrawIndirect = 13,
    DrawIndexedIndirect = 14,
    Dispatch = 15,
    DispatchIndirect = 16,
    CopyBuffer = 17,
    CopyBufferToTexture = 18,
    WriteTexture = 19,
    CopyTextureToBuffer = 20,
    CopyTextureToTexture = 21,
    ClearBuffer = 22,
    WriteBuffer = 23,
    UpdateBuffer = 24,
    PushConstants = 25,
    PushDebugGroup = 26,
    PopDebugGroup = 27,
    InsertDebugMarker = 28,
    Readback = 29,
    Barrier = 30,
}

/// <summary>Encodings of enum-like record fields (must match webgpu-glue.js).</summary>
internal static class StreamCodes
{
    public const uint LoadClear = 0, LoadLoad = 1, LoadOmit = 2;
    public const uint StoreStore = 0, StoreDiscard = 1, StoreOmit = 2;
    public const uint ClearFloat = 0, ClearSint = 1, ClearUint = 2;
    public const uint BindGraphics = 0, BindCompute = 1;
    public const uint IndexUint16 = 0, IndexUint32 = 1;
    public const uint AspectAll = 0, AspectDepthOnly = 1, AspectStencilOnly = 2;
    public const uint PushNone = 0, PushUniform = 1, PushStorage = 2;
    public const uint DepthReadOnly = 1, StencilReadOnly = 2;
    public const uint RenderPassHasDepthStencil = 1;
}
