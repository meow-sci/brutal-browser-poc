#!/usr/bin/env bash
# Tint (headless Edge/Chrome) validation of the WGSL that ksa-survey.sh produced (KSA_OUT/<env>-prepared/*.wgsl). Needs a WebGPU browser.
set -u; cd "$(dirname "$0")"
KSA_OUT=${KSA_OUT:-${TMPDIR:-/tmp}/exp-02-ksa}; DIR=${1:-$KSA_OUT/vulkan1.1-prepared}
BROWSER=${BROWSER:-"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"}; PORT=${PORT:-8766}
mkdir -p out/logs; HTML=$KSA_OUT/ksa-browser-check.html; RES=$KSA_OUT/ksa-tint-result.json; rm -f "$RES"
node src/browser-check-ksa.mjs "$DIR" "$HTML"
node src/browser-check-server.mjs $PORT "$HTML" "$RES" & SRV=$!; sleep 1
PROFILE=$(mktemp -d)
"$BROWSER" --headless=new --user-data-dir="$PROFILE" --no-first-run --no-default-browser-check --enable-unsafe-webgpu --enable-features=WebGPU,Vulkan --use-angle=metal \
  "http://127.0.0.1:$PORT/" >/dev/null 2>&1 & BR=$!
wait $SRV; kill $BR 2>/dev/null; sleep 1; pkill -f "user-data-dir=$PROFILE" 2>/dev/null
node -e "
const r=JSON.parse(require('fs').readFileSync('$RES','utf8')); if(r.error) console.log('ERROR',r.error);
let raw=0,pat=0,n=0; const msgs={};
for (const [k,v] of Object.entries(r.shaders)) { n++; if(v.raw==='OK') raw++; if(v.patched==='OK') pat++; else { const m=String(v.patched[0]).replace(/'[^']*'/g,\"'X'\").replace(/\d+/g,'N').slice(0,90); (msgs[m]??=[]).push(k);} }
console.log('shaders:',n,'Tint OK raw:',raw,'Tint OK with patches (diagnostic off derivative_uniformity, enable subgroups, strip @coherent):',pat);
for (const [m,ks] of Object.entries(msgs).sort((a,b)=>b[1].length-a[1].length)) console.log(String(ks.length).padStart(4), m);
require('fs').writeFileSync('out/ksa-tint-summary.json', JSON.stringify(Object.fromEntries(Object.entries(r.shaders).map(([k,v])=>[k.replace(/\.wgsl\$/,''),{raw:v.raw==='OK'?'OK':v.raw[0],patched:v.patched==='OK'?'OK':v.patched[0]}])),null,1));
"
