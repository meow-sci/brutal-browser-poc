// Portable push-constant pattern for Slang: real push constant for Vulkan SPIR-V, reserved uniform buffer for WGSL.
// Compile WGSL with -DTARGET_WGSL. (slangc's WGSL backend emits `var<uniform>` WITHOUT @group/@binding for [[vk::push_constant]].)
struct Globals { float4x4 view; float4x4 proj; float4 lightDir; };
struct Push    { float4x4 model; float4 tint; };

[[vk::binding(0, 0)]] ConstantBuffer<Globals> u;
[[vk::binding(1, 0)]] Texture2D<float4> tex;
[[vk::binding(2, 0)]] SamplerState samp;
#ifdef TARGET_WGSL
[[vk::binding(0, 3)]] ConstantBuffer<Push> pc;      // reserved group 3 = emulated push constants
#else
[[vk::push_constant]] ConstantBuffer<Push> pc;
#endif

struct VSOutput
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 uv   : TEXCOORD0;
    [[vk::location(1)]] float4 tint : COLOR0;
};

[shader("vertex")]
VSOutput vsMain([[vk::location(0)]] float3 position : POSITION, [[vk::location(2)]] float2 uv : TEXCOORD0)
{
    VSOutput o;
    o.position = mul(u.proj, mul(u.view, mul(pc.model, float4(position, 1.0))));
    o.uv = uv;
    o.tint = pc.tint;
    return o;
}

[shader("fragment")]
float4 fsMain(VSOutput i) : SV_Target { return tex.Sample(samp, i.uv) * i.tint; }
