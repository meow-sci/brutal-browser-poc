#version 450
#extension GL_EXT_samplerless_texture_functions : require
layout(set = 0, binding = 0) uniform texture2DMS ms;
layout(location = 0) out vec4 o;
void main() { o = texelFetch(ms, ivec2(gl_FragCoord.xy), gl_SampleID); }
