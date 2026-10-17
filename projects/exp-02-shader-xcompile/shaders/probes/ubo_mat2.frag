#version 450
// std140 mat2 has column stride 16 (WGSL mat2x2<f32> has stride 8): naga rejects the SPIR-V (UnsupportedMatrixStride).
layout(set = 0, binding = 0, std140) uniform U { mat2 m; } u;
layout(location = 0) in vec2 p;
layout(location = 0) out vec4 o;
void main() { o = vec4(u.m * p, 0.0, 1.0); }
