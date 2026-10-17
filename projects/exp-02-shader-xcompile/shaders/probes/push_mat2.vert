#version 450
layout(push_constant) uniform P { mat2 m; vec2 o; } pc;
layout(location = 0) in vec2 p;
void main() { gl_Position = vec4(pc.m * p + pc.o, 0.0, 1.0); }
