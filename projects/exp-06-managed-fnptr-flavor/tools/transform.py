#!/usr/bin/env python3
"""exp-06: produce the "managed function-pointer flavor" of BRUTAL C# sources.

Copies every ``*.cs`` file under ``--src`` (the read-only BRUTAL checkout) to ``--out`` and rewrites
unmanaged function-pointer types into managed ones, so that every Vulkan call made through the
generated ``Pfn*``/``Vk*`` wrapper structs becomes a managed ``calli`` (no native transition):

  R1  delegate* unmanaged[Cdecl]<      -> delegate*<
  R2  delegate* unmanaged[Stdcall]<    -> delegate*<
  R3  delegate* unmanaged[<other>]<    -> delegate*<   (any other calling-convention list)
  R4  delegate* unmanaged<             -> delegate*<   (platform-default unmanaged)
  R5  [UnmanagedCallersOnly(...)]      -> removed      (only with --strip-uco; default on)

R5 matters for callbacks that the *ICD* calls back into (VkDebugUtilsMessengerCallbackEXT etc.): once the
callback pointer type is managed, the callback target must be a plain static method, and C# refuses
``&method`` of an [UnmanagedCallersOnly] method as a managed function pointer.

``where T : unmanaged`` constraints are untouched (all rules are anchored on ``delegate*`` or on the
attribute name). Files are written only when their content changes, so MSBuild's incremental build
keeps working. Files that no longer exist in --src (or are excluded) are deleted from --out.

The script exits with status 1 if any unmanaged function pointer (or, with --strip-uco, any
UnmanagedCallersOnly attribute) is left in the generated tree.

Run with ``python3 -I`` (isolated mode). Standard library only.
"""
from __future__ import annotations

import argparse
import os
import re
import sys
from dataclasses import dataclass, field

# Ordered rule list: (id, description, compiled regex, replacement)
_UCO_ARGS = r"(?:\((?:[^()]|\([^()]*\))*\))?"  # "(CallConvs = [typeof(CallConvCdecl)])" - one level of nested parens
RULES: list[tuple[str, str, re.Pattern[str], str]] = [
    ("R1", "delegate* unmanaged[Cdecl]< -> delegate*<",
     re.compile(r"delegate\*\s*unmanaged\s*\[\s*Cdecl\s*\]\s*<"), "delegate*<"),
    ("R2", "delegate* unmanaged[Stdcall]< -> delegate*<",
     re.compile(r"delegate\*\s*unmanaged\s*\[\s*Stdcall\s*\]\s*<"), "delegate*<"),
    ("R3", "delegate* unmanaged[<other callconv list>]< -> delegate*<",
     re.compile(r"delegate\*\s*unmanaged\s*\[[^\]]*\]\s*<"), "delegate*<"),
    ("R4", "delegate* unmanaged< -> delegate*<",
     re.compile(r"delegate\*\s*unmanaged\s*<"), "delegate*<"),
]
UCO_RULE = (
    "R5", "[UnmanagedCallersOnly(...)] attribute (own line or inline) -> removed",
    # whole-line form first (keeps the file tidy), then any inline leftovers
    re.compile(r"^[ \t]*\[\s*UnmanagedCallersOnly" + _UCO_ARGS + r"\s*\][ \t]*\r?\n|\[\s*UnmanagedCallersOnly" + _UCO_ARGS + r"\s*\]\s*",
               re.MULTILINE),
    "",
)

# What must not survive in the output.
LEFTOVER_FNPTR = re.compile(r"delegate\*\s*unmanaged")
LEFTOVER_UCO = re.compile(r"UnmanagedCallersOnly")


@dataclass
class Stats:
    files: int = 0
    written: int = 0
    unchanged: int = 0
    deleted: int = 0
    per_rule: dict[str, int] = field(default_factory=dict)
    per_file: list[tuple[str, dict[str, int]]] = field(default_factory=list)
    leftovers: list[tuple[str, int, int]] = field(default_factory=list)


def transform_text(text: str, strip_uco: bool) -> tuple[str, dict[str, int]]:
    counts: dict[str, int] = {}
    rules = RULES + ([UCO_RULE] if strip_uco else [])
    for rid, _desc, rx, repl in rules:
        text, n = rx.subn(repl, text)
        if n:
            counts[rid] = n
    return text, counts


def iter_sources(src: str):
    for root, dirs, files in os.walk(src):
        dirs[:] = sorted(d for d in dirs if d not in ("bin", "obj", ".git"))
        for f in sorted(files):
            if f.endswith(".cs"):
                full = os.path.join(root, f)
                yield os.path.relpath(full, src).replace(os.sep, "/"), full


def run(src: str, out: str, excludes: set[str], strip_uco: bool) -> Stats:
    st = Stats()
    produced: set[str] = set()
    for rel, full in iter_sources(src):
        if rel in excludes:
            continue
        st.files += 1
        # newline="" keeps CRLF/LF exactly; utf-8 (not utf-8-sig) keeps a BOM as U+FEFF and writes it back.
        with open(full, "r", encoding="utf-8", newline="") as fh:
            text = fh.read()
        new_text, counts = transform_text(text, strip_uco)
        if counts:
            st.per_file.append((rel, counts))
            for k, v in counts.items():
                st.per_rule[k] = st.per_rule.get(k, 0) + v
        n_fnptr = len(LEFTOVER_FNPTR.findall(new_text))
        n_uco = len(LEFTOVER_UCO.findall(new_text)) if strip_uco else 0
        if n_fnptr or n_uco:
            st.leftovers.append((rel, n_fnptr, n_uco))
        dst = os.path.join(out, rel)
        produced.add(os.path.normpath(dst))
        old = None
        if os.path.exists(dst):
            with open(dst, "r", encoding="utf-8", newline="") as fh:
                old = fh.read()
        if old == new_text:
            st.unchanged += 1
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "w", encoding="utf-8", newline="") as fh:
            fh.write(new_text)
        st.written += 1
    # prune stale outputs
    if os.path.isdir(out):
        for root, _dirs, files in os.walk(out):
            for f in files:
                p = os.path.normpath(os.path.join(root, f))
                if f.endswith(".cs") and p not in produced:
                    os.remove(p)
                    st.deleted += 1
    return st


def write_report(st: Stats, path: str | None, src: str, out: str, strip_uco: bool) -> str:
    lines = [
        "# exp-06 transform report",
        f"src\t{src}",
        f"out\t{out}",
        f"strip_uco\t{strip_uco}",
        f"files\t{st.files}\twritten\t{st.written}\tunchanged\t{st.unchanged}\tdeleted_stale\t{st.deleted}",
        "# rules",
    ]
    for rid, desc, rx, _ in RULES + ([UCO_RULE] if strip_uco else []):
        lines.append(f"{rid}\t{st.per_rule.get(rid, 0)}\t{desc}\t/{rx.pattern}/")
    lines.append("# per-file replacement counts (files with >= 1 replacement)")
    for rel, counts in st.per_file:
        lines.append(rel + "\t" + "\t".join(f"{k}={v}" for k, v in sorted(counts.items())))
    lines.append(f"# leftovers (delegate* unmanaged / UnmanagedCallersOnly) in output: {len(st.leftovers)} files")
    for rel, a, b in st.leftovers:
        lines.append(f"LEFTOVER\t{rel}\tfnptr={a}\tuco={b}")
    text = "\n".join(lines) + "\n"
    if path:
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(text)
    return text


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--src", required=True, help="source directory (e.g. BRUTAL/Brutal.Vulkan); read only")
    ap.add_argument("--out", required=True, help="output directory for the transformed copies")
    ap.add_argument("--exclude", action="append", default=[],
                    help="path relative to --src to skip (files replaced by patched copies); repeatable, ';' separated ok")
    ap.add_argument("--no-strip-uco", action="store_true", help="keep [UnmanagedCallersOnly] attributes (R5 off)")
    ap.add_argument("--report", help="write the rule/per-file report to this file")
    ap.add_argument("--quiet", action="store_true")
    a = ap.parse_args(argv)
    excludes = {e.strip().replace("\\", "/") for x in a.exclude for e in x.split(";") if e.strip()}
    src, out = os.path.abspath(a.src), os.path.abspath(a.out)
    if out.startswith(src + os.sep):
        print("transform.py: --out must not be inside --src (the source tree is read-only)", file=sys.stderr)
        return 2
    st = run(src, out, excludes, not a.no_strip_uco)
    report = write_report(st, a.report, src, out, not a.no_strip_uco)
    if not a.quiet:
        summary = ", ".join(f"{k}={v}" for k, v in sorted(st.per_rule.items())) or "no replacements"
        print(f"transform.py: {os.path.basename(src)}: {st.files} files ({st.written} written, {st.unchanged} unchanged, "
              f"{st.deleted} stale removed); {summary}; leftovers={len(st.leftovers)}")
    if st.leftovers:
        print(report, file=sys.stderr)
        print("transform.py: ERROR: unmanaged function pointers / UnmanagedCallersOnly left in output", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
