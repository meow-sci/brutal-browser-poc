#version 450

layout(set = 0, binding = 0) uniform PerDraw {
    mat4 model;
    mat4 view;
    mat4 proj;
    vec4 baseColor;
    vec4 lightDir;
} u;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUv;

layout(location = 0) out vec3 outNormal;
layout(location = 1) out vec2 outUv;

void main() {
    outNormal = mat3(u.model) * inNormal;
    outUv = inUv;
    gl_Position = u.proj * u.view * u.model * vec4(inPosition, 1.0);
}
