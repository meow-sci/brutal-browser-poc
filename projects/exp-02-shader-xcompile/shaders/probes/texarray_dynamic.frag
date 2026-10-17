#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(set = 0, binding = 0) uniform texture2D texs[];
layout(set = 0, binding = 1) uniform sampler samp;
layout(location = 0) in vec2 uv;
layout(location = 1) in flat int idx;
layout(location = 0) out vec4 o;
void main() { o = texture(sampler2D(texs[nonuniformEXT(idx)], samp), uv); }
