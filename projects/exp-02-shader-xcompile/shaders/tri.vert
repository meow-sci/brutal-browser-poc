#version 450
// Vulkan-convention clip space (Y down). On a Vulkan screen this triangle is top-left -> bottom-left -> bottom-right,
// i.e. visually COUNTER-CLOCKWISE (front-facing for VK_FRONT_FACE_COUNTER_CLOCKWISE) and lies in the left half of the image.
const vec2 pos[3] = vec2[3](vec2(-0.8, -0.8), vec2(-0.8, 0.8), vec2(0.0, 0.8));
void main() { gl_Position = vec4(pos[gl_VertexIndex], 0.0, 1.0); }
