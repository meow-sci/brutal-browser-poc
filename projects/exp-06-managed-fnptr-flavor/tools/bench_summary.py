#!/usr/bin/env python3
"""exp-06: aggregate BENCH / TIMING lines of the browser and desktop runs in out/ into a Markdown table.
Run from the project root with `python3 -I tools/bench_summary.py > out/bench-summary.txt`."""
import re
import statistics as st

FILES = {"chrome#1": "out/browser-run-chrome-1.txt", "chrome#2": "out/browser-run-chrome-2.txt",
         "chrome#3": "out/browser-run-chrome-3.txt", "edge#1": "out/browser-run-edge.txt",
         "desktop": "out/desktop-flavor-smoke.txt"}
rows, order = {}, []
for k, f in FILES.items():
    for line in open(f, encoding="utf-8"):
        m = re.search(r"BENCH (.+?): ([0-9.]+) ns/call", line)
        if m:
            if m.group(1) not in rows:
                rows[m.group(1)] = {}
                order.append(m.group(1))
            rows[m.group(1)][k] = float(m.group(2))
print("| benchmark | Chrome 151 (3 page loads, min-max) | Edge 155 | browser median (4) | desktop CoreCLR |")
print("|---|---|---|---|---|")
for lbl in order:
    r = rows[lbl]
    ch = [r[x] for x in ("chrome#1", "chrome#2", "chrome#3")]
    print(f"| {lbl} | {min(ch):.1f}-{max(ch):.1f} | {r['edge#1']:.1f} | {st.median(ch + [r['edge#1']]):.1f} | {r['desktop']:.1f} |")
print()
for k, f in FILES.items():
    for line in open(f, encoding="utf-8"):
        if "S02-global-version" in line:
            print(k, "first touch (Vulkan cctor + 4 global lookups + vkEnumerateInstanceVersion):", line.split("cctor:")[1].strip().rstrip(")"))
        if "TIMING" in line:
            print(k, line.split("TIMING", 1)[1].strip())
