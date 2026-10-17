#version 450
layout(location = 0) in vec3 p;
layout(location = 1) in vec4 instColor;   // per-instance attribute (vertex input rate is a pipeline property)
layout(location = 0) out vec4 c;
void main() { c = instColor * float(gl_InstanceIndex + gl_VertexIndex); gl_Position = vec4(p, 1.0); }
