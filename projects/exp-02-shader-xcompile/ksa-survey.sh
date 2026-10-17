#!/usr/bin/env bash
# Characterize the portability gap of the KSA reference game's GLSL (read-only; nothing is copied into this project).
# Pipeline per shader:  glslc (-I Core/Shaders, Content)  ->  [optional spv-prepare.mjs]  ->  naga-wasm SPIR-V->WGSL
# Usage: ENVV=vulkan1.1 PREPARE=1 KSA_OUT=/some/dir ./ksa-survey.sh
set -u
cd "$(dirname "$0")"; HERE=$PWD
export PATH="/Users/asherwin/VulkanSDK/1.4.328.1/macOS/bin:$PATH"
C=${KSA_CONTENT:-/Users/asherwin/repos/meow-sci/ksa-linux/Content}
ENVV=${ENVV:-vulkan1.1}; PREPARE=${PREPARE:-0}
OUT=${KSA_OUT:-${TMPDIR:-/tmp}/exp-02-ksa}/$ENVV$([ "$PREPARE" = 1 ] && echo -prepared); rm -rf "$OUT"; mkdir -p "$OUT"; : > "$OUT/results.tsv"
find "$C" \( -name '*.vert' -o -name '*.frag' -o -name '*.comp' -o -name '*.geom' -o -name '*.tesc' -o -name '*.tese' -o -name '*.rgen' -o -name '*.rchit' -o -name '*.rmiss' -o -name '*.rahit' \) | sort | while read -r f; do
  rel=${f#$C/}; key=$(echo "$rel" | tr '/' '_')
  if ! glslc --target-env=$ENVV -O -I "$C/Core/Shaders" -I "$C" -I "$C/Core" -I "$(dirname "$f")" "$f" -o "$OUT/$key.spv" 2> "$OUT/$key.glslc.err"; then
    printf "%s\tGLSLC_FAIL\t%s\n" "$rel" "$(grep -m1 -i 'error' "$OUT/$key.glslc.err" | sed 's/.*error: //' | cut -c1-140)" >> "$OUT/results.tsv"; continue; fi
  [ "$PREPARE" = 1 ] && node src/spv-prepare.mjs "$OUT/$key.spv" "$OUT/$key.spv" > /dev/null 2>&1
  msg=$(node src/spv2wgsl.mjs "$OUT/$key.spv" "$OUT/$key.wgsl" 2>&1 | grep -v '^$' | head -2 | tr '\n' ' ' | sed -E 's#FAIL [^ ]*\.spv ##' | cut -c1-200)
  case "$msg" in OK*) printf "%s\tOK\t\n" "$rel" >> "$OUT/results.tsv" ;; *) printf "%s\tNAGA_FAIL\t%s\n" "$rel" "$msg" >> "$OUT/results.tsv" ;; esac
done
echo "== $ENVV prepare=$PREPARE  ($OUT)"; cut -f2 "$OUT/results.tsv" | sort | uniq -c
mkdir -p out; cp "$OUT/results.tsv" "out/ksa-summary-$ENVV$([ "$PREPARE" = 1 ] && echo -prepared).tsv"   # names + status only, no shader source
