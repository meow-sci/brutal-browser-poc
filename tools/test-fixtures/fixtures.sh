#!/usr/bin/env bash
# Pipeline test: compile the GLSL fixtures with glslc exactly like tools/Shaders.targets does, run tools/build-shaders.mjs on the
# resulting .spv files and assert the outcome (see assert-manifest.mjs for the manifest checks).
#   npm test                    full run including the headless-browser (Tint) check
#   SKIP_BROWSER=1 npm test     skip the browser check (hosts without WebGPU)
#   KEEP_OUT=1 npm test         keep the work directory (printed at the end)
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tools="$(cd "$here/.." && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/brutal-shader-fixtures.XXXXXX")"
cleanup() { if [ -n "${KEEP_OUT:-}" ]; then echo "work directory kept: $work"; else rm -rf "$work"; fi; }
trap cleanup EXIT

# glslc: $GLSLC, PATH, or a Vulkan SDK install
glslc="${GLSLC:-}"
if [ -z "$glslc" ]; then
  if command -v glslc >/dev/null 2>&1; then glslc=glslc
  else glslc="$(ls -d "$HOME"/VulkanSDK/*/macOS/bin/glslc /usr/local/bin/glslc 2>/dev/null | tail -n 1 || true)"; fi
fi
[ -n "$glslc" ] || { echo "glslc not found (set GLSLC or install the Vulkan SDK)" >&2; exit 2; }

browser_flag="--no-browser-check"
[ -z "${SKIP_BROWSER:-}" ] && browser_flag=""

pass() { echo "PASS $*"; }
fail() { echo "FAIL $*" >&2; exit 1; }
compile() { # <outdir> <fixture>...   same flags as Shaders.targets
  local out="$1"; shift; mkdir -p "$out"
  for f in "$@"; do "$glslc" --target-env=vulkan1.1 -O -I "$here" -o "$out/$f.spv" "$here/$f"; done
}

echo "== glslc: $("$glslc" --version | head -n 1)"

# ---- 1. shaders that must translate -------------------------------------------------------------------------------
good=(mesh.vert mesh.frag combined.frag push.vert push_array.vert writeonly.comp spec.frag)
compile "$work/good" "${good[@]}"
echo "== build-shaders: ${#good[@]} valid fixtures"
# shellcheck disable=SC2086
node "$tools/build-shaders.mjs" --spv-dir "$work/good" --out "$work/out" --glslc "$glslc" $browser_flag
node "$here/assert-manifest.mjs" "$work/out/manifest.json" "$work/good"
pass "valid fixtures translate; manifest matches shasum, bindings and push conventions"

# ---- 2. shaders that must be rejected -------------------------------------------------------------------------------
expect_failure() { # <fixture> <expected message substring>
  local name="$1" needle="$2" log="$work/$1.log"
  compile "$work/bad-$name" "$name"
  if node "$tools/build-shaders.mjs" --spv-dir "$work/bad-$name" --out "$work/out-bad-$name" --glslc "$glslc" --no-browser-check >"$log" 2>&1; then
    cat "$log"; fail "$name was accepted but must fail"
  fi
  grep -q "FAIL $name" "$log" && grep -q "$needle" "$log" || { cat "$log"; fail "$name failed, but not with '$needle' naming the shader"; }
  [ ! -e "$work/out-bad-$name/manifest.json" ] || fail "$name: a manifest was written despite the failure"
  pass "$name is rejected ($(grep "FAIL $name" "$log" | head -n 1 | cut -c1-110))"
}
expect_failure bad_layout.frag "layout-lint"
expect_failure bad_push.vert "push-constants"

# the stand-alone lint CLI agrees (exit code 3 on a hazard)
set +e; node "$tools/spv-layout-lint.mjs" "$work/bad-bad_layout.frag/bad_layout.frag.spv" >/dev/null; rc=$?; set -e
[ "$rc" -eq 3 ] || fail "spv-layout-lint CLI exit code is $rc, expected 3"
pass "spv-layout-lint CLI exits 3 on std140 float[4]"

# ---- 3. the browser check itself must be able to fail ---------------------------------------------------------------
if [ -z "${SKIP_BROWSER:-}" ]; then
  node -e '
    const fs = require("fs"); const [src, dst] = process.argv.slice(1);
    const m = JSON.parse(fs.readFileSync(src, "utf8"));
    const k = Object.keys(m.shaders)[0]; m.shaders[k].wgsl = m.shaders[k].wgsl.replace("fn main", "fn main_ (");
    fs.writeFileSync(dst, JSON.stringify(m));' "$work/out/manifest.json" "$work/broken-manifest.json"
  if node "$tools/check-wgsl.mjs" "$work/broken-manifest.json" >"$work/broken.log" 2>&1; then cat "$work/broken.log"; fail "check-wgsl accepted broken WGSL"; fi
  pass "check-wgsl reports an error for broken WGSL"
fi

echo "ALL FIXTURE TESTS PASSED"
