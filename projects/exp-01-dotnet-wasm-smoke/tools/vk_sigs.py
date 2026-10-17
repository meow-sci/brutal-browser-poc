# Computes the wasm32 call signature shape of every BRUTAL Vulkan function-pointer struct.
import re, os, sys, glob, collections
root = sys.argv[1]
gen = os.path.join(root, 'Generated')
src = open(os.path.join(gen, 'Functions.gen.cs')).read()
enums = {}
for f in glob.glob(os.path.join(gen, 'Enums', '**', '*.cs'), recursive=True):
    for m in re.finditer(r'enum (\w+) : (\w+)', open(f).read()):
        enums[m.group(1)] = m.group(2)
handles = {os.path.basename(f).split('.')[0] for f in glob.glob(os.path.join(gen, 'Handles', '**', '*.cs'), recursive=True)}
structs = {os.path.basename(f).split('.')[0] for f in glob.glob(os.path.join(gen, 'Structs', '**', '*.cs'), recursive=True)}
I64 = {'long', 'ulong', 'ByteSize64', 'VkDeviceAddress', 'VkFlags64', 'VkDeviceSize'}
I32 = {'int', 'uint', 'nint', 'nuint', 'short', 'ushort', 'byte', 'sbyte', 'Bool32', 'Brutal.Bool32', 'VkFlags', 'VkSampleMask', 'bool'}
unknown = collections.Counter()
def kind(t):
    t = t.strip()
    if t.endswith('*'): return 'i'
    if t == 'void': return 'v'
    if t in I64: return 'j'
    if t in I32: return 'i'
    if t == 'float': return 'f'
    if t == 'double': return 'd'
    if t in handles or t.startswith('Pfn'): return 'i'
    if t in enums: return 'j' if enums[t] in ('long', 'ulong') else 'i'
    if t in structs: return 'S'
    unknown[t] += 1
    return 'i'  # remaining unknowns are int-backed flags/enums or handles with odd casing
def split_args(s):
    out, depth, cur = [], 0, ''
    for ch in s:
        if ch == '<': depth += 1
        if ch == '>': depth -= 1
        if ch == ',' and depth == 0: out.append(cur); cur = ''
        else: cur += ch
    out.append(cur)
    return [a.strip() for a in out]
sigs = {}
for m in re.finditer(r'public readonly unsafe struct (\w+)\(\s*delegate\* unmanaged\[Cdecl\]<(.*?)> handle\s*\)', src, re.S):
    name, args = m.group(1), split_args(' '.join(m.group(2).split()))
    ks = [kind(a) for a in args]
    sigs[name] = ks[-1] + ''.join(ks[:-1])   # emscripten style: ret first
by = collections.defaultdict(list)
for n, s in sigs.items(): by[s].append(n)
print(f'fnptr structs: {len(sigs)}  distinct wasm32 signatures: {len(by)}  unknown types: {dict(unknown)}')
cnt64 = sum(1 for s in sigs.values() if 'j' in s); cntf = sum(1 for s in sigs.values() if 'f' in s or 'd' in s); cntS = sum(1 for s in sigs.values() if 'S' in s)
print(f'signatures with i64 params/ret: {cnt64} fns; with float/double: {cntf} fns; with by-value multi-field structs: {cntS} fns')
maxp = max(len(s) - 1 for s in sigs.values()); print('max params:', maxp)
want = sys.argv[2:] 
for n in want: print(n, sigs.get(n))
if '--all' in sys.argv:
    for s, ns in sorted(by.items(), key=lambda kv: -len(kv[1])): print(f'{s:16} {len(ns):4}  e.g. {", ".join(ns[:4])}')
