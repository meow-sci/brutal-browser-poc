#!/usr/bin/env bash
# exp-02-shader-xcompile: reproduce every experiment. Outputs go to out/ (logs in out/logs/).
#
# Optional environment:
#   NAGA_CLI=<path to naga binary>             (cargo install naga-cli --root <dir>)   -> CLI parity check
#   SWT=<path to spv_webgpu_transform binary>  (cargo install spirv-webgpu-transform)   -> alternative combined-sampler splitter
#   SLANGC26=<path to a newer slangc>          (slang-2026.19-macos-aarch64.tar.gz)     -> compare with /usr/local/bin/slangc 2025.17.2
#   KSA=1                                      -> also run ./ksa-survey.sh (reads ksa-linux/Content, writes to $TMPDIR, summary TSVs to out/)
set -u
cd "$(dirname "$0")"
export PATH="${VULKAN_SDK_BIN:-/Users/asherwin/VulkanSDK/1.4.328.1/macOS/bin}:$PATH"
NAGA_CLI=${NAGA_CLI:-}; SWT=${SWT:-}; SLANGC26=${SLANGC26:-}
[ -d node_modules/naga-wasm ] || npm install --no-audit --no-fund
mkdir -p out/logs out/dis out/wasm out/cli out/swt out/slang out/slang26 out/patched out/reflect out/probes
step() { echo; echo "### $*"; }

step "00 tool versions" | tee out/logs/00-versions.txt
{ glslc --version | head -3; spirv-val --version | head -1; spirv-opt --version | head -1; slangc -v 2>&1 | head -1
  [ -n "$SLANGC26" ] && echo "slangc26: $("$SLANGC26" -v 2>&1 | head -1)"
  [ -n "$NAGA_CLI" ] && echo "naga-cli: $("$NAGA_CLI" --version)"
  node --version; node -e "import('naga-wasm').then(m=>console.log('naga-wasm bundles naga', m.nagaVersion))"
  node -e "console.log('naga-wasm package', require('./node_modules/naga-wasm/package.json').version)"; } 2>&1 | tee -a out/logs/00-versions.txt

step "01 GLSL -> SPIR-V (glslc vulkan1.3 -O), validate, disassemble" | tee out/logs/01-glslc.txt
for s in mesh.vert mesh.frag mesh_combined.frag mesh_push.vert mesh_push.frag mesh_push_array.vert; do
  glslc --target-env=vulkan1.3 -O shaders/$s -o out/$s.spv && spirv-val --target-env vulkan1.3 out/$s.spv && echo "glslc+spirv-val OK  $s ($(stat -f%z out/$s.spv 2>/dev/null || stat -c%s out/$s.spv) bytes)"
  spirv-dis out/$s.spv > out/dis/$s.spvasm
done 2>&1 | tee -a out/logs/01-glslc.txt

step "02 SPIR-V -> WGSL with naga-wasm (node src/spv2wgsl.mjs)" | tee out/logs/02-naga-wasm.txt
for s in mesh.vert mesh.frag mesh_combined.frag mesh_push.vert mesh_push.frag mesh_push_array.vert; do node src/spv2wgsl.mjs out/$s.spv out/wasm/$s.wgsl; done 2>&1 | tee -a out/logs/02-naga-wasm.txt
node src/validate-wgsl.mjs out/wasm/mesh.vert.wgsl out/wasm/mesh.frag.wgsl out/wasm/mesh_push.vert.wgsl 2>&1 | tee -a out/logs/02-naga-wasm.txt

if [ -n "$NAGA_CLI" ]; then
  step "03 naga CLI parity (and --keep-coordinate-space)" | tee out/logs/03-naga-cli.txt
  for s in mesh.vert mesh.frag mesh_push.vert; do "$NAGA_CLI" out/$s.spv out/cli/$s.wgsl && cmp out/cli/$s.wgsl out/wasm/$s.wgsl && echo "naga-cli == naga-wasm for $s"; done 2>&1 | tee -a out/logs/03-naga-cli.txt
  "$NAGA_CLI" --keep-coordinate-space out/mesh.vert.spv out/cli/mesh.vert.keep-coordinate-space.wgsl
  diff out/cli/mesh.vert.wgsl out/cli/mesh.vert.keep-coordinate-space.wgsl 2>&1 | tee -a out/logs/03-naga-cli.txt   # shows the Y flip naga adds by default
fi

step "04 combined image sampler: naga rejects, spirv-opt split alone collides, spv-prepare (split + sampler binding +500) works" | tee out/logs/04-combined.txt
{ node src/spv2wgsl.mjs out/mesh_combined.frag.spv 2>&1 | head -3
  spirv-opt --split-combined-image-sampler out/mesh_combined.frag.spv -o out/mesh_combined.split-only.frag.spv
  node src/spv2wgsl.mjs out/mesh_combined.split-only.frag.spv 2>&1 | head -12
  node src/spv-prepare.mjs out/mesh_combined.frag.spv out/mesh_combined.prepared.frag.spv
  spirv-val --target-env vulkan1.3 out/mesh_combined.prepared.frag.spv && node src/spv2wgsl.mjs out/mesh_combined.prepared.frag.spv out/wasm/mesh_combined.prepared.frag.wgsl
  grep -n "@group\|@binding\|var " out/wasm/mesh_combined.prepared.frag.wgsl | head
  node src/spv-prepare.mjs out/mesh_push.frag.spv out/mesh_push.prepared.frag.spv && node src/spv2wgsl.mjs out/mesh_push.prepared.frag.spv out/wasm/mesh_push.prepared.frag.wgsl; } 2>&1 | tee -a out/logs/04-combined.txt
if [ -n "$SWT" ]; then
  step "04b spv_webgpu_transform combimg (alternative)" | tee -a out/logs/04-combined.txt
  "$SWT" combimg out/mesh_combined.frag.spv out/swt/mesh_combined.frag.spv 2>&1 | tee -a out/logs/04-combined.txt
  spirv-dis out/swt/mesh_combined.frag.spv | grep "Binding" | tee -a out/logs/04-combined.txt
fi

step "05 Slang -> SPIR-V and Slang -> WGSL (slangc 2025.17.2; optional 2026.19)" | tee out/logs/05-slang.txt
A=(-entry vsMain -stage vertex -entry fsMain -stage fragment)
slang_run() { # $1=compiler $2=outdir
  local C=$1 D=$2
  $C shaders/mesh.slang -target spirv -profile spirv_1_5 "${A[@]}" -o $D/mesh.slang.spv 2>&1 | head -5
  spirv-val --target-env vulkan1.3 $D/mesh.slang.spv && echo "$D mesh.slang.spv valid"
  spirv-dis $D/mesh.slang.spv | grep -E "OpEntryPoint|Binding|DescriptorSet|Location" | sed 's/^ *//' > $D/mesh.slang.spv.decorations.txt
  $C shaders/mesh.slang -target wgsl "${A[@]}" -o $D/mesh.slang.wgsl 2>&1 | head -5
  grep -n "@binding\|@group\|@location\|@vertex\|@fragment\|^fn" $D/mesh.slang.wgsl | cut -c1-110
  node src/validate-wgsl.mjs $D/mesh.slang.wgsl
  echo "-- Slang SPIR-V -> naga -> WGSL"; node src/spv2wgsl.mjs $D/mesh.slang.spv $D/mesh.slang.via-naga.wgsl; node src/validate-wgsl.mjs $D/mesh.slang.via-naga.wgsl
  echo "-- push constants + combined Sampler2D (shaders/mesh_push.slang)"
  $C shaders/mesh_push.slang -target spirv -profile spirv_1_5 "${A[@]}" -o $D/mesh_push.slang.spv 2>&1 | head -3
  $C shaders/mesh_push.slang -target wgsl "${A[@]}" -o $D/mesh_push.slang.wgsl 2>&1 | head -3
  grep -n "var<\|@binding\|@group" $D/mesh_push.slang.wgsl | cut -c1-110; node src/validate-wgsl.mjs $D/mesh_push.slang.wgsl
  echo "-- portable push-constant pattern (-DTARGET_WGSL -> reserved group 3)"
  $C shaders/mesh_push_portable.slang -target spirv -profile spirv_1_5 "${A[@]}" -o $D/mesh_push_portable.spv 2>&1 | head -3
  $C shaders/mesh_push_portable.slang -target wgsl -DTARGET_WGSL "${A[@]}" -o $D/mesh_push_portable.wgsl 2>&1 | head -3
  grep -n "@binding\|@group" $D/mesh_push_portable.wgsl | cut -c1-110; node src/validate-wgsl.mjs $D/mesh_push_portable.wgsl
  echo "-- register(): bindings, ParameterBlock, spec constant, SV_VertexID (probe_registers.slang)"
  $C shaders/probe_registers.slang -target spirv -profile spirv_1_5 "${A[@]}" -o $D/probe_registers.spv 2>&1 | grep -c warning
  $C shaders/probe_registers.slang -target wgsl "${A[@]}" -o $D/probe_registers.wgsl 2>&1 | grep -c warning
  grep -n "@binding\|@group\|override\|@interpolate" $D/probe_registers.wgsl | cut -c1-110; node src/validate-wgsl.mjs $D/probe_registers.wgsl
  echo "SV_VertexID => SPIR-V capability: $(spirv-dis $D/probe_registers.spv | grep -c DrawParameters) DrawParameters; naga:"; node src/spv2wgsl.mjs $D/probe_registers.spv 2>&1 | head -1
  echo "-- -fvk-b/t/s-shift (probe_shift.slang)"
  $C shaders/probe_shift.slang -target spirv -profile spirv_1_5 "${A[@]}" -fvk-b-shift 10 0 -fvk-t-shift 20 0 -fvk-s-shift 30 0 -o $D/probe_shift.spv 2>&1 | grep -v warning | grep -i error
  spirv-dis $D/probe_shift.spv | grep Binding
  $C shaders/probe_shift.slang -target wgsl "${A[@]}" -fvk-b-shift 10 0 -fvk-t-shift 20 0 -fvk-s-shift 30 0 -o $D/probe_shift.wgsl 2>&1 | grep -v warning | grep -i error
  grep -n "var" $D/probe_shift.wgsl | head -4 | cut -c1-110
  echo "-- single entry names"
  $C shaders/mesh.slang -target spirv -profile spirv_1_5 -entry vsMain -stage vertex -o $D/mesh.vs-only.spv; spirv-dis $D/mesh.vs-only.spv | grep OpEntryPoint | cut -c1-60
  $C shaders/mesh.slang -target spirv -profile spirv_1_5 -entry vsMain -stage vertex -fvk-use-entrypoint-name -o $D/mesh.vs-only.epname.spv; spirv-dis $D/mesh.vs-only.epname.spv | grep OpEntryPoint | cut -c1-60
  echo "-- -fvk-invert-y"
  $C shaders/mesh.slang -target spirv -profile spirv_1_5 -entry vsMain -stage vertex -fvk-invert-y -o $D/mesh.vs-inverty.spv; echo "OpFNegate count: $(spirv-dis $D/mesh.vs-inverty.spv | grep -c OpFNegate)"
  $C shaders/mesh.slang -target wgsl -entry vsMain -stage vertex -o $D/mesh.vs.wgsl; $C shaders/mesh.slang -target wgsl -entry vsMain -stage vertex -fvk-invert-y -o $D/mesh.vs-inverty.wgsl; cmp $D/mesh.vs.wgsl $D/mesh.vs-inverty.wgsl && echo "WGSL unaffected by -fvk-invert-y"
  echo "-- SV_VulkanVertexID / SV_VulkanInstanceID avoid the DrawParameters capability (naga-friendly)"
  $C shaders/probe_vulkan_ids.slang -target spirv -profile spirv_1_3 -entry vsMain -stage vertex -o $D/probe_vulkan_ids.spv 2>&1 | head -3
  echo "DrawParameters occurrences: $(spirv-dis $D/probe_vulkan_ids.spv | grep -c DrawParameters)"; node src/spv2wgsl.mjs $D/probe_vulkan_ids.spv $D/probe_vulkan_ids.via-naga.wgsl | head -1
  echo "-- matrix layout: default vs -matrix-layout-column-major"
  $C shaders/mesh.slang -target spirv -profile spirv_1_5 -entry vsMain -stage vertex -matrix-layout-column-major -o $D/mesh.vs-colmajor.spv; cmp $D/mesh.vs-only.spv $D/mesh.vs-colmajor.spv && echo "default == -matrix-layout-column-major (identical SPIR-V)"
  echo "-- profile spirv_1_3 output through naga"
  $C shaders/mesh.slang -target spirv -profile spirv_1_3 "${A[@]}" -o $D/mesh.spirv_1_3.spv 2>&1 | head -2; echo "$(spirv-dis $D/mesh.spirv_1_3.spv | grep -m1 Version)"; node src/spv2wgsl.mjs $D/mesh.spirv_1_3.spv | head -1
  echo "-- reflection json"; $C shaders/mesh.slang -target spirv -profile spirv_1_5 "${A[@]}" -reflection-json $D/mesh.reflection.json -o $D/mesh.refl.spv; wc -c $D/mesh.reflection.json
}
{ echo "=== slangc 2025.17.2 (/usr/local/bin/slangc)"; slang_run slangc out/slang
  [ -n "$SLANGC26" ] && { echo "=== slangc 2026.19"; slang_run "$SLANGC26" out/slang26; }; } 2>&1 | tee -a out/logs/05-slang.txt

step "06 WGSL patches (Y flip wrapper, push constants) + validation by naga's WGSL front end" | tee out/logs/06-patches.txt
{ [ -n "$NAGA_CLI" ] && "$NAGA_CLI" --keep-coordinate-space out/mesh.vert.spv out/patched/mesh.vert.noflip.wgsl \
    || { cp out/wasm/mesh.vert.wgsl out/patched/mesh.vert.noflip.wgsl; sed -i.bak -e '/global_6.member.y = -(_e11);/d' out/patched/mesh.vert.noflip.wgsl; rm -f out/patched/*.bak; }
  node src/patch-wgsl.mjs out/patched/mesh.vert.noflip.wgsl out/patched/mesh.vert.wrapper-flip.wgsl --y-flip
  node src/validate-wgsl.mjs out/patched/mesh.vert.noflip.wgsl out/patched/mesh.vert.wrapper-flip.wgsl
  src=out/slang/mesh.slang.wgsl; [ -f out/slang26/mesh.slang.wgsl ] && src=out/slang26/mesh.slang.wgsl
  node src/patch-wgsl.mjs $src out/patched/mesh.slang.flip.wgsl --y-flip && node src/validate-wgsl.mjs out/patched/mesh.slang.flip.wgsl
  for s in mesh_push.vert mesh_push.prepared.frag; do node src/patch-wgsl.mjs out/wasm/$s.wgsl out/patched/$s.push-uniform.wgsl --push uniform --push-group 3 && node src/validate-wgsl.mjs out/patched/$s.push-uniform.wgsl; done
  echo "--- push block with float[4]: uniform rejects (std140 stride 16), storage accepts (std430)"
  node src/patch-wgsl.mjs out/wasm/mesh_push_array.vert.wgsl out/patched/mesh_push_array.push-uniform.wgsl --push uniform; node src/validate-wgsl.mjs out/patched/mesh_push_array.push-uniform.wgsl
  node src/patch-wgsl.mjs out/wasm/mesh_push_array.vert.wgsl out/patched/mesh_push_array.push-storage.wgsl --push storage; node src/validate-wgsl.mjs out/patched/mesh_push_array.push-storage.wgsl
  echo "--- keep as immediates: add 'requires immediate_address_space;' (naga's own WGSL parser does not know the extension, so validate BEFORE adding it)"
  node src/patch-wgsl.mjs out/wasm/mesh_push.vert.wgsl out/patched/mesh_push.vert.immediates.wgsl --push keep; head -2 out/patched/mesh_push.vert.immediates.wgsl
  node src/validate-wgsl.mjs out/patched/mesh_push.vert.immediates.wgsl; } 2>&1 | tee -a out/logs/06-patches.txt

step "06b triangle (Vulkan clip space, visually CCW) -> WGSL with/without Y flip, for the render test in step 12" | tee out/logs/06b-tri.txt
{ mkdir -p out/tri; glslc --target-env=vulkan1.3 -O shaders/tri.vert -o out/tri/tri.vert.spv && spirv-val --target-env vulkan1.3 out/tri/tri.vert.spv && node src/spv2wgsl.mjs out/tri/tri.vert.spv out/tri/tri.vert.naga-flip.wgsl
  if [ -n "$NAGA_CLI" ]; then "$NAGA_CLI" --keep-coordinate-space out/tri/tri.vert.spv out/tri/tri.vert.noflip.wgsl && node src/patch-wgsl.mjs out/tri/tri.vert.noflip.wgsl out/tri/tri.vert.wrapper-flip.wgsl --y-flip
  else echo "NAGA_CLI not set: skipping the un-flipped and wrapper-flip variants (naga-wasm cannot disable the flip)"; fi; } 2>&1 | tee -a out/logs/06b-tri.txt

step "07 GLSL front end of naga (direct GLSL -> WGSL, no SPIR-V)" | tee out/logs/07-naga-glsl-frontend.txt
mkdir -p out/glsl-direct
for s in mesh.vert mesh.frag mesh_combined.frag mesh_push.vert; do node src/glsl2wgsl.mjs shaders/$s out/glsl-direct/$s.wgsl; done 2>&1 | head -40 | tee -a out/logs/07-naga-glsl-frontend.txt

step "08 feature probes (GLSL -> SPIR-V vulkan1.1 -> naga-wasm)" | tee out/logs/08-probes.txt
ENVV=vulkan1.1 ./run-probes.sh 2>&1 | tee -a out/logs/08-probes.txt

step "08b std140 layout hazard lint (naga drops ArrayStride/MatrixStride)" | tee out/logs/08b-layout-lint.txt
node src/spv-layout-lint.mjs out/mesh.vert.spv out/probes/uniform_layout.frag.spv out/probes/std140_layout.comp.spv out/probes/ubo_mat2.frag.spv 2>&1 | tee -a out/logs/08b-layout-lint.txt

step "08c Block / BufferBlock decorations (old-style Vulkan 1.0 SSBOs; UBO without Block)" | tee out/logs/08c-block.txt
{ glslc --target-env=vulkan1.0 -O shaders/probes/atomics.comp -o out/probes/atomics.vulkan1.0.spv
  echo "SPIR-V $(spirv-dis out/probes/atomics.vulkan1.0.spv | grep -m1 Version); BufferBlock decorations: $(spirv-dis out/probes/atomics.vulkan1.0.spv | grep -c BufferBlock)"
  node src/spv2wgsl.mjs out/probes/atomics.vulkan1.0.spv out/probes/atomics.vulkan1.0.wgsl; grep -n "var<storage" out/probes/atomics.vulkan1.0.wgsl
  spirv-dis out/mesh.frag.spv | grep -v "OpDecorate %_struct_19 Block" > out/probes/mesh.frag.noblock.spvasm
  spirv-as --target-env vulkan1.3 out/probes/mesh.frag.noblock.spvasm -o out/probes/mesh.frag.noblock.spv
  spirv-val --target-env vulkan1.3 out/probes/mesh.frag.noblock.spv 2>&1 | head -2
  node src/spv2wgsl.mjs out/probes/mesh.frag.noblock.spv; } 2>&1 | tee -a out/logs/08c-block.txt

step "09 SPIR-V version sensitivity: OpCopyLogical (SPIR-V >= 1.4)" | tee out/logs/09-copylogical.txt
for e in vulkan1.1 vulkan1.2 vulkan1.3; do glslc --target-env=$e -O shaders/probes/copylogical.frag -o out/probes/copylogical.$e.spv
  echo "$e: $(spirv-dis out/probes/copylogical.$e.spv | grep -m1 Version) CopyLogical=$(spirv-dis out/probes/copylogical.$e.spv | grep -c OpCopyLogical)"; node src/spv2wgsl.mjs out/probes/copylogical.$e.spv 2>&1 | head -2; done 2>&1 | tee -a out/logs/09-copylogical.txt

step "10 reflection: spirv-cross --reflect, spirv-reflect" | tee out/logs/10-reflect.txt
spirv-cross out/mesh.frag.spv --reflect > out/reflect/mesh.frag.spirv-cross.json 2>&1; spirv-cross out/mesh_push.vert.spv --reflect > out/reflect/mesh_push.vert.spirv-cross.json 2>&1
spirv-reflect out/mesh.frag.spv > out/reflect/mesh.frag.spirv-reflect.txt 2>&1
{ echo "spirv-cross --wgsl exits: $(spirv-cross out/mesh.frag.spv --wgsl >/dev/null 2>&1; echo $?) (prints usage: no WGSL backend)"; head -c 300 out/reflect/mesh.frag.spirv-cross.json; } 2>&1 | tee -a out/logs/10-reflect.txt

step "11 naga-wasm size and speed" | tee out/logs/11-bench.txt
W=node_modules/naga-wasm/dist/wasm/naga_bg.wasm
{ echo "raw bytes: $(stat -f%z $W 2>/dev/null || stat -c%s $W)"; echo "gzip -9 bytes: $(gzip -9 -c $W | wc -c)"
  node -e "const z=require('zlib');const b=require('fs').readFileSync('$W');console.log('brotli q11 bytes:',z.brotliCompressSync(b,{params:{[z.constants.BROTLI_PARAM_QUALITY]:11}}).length)"
  node src/bench.mjs 200 out/mesh.vert.spv out/mesh.frag.spv out/mesh_push.vert.spv; } 2>&1 | tee -a out/logs/11-bench.txt

step "11b build-time pipeline prototype (strategy A): glslc -> prepare -> lint -> naga -> patch -> manifest keyed by sha256(spv)" | tee out/logs/11b-build-shaders.txt
{ node src/build-shaders.mjs shaders out/web-shaders
  node -e "const m=require('./out/web-shaders/manifest.json'); const h=require('crypto').createHash('sha256').update(require('fs').readFileSync('out/web-shaders/mesh_combined.frag.spv')).digest('hex'); const e=m.shaders[h]; console.log('runtime lookup sha256', h.slice(0,16)+'...', '->', e.wgsl, JSON.stringify(e.bindings.map(b=>b.group+':'+b.binding+':'+b.type.split('<')[0])))"; } 2>&1 | tee -a out/logs/11b-build-shaders.txt

if [ "${BROWSER_CHECK:-0}" = 1 ]; then
  step "12 Tint validation + render/winding test in headless Edge/Chrome (run-browser-check.sh)" | tee out/logs/12-browser-check.txt
  ./run-browser-check.sh 2>&1 | tee -a out/logs/12-browser-check.txt
  step "12b naga-wasm running inside the browser (strategy B demo: fetch wasm, SPIR-V -> WGSL -> Tint)" | tee out/logs/12b-browser-naga.txt
  ./run-browser-naga.sh 2>&1 | tee -a out/logs/12b-browser-naga.txt
fi

if [ "${KSA:-0}" = 1 ]; then
  step "13 KSA portability survey (reads ksa-linux/Content; outputs in KSA_OUT, summaries in out/)" | tee out/logs/13-ksa-survey.txt
  export KSA_OUT=${KSA_OUT:-${TMPDIR:-/tmp}/exp-02-ksa}
  for e in vulkan1.3 vulkan1.1; do for p in 0 1; do ENVV=$e PREPARE=$p ./ksa-survey.sh; done; done 2>&1 | tee -a out/logs/13-ksa-survey.txt
  ./ksa-named.sh 2>&1 | tee out/logs/13-ksa-named.txt
  node src/pushsize.mjs "$KSA_OUT/vulkan1.1" 2>&1 | tee out/logs/13-ksa-pushsize.txt
  node src/spv-layout-lint.mjs "$KSA_OUT"/vulkan1.1/*.spv 2>&1 | sed "s#$KSA_OUT/vulkan1.1/##" > "$KSA_OUT/layout-lint.txt"; echo "KSA layout-hazard modules: $(grep -c LAYOUT-HAZARD "$KSA_OUT/layout-lint.txt") of $(ls "$KSA_OUT"/vulkan1.1/*.spv | wc -l)" | tee out/logs/13-ksa-layout-lint.txt
  node src/bench-all.mjs out/ksa-summary-vulkan1.1-prepared.tsv "$KSA_OUT/vulkan1.1-prepared" 2>&1 | tee out/logs/13-ksa-bench-all.txt
  [ "${BROWSER_CHECK:-0}" = 1 ] && ./run-ksa-browser-check.sh 2>&1 | tee out/logs/13-ksa-tint.txt
fi
echo; echo "done. See out/logs/*.txt"
