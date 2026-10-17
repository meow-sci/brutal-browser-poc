#version 450
// PoC fragment shader: separate texture2D (binding 1) + sampler (binding 2), lambert lighting.
layout(set = 0, binding = 0) uniform Globals {
    mat4 model;
    mat4 view;
    mat4 proj;
    vec4 lightDir;
} u;
layout(set = 0, binding = 1) uniform texture2D tex;
layout(set = 0, binding = 2) uniform sampler samp;

layout(location = 0) in vec3 vNormal;
layout(location = 1) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    vec3 n = normalize(vNormal);
    float lambert = max(dot(n, normalize(-u.lightDir.xyz)), 0.0);
    vec4 albedo = texture(sampler2D(tex, samp), vUv);
    outColor = vec4(albedo.rgb * (0.1 + 0.9 * lambert), albedo.a);
}
