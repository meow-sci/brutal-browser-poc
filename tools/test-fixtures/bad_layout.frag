#version 450
// MUST FAIL the layout lint: a std140 float[4] has an array stride of 16 bytes, but WGSL (and naga) use 4.
// Author UBO arrays as vec4[N] instead.
layout(set = 0, binding = 0, std140) uniform Weights {
    float values[4];
} w;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    int i = int(vUv.x * 3.0);
    outColor = vec4(w.values[i], w.values[3], 0.0, 1.0);
}
