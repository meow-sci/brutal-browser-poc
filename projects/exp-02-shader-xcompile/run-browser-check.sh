#!/usr/bin/env bash
# Headless Chromium-family browser validation of the generated WGSL by Tint. Needs Edge/Chrome >= 149 for var<immediate>.
set -u; cd "$(dirname "$0")"
BROWSER=${BROWSER:-"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"}
PORT=${PORT:-8765}
mkdir -p out/logs; rm -f out/browser-check.result.json
node src/browser-check.mjs
node src/browser-check-server.mjs $PORT & SRV=$!
sleep 1
PROFILE=$(mktemp -d)
"$BROWSER" --headless=new --user-data-dir="$PROFILE" --no-first-run --no-default-browser-check --enable-unsafe-webgpu --enable-features=WebGPU,Vulkan --use-angle=metal --enable-logging=stderr --v=0 \
  "http://127.0.0.1:$PORT/" >out/logs/browser-check.stdout.txt 2>out/logs/browser-check.stderr.txt & BR=$!
wait $SRV; kill $BR 2>/dev/null; sleep 1; pkill -f "user-data-dir=$PROFILE" 2>/dev/null
[ -f out/browser-check.result.json ] && cat out/browser-check.result.json || { echo "no result (see out/logs/browser-check.stderr.txt)"; tail -5 out/logs/browser-check.stderr.txt; }
