#version 450
layout(location = 0) in flat int vId;
layout(location = 0) out vec4 outColor;
void main() {
    if (gl_FragCoord.x < 1.0) discard;
    vec2 d = vec2(dFdx(gl_FragCoord.x), dFdy(gl_FragCoord.y));
    outColor = gl_FrontFacing ? vec4(d, float(vId), fwidth(gl_FragCoord.z)) : vec4(0.0);
    gl_FragDepth = gl_FragCoord.z * 0.5;
}
