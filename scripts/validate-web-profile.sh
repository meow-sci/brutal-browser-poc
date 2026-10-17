#!/usr/bin/env bash
#
# validate-web-profile.sh -- run a BRUTAL desktop app against the BRUTAL web Vulkan profile.
#
# Runs a command (normally a BRUTAL desktop app) with two Khronos layers injected through the
# Vulkan loader environment:
#
#     app -> VK_LAYER_KHRONOS_validation -> VK_LAYER_KHRONOS_profiles -> driver (system ICD)
#
# The profiles layer sits nearest the driver and makes the real GPU look like the device the
# BRUTAL Vulkan-on-WebGPU ICD exposes (profiles/VP_BRUTAL_web.json: Vulkan 1.3, extension list,
# features, limits, formats, one queue family, VK_KHR_portability_subset booleans).  The
# validation layer sits above it and therefore validates the app against that simulated device,
# so anything beyond the profile (a 5th descriptor set, a 3-component vertex format, a disabled
# feature, ...) is reported as a VUID.  VK_ICD_FILENAMES is deliberately left alone: the system
# install of the ICD (MoltenVK, a desktop driver, ...) is the device being simulated.
#
# Usage:
#   scripts/validate-web-profile.sh [options] -- <command...>
#
# Options:
#   --profile NAME      VP_BRUTAL_web_poc_2026 (default), VP_BRUTAL_web_game_2026 or
#                       VP_BRUTAL_web_game_max_2026 (game tier with every optional capability)
#   --profile-dir DIR   directory containing VP_BRUTAL_web.json (default: <repo>/profiles)
#   --log FILE          write the combined stdout+stderr log here (default: a mktemp file)
#   --sync              also enable synchronization validation
#   --best-practices    also enable best-practices validation
#   --fail-on-error     set the profiles layer's debug_fail_on_error (vkEnumeratePhysicalDevices then
#                       fails on a layer error).  Off by default: with the 1.4.328 layer it also
#                       fails (VK_ERROR_INITIALIZATION_FAILED) for a perfectly valid profile, and
#                       this script already fails the run on any "PROFILES ERROR" line.
#   --ignore REGEX      drop log lines matching this extended regex before judging the run
#                       (repeatable; e.g. --ignore 'VUID-vkDestroyDevice-device-05137')
#   --no-pty            run the command directly instead of on a pseudo-terminal (see "Output handling")
#   --list              list the profiles in the JSON file and exit
#   -h, --help          show this help
#
# Exit status:
#   0  command exited 0, no validation error / VUID, profiles layer loaded the profile cleanly
#   1  validation errors or VUIDs were reported (summary lists the distinct IDs)
#   2  usage or setup error (bad option, missing profile, python3 / layers not found)
#   3  the profiles layer failed (PROFILES ERROR, or it never applied the profile -- e.g. the
#      command did not create a Vulkan instance)
#   4  the command itself exited non-zero but no layer finding explains it
#   (3 takes precedence over 1, 1 over 4.)
#
# Output handling: the validation and profiles layers log with plain stdio (stdout), which is
# block-buffered when stdout is a pipe -- a command that crashes (e.g. SIGSEGV through a NULL
# Vulkan entry point) would take the unflushed validation messages with it.  So the command runs
# on a pseudo-terminal (python3 pty; line-buffered, stdout and stderr merged, stdin not forwarded;
# --no-pty to disable).  The merged stream is shown live and saved to the log file, which is what
# the script scans.  The validation layer reports each message ID at most 10 times (its duplicate
# limit), so counts are lower bounds.
#
# The profiles layer prints "PROFILES WARNING" for every profile value that differs from what the
# host device (or an earlier capability block) provides: D24 depth formats "simulating unsupported
# features" on Apple GPUs, "bufferImageGranularity profile value (1) is lesser than device (16)",
# or "maxPushConstantsSize profile value (128) is greater than device (64)" when the game tier
# raises a PoC limit.  They are informational and never fail the run; only "PROFILES ERROR" does.
#
# The 15 VK_KHR_portability_subset booleans and minVertexInputBindingStrideAlignment are read from
# the profile JSON (merged in capability order) and fed to the layer through its emulate_portability
# settings, so the result is the same on devices with and without native portability-subset support.
#
# Vulkan version: the profiles report apiVersion 1.3.0, the (non-conformant) version the ICD
# advertises.  BRUTAL calls the Vulkan 1.3 core entry points (vkCmdBeginRendering,
# vkCmdPipelineBarrier2, vkQueueSubmit2, ...) and the layers resolve those names only when the
# simulated device is 1.3: under a 1.2 profile vkGetDeviceProcAddr returns NULL for them and a
# caller using unchecked function pointers crashes with SIGSEGV (status 139) and no VUID.  Keep
# api-version at the ICD's version.
#
# WHAT THE LAYERS CANNOT SEE.  Khronos validation only knows Vulkan.  Rules that exist only
# because the target is WebGPU are invisible here and must be checked elsewhere (a debug-only
# WebProfileLint in BRUTAL's wrapper layer, offline SPIR-V reflection in the shader build):
#   - 256-byte alignment of bufferRowLength * texel size in buffer<->image copies (the ICD
#     repacks, slowly, when it is not),
#   - 4-byte alignment of buffer copy offsets/sizes, vertex-buffer bind offsets and strides,
#   - attachments with storeOp = DONT_CARE that are read later (WebGPU "discard" reads zero),
#   - the 32 bytes/sample colour-attachment budget (maxColorAttachmentBytesPerSample),
#   - combined image samplers and descriptor arrays in SPIR-V (PoC tier),
#   - mixing Y conventions, negative-height viewports, blocking fence waits on readbacks.
#
# macOS note: bash and env drop DYLD_* variables (SIP).  If DYLD_LIBRARY_PATH is empty the script
# restores it from $VALIDATE_WEB_PROFILE_DYLD_LIBRARY_PATH, $VULKAN_SDK/lib or ~/VulkanSDK/*/macOS/lib.
#
# Requires: bash, python3, the Vulkan SDK / loader with the Khronos profiles and validation
# layers installed (e.g. /usr/local/share/vulkan/explicit_layer.d), and vulkaninfo on PATH
# (optional, used for a layer-presence pre-flight).

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "$SCRIPT_DIR/.." && pwd)

PROFILE=VP_BRUTAL_web_poc_2026
PROFILE_DIR="$REPO_ROOT/profiles"
PROFILE_FILE_NAME=VP_BRUTAL_web.json
LOG=""
SYNC=false
BEST_PRACTICES=false
FAIL_ON_ERROR=false
USE_PTY=true
LIST_ONLY=false
IGNORES=()

usage() { sed -n '2,/^$/{s/^# \{0,1\}//;p;}' "${BASH_SOURCE[0]}" | sed '1d'; }
die() { echo "validate-web-profile: $*" >&2; exit 2; }

while [ $# -gt 0 ]; do
  case "$1" in
    --profile)      [ $# -ge 2 ] || die "--profile needs a value"; PROFILE=$2; shift 2 ;;
    --profile-dir)  [ $# -ge 2 ] || die "--profile-dir needs a value"; PROFILE_DIR=$2; shift 2 ;;
    --log)          [ $# -ge 2 ] || die "--log needs a value"; LOG=$2; shift 2 ;;
    --ignore)       [ $# -ge 2 ] || die "--ignore needs a value"; IGNORES+=("$2"); shift 2 ;;
    --sync)         SYNC=true; shift ;;
    --fail-on-error) FAIL_ON_ERROR=true; shift ;;
    --no-pty)       USE_PTY=false; shift ;;
    --best-practices) BEST_PRACTICES=true; shift ;;
    --list)         LIST_ONLY=true; shift ;;
    -h|--help)      usage; exit 0 ;;
    --)             shift; break ;;
    *)              die "unknown option '$1' (commands go after '--'; see --help)" ;;
  esac
done

command -v python3 >/dev/null 2>&1 || die "python3 is required (reads the profile JSON)"
PROFILE_DIR=$(cd "$PROFILE_DIR" 2>/dev/null && pwd) || die "profile directory not found"
PROFILE_JSON="$PROFILE_DIR/$PROFILE_FILE_NAME"
[ -f "$PROFILE_JSON" ] || die "profile file not found: $PROFILE_JSON"

# ---------------------------------------------------------------------------------------------
# Read the profile: print "INFO<TAB>key<TAB>value" and "ENV NAME=VALUE" lines.  Portability booleans
# come from the capability blocks in the profile's order (later blocks win, like the layer itself).
# ---------------------------------------------------------------------------------------------
read_profile() {
  python3 -I - "$PROFILE_JSON" "$PROFILE" <<'PY'
import json, sys

path, name = sys.argv[1], sys.argv[2]
try:
    with open(path) as fh:
        doc = json.load(fh)
except ValueError as e:
    sys.stderr.write("%s is not valid JSON: %s\n" % (path, e))
    sys.exit(2)
profiles = doc.get("profiles", {})
if name == "--list":
    for n, p in sorted(profiles.items()):
        print("LIST %s  (api %s, capabilities: %s%s)" % (
            n, p.get("api-version"), ", ".join(p.get("capabilities", [])),
            "; optionals: %d" % len(p["optionals"]) if p.get("optionals") else ""))
    sys.exit(0)
if name not in profiles:
    sys.stderr.write("profile '%s' not found in %s (available: %s)\n" % (name, path, ", ".join(sorted(profiles))))
    sys.exit(2)
prof = profiles[name]
caps = []
def flat(items):
    for i in items:
        flat(i) if isinstance(i, list) else caps.append(i)
flat(prof.get("capabilities", []))
members = ["constantAlphaColorBlendFactors", "events", "imageViewFormatReinterpretation",
           "imageViewFormatSwizzle", "imageView2DOn3DImage", "multisampleArrayImage",
           "mutableComparisonSamplers", "pointPolygons", "samplerMipLodBias",
           "separateStencilMaskRef", "shaderSampleRateInterpolationFunctions",
           "tessellationIsolines", "tessellationPointMode", "triangleFans",
           "vertexAttributeAccessBeyondStride"]
feat, prop = {}, {}
for c in caps:
    if c not in doc.get("capabilities", {}):
        sys.stderr.write("profile '%s' references unknown capability block '%s' in %s\n" % (name, c, path))
        sys.exit(2)
    block = doc["capabilities"][c]
    feat.update(block.get("features", {}).get("VkPhysicalDevicePortabilitySubsetFeaturesKHR", {}))
    prop.update(block.get("properties", {}).get("VkPhysicalDevicePortabilitySubsetPropertiesKHR", {}))
print("INFO\tprofile\t%s - %s" % (name, prof.get("label", "")))
print("INFO\tapi-version\t%s (status %s)" % (prof.get("api-version"), prof.get("status", "-")))
print("INFO\tcapabilities\t%s" % ", ".join(caps))
for m in members:
    print("ENV VK_KHRONOS_PROFILES_%s=%s" % (m.upper(), "true" if feat.get(m, False) else "false"))
print("ENV VK_KHRONOS_PROFILES_MINVERTEXINPUTBINDINGSTRIDEALIGNMENT=%d" % prop.get("minVertexInputBindingStrideAlignment", 4))
PY
}

if $LIST_ONLY; then
  PROFILE=--list
  list_out=$(read_profile) || exit 2
  echo "$list_out" | sed -n 's/^LIST //p'
  exit 0
fi

[ $# -gt 0 ] || die "no command given; usage: $(basename "$0") [options] -- <command...>"
CMD=("$@")

profile_info=$(read_profile) || exit 2

# ---------------------------------------------------------------------------------------------
# Layer configuration (names verified against the VK_LAYER_KHRONOS_profiles 1.4.328 build:
# env var = VK_KHRONOS_PROFILES_<SETTING> / VK_KHRONOS_VALIDATION_<SETTING>, upper case).
# ---------------------------------------------------------------------------------------------
LAYERS="VK_LAYER_KHRONOS_validation:VK_LAYER_KHRONOS_profiles"   # first = nearest the app
SIMULATE="SIMULATE_API_VERSION_BIT,SIMULATE_FEATURES_BIT,SIMULATE_PROPERTIES_BIT,SIMULATE_EXTENSIONS_BIT,SIMULATE_FORMATS_BIT,SIMULATE_QUEUE_FAMILY_PROPERTIES_BIT"

CFG=(
  "VK_INSTANCE_LAYERS=$LAYERS"
  "VK_KHRONOS_PROFILES_PROFILE_EMULATION=true"
  "VK_KHRONOS_PROFILES_PROFILE_DIRS=$PROFILE_DIR"
  "VK_KHRONOS_PROFILES_PROFILE_NAME=$PROFILE"
  "VK_KHRONOS_PROFILES_PROFILE_VALIDATION=true"
  "VK_KHRONOS_PROFILES_SIMULATE_CAPABILITIES=$SIMULATE"
  "VK_KHRONOS_PROFILES_EMULATE_PORTABILITY=true"
  "VK_KHRONOS_PROFILES_DEBUG_ACTIONS=DEBUG_ACTION_STDOUT_BIT"
  "VK_KHRONOS_PROFILES_DEBUG_REPORTS=DEBUG_REPORT_NOTIFICATION_BIT,DEBUG_REPORT_WARNING_BIT,DEBUG_REPORT_ERROR_BIT"
  "VK_KHRONOS_VALIDATION_VALIDATE_CORE=true"
  "VK_KHRONOS_VALIDATION_DEBUG_ACTION=VK_DBG_LAYER_ACTION_LOG_MSG"
  "VK_KHRONOS_VALIDATION_REPORT_FLAGS=error,warn"
  "VK_KHRONOS_VALIDATION_VALIDATE_SYNC=$SYNC"
  "VK_KHRONOS_VALIDATION_VALIDATE_BEST_PRACTICES=$BEST_PRACTICES"
)
$FAIL_ON_ERROR && CFG+=("VK_KHRONOS_PROFILES_DEBUG_FAIL_ON_ERROR=true")
while IFS= read -r line; do
  case "$line" in ENV\ *) CFG+=("${line#ENV }") ;; esac
done <<EOF
$profile_info
EOF

# ---------------------------------------------------------------------------------------------
# Pre-flight: both layers must be installed.
# ---------------------------------------------------------------------------------------------
if command -v vulkaninfo >/dev/null 2>&1; then
  layer_list=$(env -u VK_INSTANCE_LAYERS vulkaninfo --summary 2>/dev/null | grep -E '^VK_LAYER_')
  for l in VK_LAYER_KHRONOS_profiles VK_LAYER_KHRONOS_validation; do
    echo "$layer_list" | grep -q "^$l " || die "layer $l not found by the Vulkan loader (install the Vulkan SDK; check VK_LAYER_PATH)"
  done
else
  echo "validate-web-profile: warning: vulkaninfo not on PATH, skipping layer pre-flight" >&2
fi

# macOS: /usr/bin/env and /bin/bash are SIP-protected and drop every DYLD_* variable, so a Vulkan SDK
# lib directory exported through DYLD_LIBRARY_PATH (setup-env.sh, ~/.zshenv) never reaches this
# script, and apps that rely on it then pick a different Vulkan loader copy than GLFW/MoltenVK
# (BRUTAL: "Could not create the window surface").  When the variable is empty, restore it from
# $VALIDATE_WEB_PROFILE_DYLD_LIBRARY_PATH, else $VULKAN_SDK/lib, else the newest ~/VulkanSDK/*/macOS/lib.
DYLD_NOTE=""
if [ "$(uname -s)" = Darwin ] && [ -z "${DYLD_LIBRARY_PATH:-}" ]; then
  sdk_lib=${VALIDATE_WEB_PROFILE_DYLD_LIBRARY_PATH:-}
  if [ -z "$sdk_lib" ] && [ -n "${VULKAN_SDK:-}" ] && [ -d "$VULKAN_SDK/lib" ]; then sdk_lib="$VULKAN_SDK/lib"; fi
  if [ -z "$sdk_lib" ]; then sdk_lib=$(ls -d "$HOME"/VulkanSDK/*/macOS/lib 2>/dev/null | sort | tail -1); fi
  if [ -n "$sdk_lib" ] && [ -d "$sdk_lib" ]; then
    export DYLD_LIBRARY_PATH="$sdk_lib"
    DYLD_NOTE="DYLD_LIBRARY_PATH=$sdk_lib (restored: SIP bash/env drop the caller's value)"
  fi
fi

if [ -z "$LOG" ]; then
  LOG=$(mktemp "${TMPDIR:-/tmp}/validate-web-profile.XXXXXX") || die "cannot create a log file"
fi

# ---------------------------------------------------------------------------------------------
# Print the configuration.
# ---------------------------------------------------------------------------------------------
kv() { printf '  %-14s : %s\n' "$1" "$2"; }
echo "== BRUTAL web profile validation =="
echo "$profile_info" | while IFS="$(printf '\t')" read -r tag key value; do
  [ "$tag" = INFO ] && kv "$key" "$value"
done
kv "profile file" "$PROFILE_JSON"
kv "layer order" "app -> VK_LAYER_KHRONOS_validation -> VK_LAYER_KHRONOS_profiles -> driver"
if [ -n "${VK_ICD_FILENAMES:-}" ]; then
  kv "ICD" "VK_ICD_FILENAMES set by the caller: $VK_ICD_FILENAMES"
else
  kv "ICD" "system install (VK_ICD_FILENAMES left unset)"
fi
[ -n "${VK_LAYER_SETTINGS_PATH:-}" ] && kv "note" "VK_LAYER_SETTINGS_PATH=$VK_LAYER_SETTINGS_PATH is set; the variables below take precedence over its file"
[ -n "${VK_LAYER_PATH:-}" ] && kv "note" "VK_LAYER_PATH=$VK_LAYER_PATH"
[ -n "$DYLD_NOTE" ] && kv "note" "$DYLD_NOTE"
echo "  settings (exported to the command):"
for kv_ in "${CFG[@]}"; do echo "    $kv_"; done
if [ ${#IGNORES[@]} -gt 0 ]; then
  for r in "${IGNORES[@]}"; do kv "ignoring" "lines matching $r"; done
fi
kv "command" "${CMD[*]}"
kv "log" "$LOG"
echo "== running =="

for kv in "${CFG[@]}"; do export "$kv"; done

# Runs the command on a pty.  /usr/bin/python3 is SIP-protected on macOS and drops DYLD_* from its
# own environment, so the values are handed over as VWP_* and restored just before exec.
read -r -d '' PTY_RUNNER <<'PY'
import errno, os, pty, sys

for name in ("DYLD_LIBRARY_PATH", "DYLD_FALLBACK_LIBRARY_PATH"):
    saved = os.environ.pop("VWP_" + name, "")
    if saved:
        os.environ[name] = saved
pid, fd = pty.fork()
if pid == 0:
    try:
        os.execvp(sys.argv[1], sys.argv[1:])
    except OSError as e:
        sys.stderr.write("validate-web-profile: cannot run %s: %s\n" % (sys.argv[1], e.strerror))
        os._exit(127)
while True:
    try:
        data = os.read(fd, 65536)
    except OSError as e:
        if e.errno == errno.EIO:        # child closed the pty
            break
        raise
    if not data:
        break
    os.write(1, data)
code = os.waitstatus_to_exitcode(os.waitpid(pid, 0)[1])
sys.exit(128 - code if code < 0 else code)    # killed by signal N -> 128 + N
PY
export VWP_DYLD_LIBRARY_PATH="${DYLD_LIBRARY_PATH:-}" VWP_DYLD_FALLBACK_LIBRARY_PATH="${DYLD_FALLBACK_LIBRARY_PATH:-}"

if $USE_PTY; then
  python3 -I -c "$PTY_RUNNER" "${CMD[@]}" 2>&1 | tee "$LOG"
else
  "${CMD[@]}" 2>&1 | tee "$LOG"
fi
cmd_rc=${PIPESTATUS[0]}
tr -d '\r' < "$LOG" > "$LOG.tmp" && mv "$LOG.tmp" "$LOG"    # pty turns \n into \r\n

# ---------------------------------------------------------------------------------------------
# Judge the run.
# ---------------------------------------------------------------------------------------------
JUDGED=$(mktemp "${TMPDIR:-/tmp}/validate-web-profile.judged.XXXXXX") || die "cannot create temp file"
trap 'rm -f "$JUDGED"' EXIT
if [ ${#IGNORES[@]} -gt 0 ]; then
  ignore_re=""
  for r in "${IGNORES[@]}"; do ignore_re="${ignore_re:+$ignore_re|}$r"; done
  grep -v -E "$ignore_re" "$LOG" > "$JUDGED"
else
  cp "$LOG" "$JUDGED"
fi

profiles_applied=false
grep -q -F "PROFILES NOTIFICATION: - Overriding device capabilities with the '$PROFILE' profile" "$JUDGED" && profiles_applied=true
profiles_errors=$(grep -c "PROFILES ERROR" "$JUDGED")
profiles_warnings=$(grep "PROFILES WARNING" "$JUDGED" | grep -v 'PROFILES WARNING: For VK_FORMAT_' | sort -u | wc -l | tr -d ' ')
val_errors=$(grep -c -E "^Validation Error:" "$JUDGED")
val_warnings=$(grep -c -E "^Validation Warning:" "$JUDGED")
vuid_lines=$(grep -c -E "VUID-" "$JUDGED")

echo
echo "== validate-web-profile result =="
echo "  profile            : $PROFILE ($($profiles_applied && echo applied || echo NOT applied))"
echo "  command exit code  : $cmd_rc"
echo "  profiles layer     : $profiles_errors error(s), $profiles_warnings distinct warning(s)"
echo "  validation layer   : $val_errors error(s), $val_warnings warning(s), $vuid_lines line(s) mentioning a VUID"

if [ "$profiles_warnings" -gt 0 ]; then
  echo "  profiles warnings (informational: host device / earlier capability block differs from the profile):"
  grep "PROFILES WARNING" "$JUDGED" | sed 's/^PROFILES WARNING: //' | grep -v '^For VK_FORMAT_' | cut -c1-160 | sort -u | head -20 | sed 's/^/    /'
fi
if [ "$profiles_errors" -gt 0 ]; then
  echo "  profiles layer errors:"
  grep "PROFILES ERROR" "$JUDGED" | cut -c1-200 | sort -u | head -12 | sed 's/^/    /'
fi
if [ "$val_errors" -gt 0 ] || [ "$vuid_lines" -gt 0 ]; then
  echo "  distinct validation messages (count  severity  id):"
  grep -E "^Validation (Error|Warning):" "$JUDGED" \
    | sed -E 's/^Validation (Error|Warning): \[ ([^ ]+) \].*/\1 \2/' \
    | sort | uniq -c | sort -rn | head -40 | sed -E 's/^ *([0-9]+) ([A-Za-z]+) (.*)$/    \1  \2  \3/'
fi

rc=0
if [ "$profiles_errors" -gt 0 ] || ! $profiles_applied; then
  echo "  FAIL: the profiles layer did not apply '$PROFILE' cleanly (see PROFILES ERROR lines above;"
  echo "        if there are none, the command never created a Vulkan instance or the layer was not loaded)."
  rc=3
elif [ "$val_errors" -gt 0 ] || [ "$vuid_lines" -gt 0 ]; then
  echo "  FAIL: validation reported errors / VUIDs against the simulated '$PROFILE' device."
  rc=1
elif [ "$cmd_rc" -ne 0 ]; then
  echo "  FAIL: the command exited with status $cmd_rc (no layer finding explains it)."
  if [ "$cmd_rc" -gt 128 ]; then
    echo "        Status > 128 means it was killed by signal $((cmd_rc - 128)).  A common cause: the profile's api-version is below 1.3, so the"
    echo "        layers hand out NULL for Vulkan 1.3 core entry points (vkCmdBeginRendering, vkCmdPipelineBarrier2, vkQueueSubmit2, ...);"
    echo "        keep api-version 1.3.0 (the ICD's) or call the *KHR names with VK_KHR_dynamic_rendering / VK_KHR_synchronization2."
  fi
  rc=4
else
  echo "  PASS"
fi
echo "  log: $LOG"
exit $rc
