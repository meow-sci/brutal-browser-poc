#version 450
// Web ICD smoke test: separate texture + sampler (web profile convention), uniform tint and a push-constant tint.
layout(set = 0, binding = 0) uniform Ubo { mat4 mvp; vec4 tint; } ubo;
layout(set = 0, binding = 1) uniform texture2D tex;
layout(set = 0, binding = 2) uniform sampler samp;
layout(push_constant) uniform Push { vec4 tint; } pc;
layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    outColor = texture(sampler2D(tex, samp), vUv) * ubo.tint * pc.tint;
}
