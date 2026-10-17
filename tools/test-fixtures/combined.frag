#version 450
// Variant of mesh.frag with a combined image sampler: the pipeline splits it into texture@1 + sampler@501.
layout(set = 0, binding = 0, std140) uniform Globals {
    mat4 model;
    mat4 view;
    mat4 proj;
    vec4 baseColor;
    vec4 lightDir;
} u;
layout(set = 0, binding = 1) uniform sampler2D tex;

layout(location = 0) in vec3 vNormal;
layout(location = 1) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    vec3 n = normalize(vNormal);
    float lambert = max(dot(n, normalize(-u.lightDir.xyz)), 0.0);
    vec4 albedo = texture(tex, vUv) * u.baseColor;
    outColor = vec4(albedo.rgb * (0.1 + 0.9 * lambert), albedo.a);
}
