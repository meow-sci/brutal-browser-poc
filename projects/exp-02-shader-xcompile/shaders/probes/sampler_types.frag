#version 450
layout(set = 0, binding = 0) uniform sampler samp;
layout(set = 0, binding = 1) uniform samplerShadow sampCmp;
layout(set = 0, binding = 2) uniform textureCube cube;
layout(set = 0, binding = 3) uniform texture2DArray arr;
layout(set = 0, binding = 4) uniform texture3D vol;
layout(set = 0, binding = 5) uniform texture2D depthTex;
layout(location = 0) in vec3 uvw;
layout(location = 0) out vec4 o;
void main() {
    o = texture(samplerCube(cube, samp), uvw) + texture(sampler2DArray(arr, samp), uvw) + texture(sampler3D(vol, samp), uvw)
      + vec4(texture(sampler2DShadow(depthTex, sampCmp), uvw));
}
