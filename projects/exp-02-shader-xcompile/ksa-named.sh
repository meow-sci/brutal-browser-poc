#!/usr/bin/env bash
# Run a handful of named KSA shaders through glslc -> (optional spv-prepare) -> naga-wasm and print the exact outcome/message.
# Outputs stay under $KSA_OUT (outside the project). Usage: ./ksa-named.sh  (env: KSA_OUT, KSA_CONTENT)
set -u; cd "$(dirname "$0")"
export PATH="/Users/asherwin/VulkanSDK/1.4.328.1/macOS/bin:$PATH"
C=${KSA_CONTENT:-/Users/asherwin/repos/meow-sci/ksa-linux/Content}; OUT=${KSA_OUT:-${TMPDIR:-/tmp}/exp-02-ksa}/named; mkdir -p "$OUT"
for n in FullScreenTriangle.vert Copy.frag Line.vert Line.frag Grid.vert Grid.frag GenericMesh.vert GenericMesh.frag; do
  f=$C/Core/Shaders/$n
  for v in "vulkan1.3 0" "vulkan1.1 0" "vulkan1.1 1"; do set -- $v; env=$1; prep=$2; key=$n.$env.$prep
    if ! glslc --target-env=$env -O -I "$C/Core/Shaders" -I "$C" "$f" -o "$OUT/$key.spv" 2> "$OUT/$key.err"; then printf "%-24s %-9s prepare=%s  GLSLC_FAIL %s\n" $n $env $prep "$(grep -m1 error "$OUT/$key.err" | cut -c1-100)"; continue; fi
    [ "$prep" = 1 ] && node src/spv-prepare.mjs "$OUT/$key.spv" "$OUT/$key.spv" >/dev/null 2>&1
    r=$(node src/spv2wgsl.mjs "$OUT/$key.spv" "$OUT/$key.wgsl" 2>&1 | grep -v '^$' | head -2 | tr '\n' ' ' | sed -E "s#$OUT/##g; s#[A-Za-z0-9_.]*\.spv ##" | cut -c1-170)
    printf "%-24s %-9s prepare=%s  %s\n" $n $env $prep "$r"
  done
done
