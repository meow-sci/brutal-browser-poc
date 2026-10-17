#version 450
// Whole-struct copy from a UBO into a local: glslang emits OpCopyLogical for SPIR-V >= 1.4 (naga: unsupported instruction).
struct Light { vec4 pos; vec4 color; float range; };
layout(set = 0, binding = 0) uniform U { Light light; Light lights[2]; } u;
layout(location = 0) out vec4 o;
void main() { Light l = u.light; Light l2 = u.lights[1]; o = l.pos * l.range + l2.color; }
