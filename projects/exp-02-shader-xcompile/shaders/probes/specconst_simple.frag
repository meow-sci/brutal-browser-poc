#version 450
// Direct (non-folded) use of specialization constants: this is the form naga's SPIR-V front end can import as `override`.
layout(constant_id = 7) const int MODE = 2;
layout(constant_id = 8) const float GAMMA = 2.2;
layout(constant_id = 9) const bool USE_FOG = true;
layout(location = 0) in vec4 c;
layout(location = 0) out vec4 o;
void main() {
    vec4 r = c * GAMMA;
    if (USE_FOG) r.rgb *= 0.5;
    r.a += float(MODE);
    o = r;
}
