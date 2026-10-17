#version 450
// A specialization constant used directly in run-time arithmetic (no OpSpecConstantOp, which naga cannot import):
// becomes `@id(7) override ...` in WGSL and "7" in the manifest's overrides.
layout(constant_id = 7) const float GAIN = 1.0;

layout(set = 0, binding = 0, std140) uniform Params {
    vec4 color;
} p;

layout(location = 0) out vec4 outColor;

void main()
{
    outColor = p.color * GAIN;
}
