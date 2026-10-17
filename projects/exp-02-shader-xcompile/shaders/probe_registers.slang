// Probe: HLSL-style register bindings (no [[vk::binding]]), ParameterBlock, specialization constant, builtins.
struct Globals { float4x4 viewProj; float4 tint; };
struct PerMaterial { float4 color; Texture2D<float4> albedo; SamplerState samp; };

ConstantBuffer<Globals> g : register(b0, space0);
Texture2D<float4> tex0 : register(t1, space0);
SamplerState samp0 : register(s2, space0);
ParameterBlock<PerMaterial> material;     // gets its own bind group
[[vk::constant_id(7)]] const int kMode = 2;

struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; nointerpolation uint inst : INSTANCE; };

[shader("vertex")]
VSOut vsMain(uint vid : SV_VertexID, uint iid : SV_InstanceID, float3 p : POSITION)
{
    VSOut o;
    o.pos = mul(g.viewProj, float4(p, 1)) + float4(vid * 0.0, iid * 0.0, 0, 0);
    o.uv = float2(vid & 1, vid >> 1);
    o.inst = iid;
    return o;
}

[shader("fragment")]
float4 fsMain(VSOut i, bool front : SV_IsFrontFace) : SV_Target
{
    float4 c = tex0.Sample(samp0, i.uv) * material.color * material.albedo.Sample(material.samp, i.uv);
    if (kMode == 3) c.rgb = 1 - c.rgb;
    return front ? c : c * 0.5 + float4(i.pos.xy * 0, 0, 0);
}
