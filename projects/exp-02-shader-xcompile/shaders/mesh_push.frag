#version 450
// Variant: push constant read in the fragment stage as well (same block, different range).
layout(push_constant) uniform PushConstants {
    mat4 model;
    vec4 tint;
} pc;
layout(set = 0, binding = 1) uniform sampler2D tex;
layout(location = 0) in vec3 vNormal;
layout(location = 1) in vec2 vUv;
layout(location = 2) in vec4 vTint;
layout(location = 0) out vec4 outColor;
void main()
{
    outColor = texture(tex, vUv) * vTint * pc.tint;
}
