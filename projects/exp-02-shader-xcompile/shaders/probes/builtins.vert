#version 450
layout(constant_id = 0) const float SCALE = 1.0;
layout(location = 0) out flat int vId;
void main() {
    vId = gl_VertexIndex + gl_InstanceIndex;
    gl_PointSize = 2.0;
    gl_Position = vec4(float(gl_VertexIndex & 1) * SCALE, float(gl_VertexIndex >> 1), 0.0, 1.0);
}
