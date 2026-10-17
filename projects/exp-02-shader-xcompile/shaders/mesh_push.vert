#version 450
// Variant: model matrix delivered by a push-constant block instead of UBO member.
layout(set = 0, binding = 0) uniform Globals {
    mat4 view;
    mat4 proj;
    vec4 lightDir;
} u;
layout(push_constant) uniform PushConstants {
    mat4 model;
    vec4 tint;
} pc;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUv;

layout(location = 0) out vec3 vNormal;
layout(location = 1) out vec2 vUv;
layout(location = 2) out vec4 vTint;

void main()
{
    vec4 world = pc.model * vec4(inPosition, 1.0);
    vNormal = mat3(pc.model) * inNormal;
    vUv = inUv;
    vTint = pc.tint;
    gl_Position = u.proj * u.view * world;
}
