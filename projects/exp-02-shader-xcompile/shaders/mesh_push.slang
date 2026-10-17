// Slang variant with push constants ([[vk::push_constant]]) and a combined sampler (Sampler2D).
struct Globals { float4x4 view; float4x4 proj; float4 lightDir; };
struct Push    { float4x4 model; float4 tint; };

[[vk::binding(0, 0)]] ConstantBuffer<Globals> u;
[[vk::binding(1, 0)]] Sampler2D tex;           // Slang combined texture+sampler
[[vk::push_constant]] ConstantBuffer<Push> pc;

struct VSInput
{
    [[vk::location(0)]] float3 position : POSITION;
    [[vk::location(1)]] float3 normal   : NORMAL;
    [[vk::location(2)]] float2 uv       : TEXCOORD0;
};
struct VSOutput
{
    [[vk::location(0)]] float3 normal : NORMAL;
    [[vk::location(1)]] float2 uv     : TEXCOORD0;
    [[vk::location(2)]] float4 tint   : COLOR0;
    float4 position : SV_Position;
};

[shader("vertex")]
VSOutput vsMain(VSInput i)
{
    VSOutput o;
    float4 world = mul(pc.model, float4(i.position, 1.0));
    o.normal = mul((float3x3)pc.model, i.normal);
    o.uv = i.uv;
    o.tint = pc.tint;
    o.position = mul(u.proj, mul(u.view, world));
    return o;
}

[shader("fragment")]
float4 fsMain(VSOutput i) : SV_Target
{
    return tex.Sample(i.uv) * i.tint * pc.tint;
}
