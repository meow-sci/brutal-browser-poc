#version 450
// Layout probe: push-constant block with a float[4] array and a vec3 + float pack (std430 stride 4 vs std140 stride 16).
layout(push_constant) uniform PushConstants {
    mat4 model;
    float weights[4];
    vec3 offset;
    float scale;
} pc;
layout(location = 0) in vec3 inPosition;
void main() { gl_Position = pc.model * vec4(inPosition * pc.scale + pc.offset, 1.0) * (pc.weights[0] + pc.weights[3]); }
