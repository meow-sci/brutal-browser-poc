#version 450
// A 64-byte push-constant block (the limit is 128 bytes): emulated as a uniform buffer at group 3 binding 0 in the browser.
layout(push_constant) uniform Push {
    mat4 mvp;
} pc;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inUv;

layout(location = 0) out vec2 vUv;

void main()
{
    vUv = inUv;
    gl_Position = pc.mvp * vec4(inPosition, 1.0);
}
