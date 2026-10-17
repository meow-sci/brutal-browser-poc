// Probe: HLSL register-based bindings + -fvk-*-shift (no ParameterBlock).
struct Globals { float4x4 viewProj; float4 tint; };
ConstantBuffer<Globals> g : register(b0);
Texture2D<float4> tex0 : register(t1);
SamplerState samp0 : register(s2);
struct VSOut { float4 pos : SV_Position; [[vk::location(0)]] float2 uv : TEXCOORD0; };
[shader("vertex")]
VSOut vsMain([[vk::location(0)]] float3 p : POSITION, [[vk::location(1)]] float2 uv : TEXCOORD0)
{ VSOut o; o.pos = mul(g.viewProj, float4(p, 1)); o.uv = uv; return o; }
[shader("fragment")]
float4 fsMain(VSOut i) : SV_Target { return tex0.Sample(samp0, i.uv) * g.tint; }
