#version 450
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[1]; };
void main() { gl_Position = vec4(0.0, 0.0, 0.0, 1.0); gl_ClipDistance[0] = gl_Position.x; }
