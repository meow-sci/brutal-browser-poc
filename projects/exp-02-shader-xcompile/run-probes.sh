#!/usr/bin/env bash
# Feature probes: GLSL -> SPIR-V (glslc) -> WGSL (naga-wasm). Writes out/probes/results.tsv
set -u
cd "$(dirname "$0")"
export PATH="/Users/asherwin/VulkanSDK/1.4.328.1/macOS/bin:$PATH"
ENVV=${ENVV:-vulkan1.1}
mkdir -p out/probes; : > out/probes/results.tsv
for f in shaders/probes/*.vert shaders/probes/*.frag shaders/probes/*.comp; do
  n=$(basename "$f")
  if ! glslc --target-env=$ENVV -O "$f" -o out/probes/$n.spv 2> out/probes/$n.glslc.err; then
    printf "%s\tGLSLC_FAIL\t%s\n" "$n" "$(grep -m1 error out/probes/$n.glslc.err | sed 's/.*error: //' | cut -c1-120)" >> out/probes/results.tsv; continue; fi
  out=$(node src/spv2wgsl.mjs out/probes/$n.spv out/probes/$n.wgsl 2>&1 | grep -v '^$' | head -2 | tr '\n' ' ' | cut -c1-170)
  case "$out" in OK*) printf "%s\tOK\t\n" "$n" >> out/probes/results.tsv ;; *) printf "%s\tNAGA_FAIL\t%s\n" "$n" "$out" >> out/probes/results.tsv ;; esac
done
column -t -s$'\t' out/probes/results.tsv
