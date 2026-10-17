// Minimal SPIR-V reader shared by spv-prepare, spv-layout-lint and build-shaders.
//
// Only what the pipeline needs: an instruction iterator and a "module summary" (types, constants, decorations, variables,
// entry points, capabilities) built in one pass. It does not validate anything - spirv-val does that.

export const OP = {
  EntryPoint: 15, Capability: 17,
  TypeBool: 20, TypeInt: 21, TypeFloat: 22, TypeVector: 23, TypeMatrix: 24, TypeImage: 25, TypeSampler: 26,
  TypeSampledImage: 27, TypeArray: 28, TypeRuntimeArray: 29, TypeStruct: 30, TypePointer: 32,
  Constant: 43, Variable: 59, Decorate: 71, MemberDecorate: 72,
};
export const DEC = { Block: 2, BufferBlock: 3, RowMajor: 4, ColMajor: 5, ArrayStride: 6, MatrixStride: 7, NonWritable: 24, NonReadable: 25, Binding: 33, DescriptorSet: 34, Offset: 35 };
export const STORAGE = { UniformConstant: 0, Uniform: 2, PushConstant: 9, StorageBuffer: 12 };
export const CAP = { Float64: 10, Int64: 11 };
export const EXEC_MODEL = { 0: 'vertex', 4: 'fragment', 5: 'compute' };

const SPIRV_MAGIC = 0x07230203;

/** Bytes -> Uint32Array of SPIR-V words (copies, so alignment of the source buffer does not matter). */
export function loadWords(bytes) {
  if (bytes.length < 20 || bytes.length % 4 !== 0) throw new Error(`not a SPIR-V module (size ${bytes.length} is not a multiple of 4 or is smaller than the header)`);
  const words = new Uint32Array(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength));
  if (words[0] !== SPIRV_MAGIC) throw new Error(`not a little-endian SPIR-V module (magic 0x${words[0].toString(16)})`);
  return words;
}

/** Words -> Buffer (little endian, as written by glslc / spirv-opt). */
export const wordsToBuffer = (words) => Buffer.from(words.buffer, words.byteOffset, words.byteLength);

/** Iterate instructions after the 5-word header: yields { op, wc, at } where `at` is the index of the first word. */
export function* instructions(words) {
  for (let at = 5; at < words.length;) {
    const wc = words[at] >>> 16, op = words[at] & 0xffff;
    if (wc === 0) throw new Error(`malformed SPIR-V: zero-length instruction at word ${at}`);
    yield { op, wc, at };
    at += wc;
  }
}

/** Decode a nul-terminated, word-packed SPIR-V literal string starting at words[at]. */
function literalString(words, at, end) {
  const bytes = [];
  for (let i = at; i < end; i++) for (let s = 0; s < 32; s += 8) { const b = (words[i] >>> s) & 0xff; if (b === 0) return String.fromCharCode(...bytes); bytes.push(b); }
  return String.fromCharCode(...bytes);
}

/** One pass over the module. Types are { kind, ... }; decorations are Map<decoration, operands[]>. */
export function parseModule(words) {
  const m = {
    version: words[1], types: new Map(), constants: new Map(), decorations: new Map(), memberDecorations: new Map(),
    variables: [], entryPoints: [], capabilities: new Set(),
  };
  const addDec = (map, key, dec, operands) => { if (!map.has(key)) map.set(key, new Map()); map.get(key).set(dec, operands); };
  for (const { op, wc, at } of instructions(words)) {
    const w = (n) => words[at + n];
    switch (op) {
      case OP.Capability: m.capabilities.add(w(1)); break;
      case OP.EntryPoint: m.entryPoints.push({ model: EXEC_MODEL[w(1)] ?? `executionModel${w(1)}`, id: w(2), name: literalString(words, at + 3, at + wc) }); break;
      case OP.TypeBool: m.types.set(w(1), { kind: 'bool', bytes: 4 }); break;
      case OP.TypeInt: m.types.set(w(1), { kind: 'int', bytes: w(2) / 8, signed: w(3) === 1 }); break;
      case OP.TypeFloat: m.types.set(w(1), { kind: 'float', bytes: w(2) / 8 }); break;
      case OP.TypeVector: m.types.set(w(1), { kind: 'vector', comp: w(2), count: w(3) }); break;
      case OP.TypeMatrix: m.types.set(w(1), { kind: 'matrix', col: w(2), cols: w(3) }); break;
      case OP.TypeSampler: m.types.set(w(1), { kind: 'sampler' }); break;
      case OP.TypeImage: m.types.set(w(1), { kind: 'image' }); break;
      case OP.TypeSampledImage: m.types.set(w(1), { kind: 'sampledImage' }); break;
      case OP.TypeArray: m.types.set(w(1), { kind: 'array', elem: w(2), lengthId: w(3) }); break;
      case OP.TypeRuntimeArray: m.types.set(w(1), { kind: 'runtimeArray', elem: w(2) }); break;
      case OP.TypeStruct: m.types.set(w(1), { kind: 'struct', members: [...words.slice(at + 2, at + wc)] }); break;
      case OP.TypePointer: m.types.set(w(1), { kind: 'pointer', storage: w(2), pointee: w(3) }); break;
      case OP.Constant: m.constants.set(w(2), w(3)); break;           // low 32 bits are enough for array lengths
      case OP.Variable: m.variables.push({ id: w(2), type: w(1), storage: w(3) }); break;
      case OP.Decorate: addDec(m.decorations, w(1), w(2), [...words.slice(at + 3, at + wc)]); break;
      case OP.MemberDecorate: addDec(m.memberDecorations, `${w(1)}:${w(2)}`, w(3), [...words.slice(at + 4, at + wc)]); break;
      default: break;
    }
  }
  return m;
}

/** Strip array wrappers: array<T> / T[] -> T. */
export function stripArrays(m, typeId) {
  let t = m.types.get(typeId);
  while (t && (t.kind === 'array' || t.kind === 'runtimeArray')) t = m.types.get(t.elem);
  return t;
}

/** Variables whose (array-stripped) pointee is a combined image sampler (`sampler2D` etc.). */
export function combinedSamplerVariables(m) {
  return m.variables.filter((v) => {
    const ptr = m.types.get(v.type);
    return ptr?.kind === 'pointer' && ptr.storage === STORAGE.UniformConstant && stripArrays(m, ptr.pointee)?.kind === 'sampledImage';
  });
}

/**
 * Size in bytes of a type according to its explicit SPIR-V layout (Offset / ArrayStride / MatrixStride decorations).
 * `ctx` carries the struct-member decorations that apply to matrices nested below the member: { rowMajor, matrixStride }.
 */
export function layoutSize(m, typeId, ctx = {}) {
  const t = m.types.get(typeId);
  if (!t) throw new Error(`unknown type %${typeId}`);
  switch (t.kind) {
    case 'bool': case 'int': case 'float': return t.bytes;
    case 'vector': return layoutSize(m, t.comp) * t.count;
    case 'matrix': {
      const stride = ctx.matrixStride ?? 0, vec = m.types.get(t.col), compBytes = layoutSize(m, vec.comp);
      return ctx.rowMajor ? (vec.count - 1) * stride + t.cols * compBytes : (t.cols - 1) * stride + vec.count * compBytes;
    }
    case 'array': {
      const len = m.constants.get(t.lengthId), stride = m.decorations.get(typeId)?.get(DEC.ArrayStride)?.[0];
      if (len === undefined || stride === undefined) throw new Error('array with a specialization-constant length or without ArrayStride');
      return len * stride;
    }
    case 'struct': {
      let end = 0;
      t.members.forEach((memberType, i) => {
        const d = m.memberDecorations.get(`${typeId}:${i}`), offset = d?.get(DEC.Offset)?.[0];
        if (offset === undefined) throw new Error(`struct member ${i} has no Offset decoration`);
        end = Math.max(end, offset + layoutSize(m, memberType, { rowMajor: d.has(DEC.RowMajor), matrixStride: d.get(DEC.MatrixStride)?.[0] }));
      });
      return end;
    }
    default: throw new Error(`type kind '${t.kind}' has no layout size`);
  }
}

/**
 * Declared size of the push-constant block: end offset of its last member (no tail padding), or null when the module has none.
 * Throws when the layout cannot be computed.
 */
export function pushConstantBlockEnd(m) {
  const v = m.variables.find((x) => x.storage === STORAGE.PushConstant);
  return v ? layoutSize(m, m.types.get(v.type).pointee) : null;
}
