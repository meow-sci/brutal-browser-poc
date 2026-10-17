// Usage: node src/pushsize.mjs <dir-with-spvs>   -> push-constant block size per SPIR-V (via spirv-cross --reflect)
import { readdirSync } from 'node:fs'; import { spawnSync } from 'node:child_process'; import { join } from 'node:path';
const dir = process.argv[2]; const rows = [];
const baseSize = (t) => { const m = /^(?:[iub]?vec)(\d)$/.exec(t); if (m) return 4 * Number(m[1]); if (/^(float|int|uint|bool)$/.test(t)) return 4; if (/^(half|short|ushort)$/.test(t)) return 2; if (/^(double|int64_t|uint64_t)$/.test(t)) return 8; return null; };
for (const f of readdirSync(dir).filter((x) => x.endsWith('.spv'))) {
  const r = spawnSync('spirv-cross', [join(dir, f), '--reflect'], { encoding: 'utf8', maxBuffer: 1 << 28 }); if (r.status !== 0) continue;
  let j; try { j = JSON.parse(r.stdout); } catch { continue; }
  const pc = j.push_constants?.[0]; if (!pc) continue;
  const sizeOf = (typeName, member) => {
    let sz;
    if (member?.matrix_stride !== undefined && /^mat/.test(member.type)) sz = member.matrix_stride * Number(/mat(\d)/.exec(member.type)?.[1] ?? 4);
    else sz = baseSize(typeName) ?? structSize(typeName);
    if (member?.array) sz = (member.array_stride ?? sz) * member.array.reduce((a, b) => a * b, 1);
    return sz;
  };
  const structSize = (name) => { const t = j.types[name]; if (!t) return 0; let end = 0; for (const m of t.members) end = Math.max(end, m.offset + sizeOf(m.type, m)); return end; };
  rows.push([f, structSize(pc.type)]);
}
rows.sort((a, b) => b[1] - a[1]);
const sizes = rows.map((r) => r[1]);
console.log(`shaders with push constants: ${rows.length}; max=${Math.max(...sizes)} B; >64 B: ${sizes.filter((s) => s > 64).length}; >128 B: ${sizes.filter((s) => s > 128).length}; <=64 B: ${sizes.filter((s) => s <= 64).length}`);
console.log('largest 8:'); for (const r of rows.slice(0, 8)) console.log(`  ${String(r[1]).padStart(4)} B  ${r[0]}`);
const hist = {}; for (const s of sizes) { const b = s <= 16 ? '<=16' : s <= 32 ? '17-32' : s <= 64 ? '33-64' : s <= 128 ? '65-128' : '>128'; hist[b] = (hist[b] ?? 0) + 1; } console.log('histogram', JSON.stringify(hist));
