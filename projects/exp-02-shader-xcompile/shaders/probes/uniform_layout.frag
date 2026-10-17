#version 450
// std140: float[4] has stride 16, vec3+float pack in 16 bytes, mat3 columns stride 16
layout(set = 0, binding = 0, std140) uniform U { float f[4]; vec3 v; float s; mat3 m; ivec2 i2; } u;
layout(location = 0) out vec4 o;
void main() { o = vec4(u.f[1] + u.f[3], u.v.x + u.s, u.m[2][1], float(u.i2.y)); }
