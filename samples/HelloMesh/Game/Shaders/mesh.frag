#version 450

layout(set = 0, binding = 0) uniform PerDraw {
    mat4 model;
    mat4 view;
    mat4 proj;
    vec4 baseColor;
    vec4 lightDir;
} u;

layout(set = 0, binding = 1) uniform texture2D baseColorTexture;
layout(set = 0, binding = 2) uniform sampler baseColorSampler;

layout(location = 0) in vec3 inNormal;
layout(location = 1) in vec2 inUv;

layout(location = 0) out vec4 outColor;

void main() {
    vec3 n = normalize(inNormal);
    vec3 l = normalize(u.lightDir.xyz);
    vec4 tex = texture(sampler2D(baseColorTexture, baseColorSampler), inUv);
    vec4 color = u.baseColor * tex * (0.25 + 0.75 * max(dot(n, l), 0.0));
    outColor = vec4(color.rgb, 1.0);
}
