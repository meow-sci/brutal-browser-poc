#!/usr/bin/env bash
# Compares the signature-level API (types, methods, fields incl. private; metadata only) of a source-built BRUTAL
# assembly with the published NuGet assembly.
#   usage: patches/brutal/tools/api-diff.sh <built.dll> <nuget.dll> [out-dir]
# Prints the entry counts, how many entries differ only in the function-pointer calling convention (fnptr<CDecl> in
# the NuGet assembly and the unmanaged flavor, fnptr<Default> in the managed flavor), and every remaining difference
# ("<" only in NuGet, ">" only in the built assembly). Exit status 0 even when differences remain.
set -euo pipefail
built="$1" nuget="$2"
here="$(cd "$(dirname "$0")" && pwd)"
out="${3:-$(mktemp -d)}"
mkdir -p "$out"
export LC_ALL=C
dotnet build "$here/ApiDump" -c Release -v q -nologo >/dev/null
dump() { dotnet "$here/ApiDump/bin/Release/net10.0/ApiDump.dll" "$1" "$out/$2.raw" >/dev/null; sort "$out/$2.raw" > "$out/$2.txt"; }
dump "$nuget" nuget
dump "$built" built
norm() { sed 's/fnptr<CDecl>/fnptr<Default>/g' "$1" | sort > "$2"; }
norm "$out/nuget.txt" "$out/nuget.normalized.txt"
norm "$out/built.txt" "$out/built.normalized.txt"

n_nuget=$(wc -l < "$out/nuget.txt" | tr -d ' '); n_built=$(wc -l < "$out/built.txt" | tr -d ' ')
only_nuget_raw=$(comm -23 "$out/nuget.txt" "$out/built.txt" | wc -l | tr -d ' ')
only_nuget_norm=$(comm -23 "$out/nuget.normalized.txt" "$out/built.normalized.txt" | wc -l | tr -d ' ')
only_built_norm=$(comm -13 "$out/nuget.normalized.txt" "$out/built.normalized.txt" | wc -l | tr -d ' ')
echo "entries: nuget=$n_nuget built=$n_built"
echo "raw: $only_nuget_raw NuGet entries not found verbatim in the built assembly"
echo "after normalizing fnptr<CDecl> to fnptr<Default>: only in NuGet=$only_nuget_norm, only in built=$only_built_norm"
echo "callconv-only differences: $((only_nuget_raw - only_nuget_norm))"
rest=$(diff "$out/nuget.normalized.txt" "$out/built.normalized.txt" | grep '^[<>]' || true)
echo "${rest:-(no remaining differences)}"
