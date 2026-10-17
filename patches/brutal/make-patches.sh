#!/usr/bin/env bash
# Regenerates the unified diffs from the patched sources under src/Brutal.Vulkan.Browser/Patched/ against the pinned
# BRUTAL tree (read-only), then verifies them with `git apply --check`.
#   usage: patches/brutal/make-patches.sh [BRUTAL_ROOT]      (default: $BRUTAL_SRC or ../BRUTAL)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
brutal="${1:-${BRUTAL_SRC:-$repo/../BRUTAL}}"
patched="$repo/src/Brutal.Vulkan.Browser/Patched"

mk() { # <relative path inside Brutal.Vulkan> <output patch>
  local rel="$1" out="$2"
  diff -u --label "a/Brutal.Vulkan/$rel" --label "b/Brutal.Vulkan/$rel" "$brutal/Brutal.Vulkan/$rel" "$patched/$rel" > "$out" || [ $? -eq 1 ]
}
mk ImportResolver.cs "$here/0001-pluggable-loader-import-resolver.patch"
mk Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs "$here/0002-pluggable-loader-global-commands.patch"
cat "$here/0001-pluggable-loader-import-resolver.patch" "$here/0002-pluggable-loader-global-commands.patch" > "$here/brutal-vulkan-pluggable-loader.patch"
for p in "$here"/*.patch; do git -C "$brutal" apply --check "$p" && echo "git apply --check OK: $(basename "$p")"; done
