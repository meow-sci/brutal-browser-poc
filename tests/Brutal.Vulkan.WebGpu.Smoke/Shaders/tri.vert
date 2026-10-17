#version 450
// Web ICD smoke test: positions + texture coordinates, transformed by a dynamic-offset uniform buffer.
layout(set = 0, binding = 0) uniform Ubo { mat4 mvp; vec4 tint; } ubo;
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec2 inUv;
layout(location = 0) out vec2 vUv;

void main()
{
    vUv = inUv;
    gl_Position = ubo.mvp * vec4(inPos, 1.0);
}
