#version 450
// MUST FAIL: 144-byte push-constant block, larger than the 128-byte limit of the web pipeline (the ICD's maxPushConstantsSize).
layout(push_constant) uniform Push {
    mat4 model;
    mat4 viewProj;
    vec4 tint;
} pc;

layout(location = 0) in vec3 inPosition;
layout(location = 0) out vec4 vTint;

void main()
{
    vTint = pc.tint;
    gl_Position = pc.viewProj * pc.model * vec4(inPosition, 1.0);
}
