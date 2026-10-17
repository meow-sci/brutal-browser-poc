#version 450
// HelloMesh vertex shader contract: set 0 binding 0 std140 UBO, three vertex attributes.
layout(set = 0, binding = 0, std140) uniform Globals {
    mat4 model;
    mat4 view;
    mat4 proj;
    vec4 baseColor;
    vec4 lightDir;
} u;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUv;

layout(location = 0) out vec3 vNormal;
layout(location = 1) out vec2 vUv;

void main()
{
    vec4 world = u.model * vec4(inPosition, 1.0);
    vNormal = mat3(u.model) * inNormal;
    vUv = inUv;
    gl_Position = u.proj * u.view * world;
}
