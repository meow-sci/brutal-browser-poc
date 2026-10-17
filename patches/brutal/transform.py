#!/usr/bin/env python3
"""Copy a BRUTAL library's C# sources, optionally turning them into the "managed function-pointer flavor".

Copies every ``*.cs`` file under ``--src`` (a read-only BRUTAL library directory, e.g. ``BRUTAL/Brutal.Vulkan``) to
``--out``. With ``--managed-fnptr`` unmanaged function-pointer types are rewritten into managed ones, so every Vulkan
call made through the generated ``Pfn*``/``Vk*`` wrapper structs is a managed ``calli`` (no native transition):

  R1  delegate* unmanaged[Cdecl]<      -> delegate*<
  R2  delegate* unmanaged[Stdcall]<    -> delegate*<
  R3  delegate* unmanaged[<other>]<    -> delegate*<   (any other calling-convention list)
  R4  delegate* unmanaged<             -> delegate*<   (platform-default unmanaged)
  R5  [UnmanagedCallersOnly(...)]      -> removed      (default on; --no-strip-uco keeps it)

R5 matters for callbacks that the *ICD* calls back into (VkDebugUtilsMessengerCallbackEXT etc.): once the callback
pointer type is managed, the target must be a plain static method, and C# refuses ``&method`` of an
[UnmanagedCallersOnly] method as a managed function pointer.

Without ``--managed-fnptr`` the files are copied unchanged. ``--exclude <path relative to --src>`` (repeatable, ``;``
separated also accepted) skips files that have a patched replacement. Files are written only when their content changes
(MSBuild's incremental compile stays incremental) and stale ``.cs`` files in ``--out`` are removed. ``where T : unmanaged``
constraints are untouched: every rule is anchored on ``delegate*`` or on the attribute name.

Exit status 1 if an unmanaged function pointer (or, with R5 on, an UnmanagedCallersOnly) is left in a managed-flavor
tree; 2 for usage errors. Run with ``python3 -I`` (isolated mode). Standard library only.
"""
from __future__ import annotations

import argparse
import os
import re
import sys
from dataclasses import dataclass, field

_UCO_ARGS = r"(?:\((?:[^()]|\([^()]*\))*\))?"  # "(CallConvs = [typeof(CallConvCdecl)])" - one level of nested parens
# (id, description, compiled regex, replacement)
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


def active_rules(managed: bool, strip_uco: bool):
    return (RULES + ([UCO_RULE] if strip_uco else [])) if managed else []


def transform_text(text: str, managed: bool, strip_uco: bool) -> tuple[str, dict[str, int]]:
    counts: dict[str, int] = {}
    for rid, _desc, rx, repl in active_rules(managed, strip_uco):
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


def run(src: str, out: str, excludes: set[str], managed: bool, strip_uco: bool) -> Stats:
    st = Stats()
    produced: set[str] = set()
    for rel, full in iter_sources(src):
        if rel in excludes:
            continue
        st.files += 1
        # newline="" keeps CRLF/LF exactly; utf-8 (not utf-8-sig) keeps a BOM as U+FEFF and writes it back.
        with open(full, "r", encoding="utf-8", newline="") as fh:
            text = fh.read()
        new_text, counts = transform_text(text, managed, strip_uco)
        if counts:
            st.per_file.append((rel, counts))
            for k, v in counts.items():
                st.per_rule[k] = st.per_rule.get(k, 0) + v
        if managed:
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
    if os.path.isdir(out):  # prune stale outputs
        for root, _dirs, files in os.walk(out):
            for f in files:
                p = os.path.normpath(os.path.join(root, f))
                if f.endswith(".cs") and p not in produced:
                    os.remove(p)
                    st.deleted += 1
    return st


def write_report(st: Stats, path: str, src: str, out: str, managed: bool, strip_uco: bool) -> str:
    lines = [
        "# BRUTAL transform report",
        f"src\t{src}", f"out\t{out}", f"managed_fnptr\t{managed}\tstrip_uco\t{strip_uco}",
        f"files\t{st.files}\twritten\t{st.written}\tunchanged\t{st.unchanged}\tdeleted_stale\t{st.deleted}",
        "# rules",
    ]
    for rid, desc, rx, _ in active_rules(managed, strip_uco):
        lines.append(f"{rid}\t{st.per_rule.get(rid, 0)}\t{desc}\t/{rx.pattern}/")
    lines.append("# per-file replacement counts (files with >= 1 replacement)")
    lines += [rel + "\t" + "\t".join(f"{k}={v}" for k, v in sorted(c.items())) for rel, c in st.per_file]
    lines.append(f"# leftovers (delegate* unmanaged / UnmanagedCallersOnly) in output: {len(st.leftovers)} files")
    lines += [f"LEFTOVER\t{rel}\tfnptr={a}\tuco={b}" for rel, a, b in st.leftovers]
    text = "\n".join(lines) + "\n"
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(text)
    return text


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--src", required=True, help="BRUTAL library directory (e.g. BRUTAL/Brutal.Vulkan); read only")
    ap.add_argument("--out", required=True, help="output directory for the copies")
    ap.add_argument("--managed-fnptr", action="store_true", help="rewrite unmanaged function pointers to managed ones (R1-R5)")
    ap.add_argument("--exclude", action="append", default=[],
                    help="path relative to --src to skip (file replaced by a patched copy); repeatable, ';' separated ok")
    ap.add_argument("--no-strip-uco", action="store_true", help="with --managed-fnptr: keep [UnmanagedCallersOnly] (R5 off)")
    ap.add_argument("--report", help="write the rule/per-file report to this file")
    ap.add_argument("--quiet", action="store_true", help="no summary line")
    a = ap.parse_args(argv)
    excludes = {e.strip().replace("\\", "/") for x in a.exclude for e in x.split(";") if e.strip()}
    src, out = os.path.abspath(a.src), os.path.abspath(a.out)
    if not os.path.isdir(src):
        print(f"transform.py: --src is not a directory: {src}", file=sys.stderr)
        return 2
    if out == src or out.startswith(src + os.sep):
        print("transform.py: --out must not be inside --src (the source tree is read-only)", file=sys.stderr)
        return 2
    strip_uco = not a.no_strip_uco
    st = run(src, out, excludes, a.managed_fnptr, strip_uco)
    if a.report:
        write_report(st, a.report, src, out, a.managed_fnptr, strip_uco)
    if not a.quiet:
        summary = ", ".join(f"{k}={v}" for k, v in sorted(st.per_rule.items())) or "no replacements"
        print(f"transform.py: {os.path.basename(src)} [{'managed' if a.managed_fnptr else 'unmanaged'}]: {st.files} files "
              f"({st.written} written, {st.unchanged} unchanged, {st.deleted} stale removed, {len(excludes)} excluded); "
              f"{summary}; leftovers={len(st.leftovers)}")
    if st.leftovers:
        for rel, n_fnptr, n_uco in st.leftovers:
            print(f"transform.py: LEFTOVER {rel} fnptr={n_fnptr} uco={n_uco}", file=sys.stderr)
        print("transform.py: ERROR: unmanaged function pointers / UnmanagedCallersOnly left in output", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
