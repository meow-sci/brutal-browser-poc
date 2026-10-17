#!/usr/bin/env bash
# Runs naga-wasm inside headless Edge/Chrome (strategy B: runtime SPIR-V -> WGSL in the browser). Result: out/browser-naga.result.json
set -u; cd "$(dirname "$0")"
BROWSER=${BROWSER:-"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"}; PORT=${PORT:-8767}
mkdir -p out/logs; rm -f out/browser-naga.result.json; node src/browser-naga.mjs
node src/browser-check-server.mjs $PORT out/browser-naga.html out/browser-naga.result.json & SRV=$!; sleep 1
PROFILE=$(mktemp -d)
"$BROWSER" --headless=new --user-data-dir="$PROFILE" --no-first-run --no-default-browser-check --enable-unsafe-webgpu --enable-features=WebGPU,Vulkan --use-angle=metal "http://127.0.0.1:$PORT/" >/dev/null 2>&1 & BR=$!
wait $SRV; kill $BR 2>/dev/null; sleep 1; pkill -f "user-data-dir=$PROFILE" 2>/dev/null
cat out/browser-naga.result.json 2>/dev/null || echo "no result"
