#version 450
// 48-byte push block with a float[4]: the std430 array stride (4) is illegal in a WGSL uniform buffer, so the pipeline falls back
// to `@group(3) @binding(0) var<storage, read>` (push mode "storage").
layout(push_constant) uniform Push {
    float weights[4];
    vec3 offset;
    float scale;
    vec4 tint;
} pc;

layout(location = 0) in vec3 inPosition;
layout(location = 0) out vec4 vTint;

void main()
{
    vTint = pc.tint * (pc.weights[0] + pc.weights[3]);
    gl_Position = vec4(inPosition * pc.scale + pc.offset, 1.0);
}
