// PoC Slang shader: same interface as mesh.vert/mesh.frag (GLSL).
// Explicit [[vk::binding(binding, set)]] so the SPIR-V and WGSL targets get identical group/binding numbers.
struct Globals
{
    float4x4 model;
    float4x4 view;
    float4x4 proj;
    float4   lightDir;
};

[[vk::binding(0, 0)]] ConstantBuffer<Globals> u;
[[vk::binding(1, 0)]] Texture2D<float4> tex;
[[vk::binding(2, 0)]] SamplerState samp;

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
    float4 position : SV_Position;
};

[shader("vertex")]
VSOutput vsMain(VSInput i)
{
    VSOutput o;
    float4 world = mul(u.model, float4(i.position, 1.0));
    o.normal = mul((float3x3)u.model, i.normal);
    o.uv = i.uv;
    o.position = mul(u.proj, mul(u.view, world));
    return o;
}

struct FSInput
{
    [[vk::location(0)]] float3 normal : NORMAL;
    [[vk::location(1)]] float2 uv     : TEXCOORD0;
};

[shader("fragment")]
float4 fsMain(FSInput i) : SV_Target
{
    float3 n = normalize(i.normal);
    float lambert = max(dot(n, normalize(-u.lightDir.xyz)), 0.0);
    float4 albedo = tex.Sample(samp, i.uv);
    return float4(albedo.rgb * (0.1 + 0.9 * lambert), albedo.a);
}
