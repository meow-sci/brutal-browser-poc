// Probe: SV_VulkanVertexID / SV_VulkanInstanceID (no BaseVertex/BaseInstance subtraction, hence no DrawParameters capability).
struct VSOut { float4 pos : SV_Position; [[vk::location(0)]] nointerpolation uint inst : INSTANCE; };
[shader("vertex")]
VSOut vsMain(uint vid : SV_VulkanVertexID, uint iid : SV_VulkanInstanceID, [[vk::location(0)]] float3 p : POSITION)
{ VSOut o; o.pos = float4(p + float3(vid * 0.0, iid * 0.0, 0), 1); o.inst = iid; return o; }
