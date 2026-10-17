#version 450
layout(set = 0, binding = 0) uniform texture2D texs[4];
layout(set = 0, binding = 1) uniform sampler samp;
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 o;
void main() { o = texture(sampler2D(texs[0], samp), uv) + texture(sampler2D(texs[3], samp), uv); }
