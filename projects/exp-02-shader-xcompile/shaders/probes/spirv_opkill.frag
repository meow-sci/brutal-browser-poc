#version 450
layout(location = 0) out vec4 o;
layout(location = 0) in float a;
void main() { if (a > 0.5) discard; o = vec4(a); }
