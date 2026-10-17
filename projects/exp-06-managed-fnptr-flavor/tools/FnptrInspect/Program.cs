// Usage: FnptrInspect <assembly.dll>...
// For each assembly prints: identity, size, P/Invoke count, [UnmanagedCallersOnly] method count, function-pointer
// fields grouped by calling convention, number of wrapper structs (types with an `Invoke` function-pointer field),
// and every `calli` in every method body grouped by the call-site signature's calling convention.
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

var opTable = new Dictionary<ushort, OpCode>();
foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    if (f.GetValue(null) is OpCode op) opTable[(ushort)op.Value] = op;

foreach (var path in args)
{
    Console.WriteLine($"=== {path}");
    using var fs = File.OpenRead(path);
    using var pe = new PEReader(fs);
    var md = pe.GetMetadataReader();
    var asm = md.GetAssemblyDefinition();
    var info = md.GetCustomAttributes(EntityHandle.AssemblyDefinition);
    Console.WriteLine($"  name: {asm.GetAssemblyName().FullName}; size={new FileInfo(path).Length:N0} bytes");
    foreach (var ca in asm.GetCustomAttributes())
    {
        var name = AttrName(md, md.GetCustomAttribute(ca));
        if (name is "AssemblyInformationalVersionAttribute")
        {
            var br = md.GetBlobReader(md.GetCustomAttribute(ca).Value); br.ReadUInt16();
            Console.WriteLine($"  informational: {br.ReadSerializedString()}");
        }
    }
    var prov = new Prov();
    int pinvoke = 0, uco = 0, types = 0, wrappers = 0;
    int ldftn = 0;
    var ldftnTargets = new SortedSet<string>();
    var fieldConv = new SortedDictionary<string, int>();
    var calliConv = new SortedDictionary<string, int>();
    var calliExamples = new Dictionary<string, string>();
    foreach (var th in md.TypeDefinitions)
    {
        var td = md.GetTypeDefinition(th);
        types++;
        bool isWrapper = false;
        foreach (var fh in td.GetFields())
        {
            var fd = md.GetFieldDefinition(fh);
            var sig = fd.DecodeSignature(prov, null);
            if (sig.StartsWith("fnptr:"))
            {
                var conv = sig.Split(':')[1];
                fieldConv[conv] = fieldConv.GetValueOrDefault(conv) + 1;
                if (md.GetString(fd.Name) == "Invoke") isWrapper = true;
            }
        }
        if (isWrapper) wrappers++;
        foreach (var mh in td.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            if (!m.GetImport().Module.IsNil) pinvoke++;
            foreach (var ca in m.GetCustomAttributes())
                if (AttrName(md, md.GetCustomAttribute(ca)) == "UnmanagedCallersOnlyAttribute") uco++;
            if (m.RelativeVirtualAddress == 0) continue;
            var body = pe.GetMethodBody(m.RelativeVirtualAddress);
            var il = body.GetILReader();
            while (il.RemainingBytes > 0)
            {
                ushort code = il.ReadByte();
                if (code == 0xFE) code = (ushort)(0xFE00 | il.ReadByte());
                var op = opTable[code];
                if (op == OpCodes.Calli)
                {
                    var tok = il.ReadInt32();
                    var ss = md.GetStandaloneSignature((StandaloneSignatureHandle)MetadataTokens.Handle(tok));
                    var msig = ss.DecodeMethodSignature(prov, null);
                    var conv = msig.Header.CallingConvention.ToString();
                    calliConv[conv] = calliConv.GetValueOrDefault(conv) + 1;
                    calliExamples.TryAdd(conv, $"{md.GetString(td.Name)}::{md.GetString(m.Name)} -> {msig.ReturnType}({string.Join(", ", msig.ParameterTypes)})");
                    continue;
                }
                if (op == OpCodes.Ldftn)
                {
                    var tok = il.ReadInt32();
                    var h = MetadataTokens.EntityHandle(tok);
                    ldftn++;
                    if (h.Kind == HandleKind.MethodDefinition)
                    {
                        var target = md.GetMethodDefinition((MethodDefinitionHandle)h);
                        ldftnTargets.Add($"{md.GetString(md.GetTypeDefinition(target.GetDeclaringType()).Name)}::{md.GetString(target.Name)}");
                    }
                    continue;
                }
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: il.ReadByte(); break;
                    case OperandType.InlineVar: il.ReadUInt16(); break;
                    case OperandType.InlineI8: case OperandType.InlineR: il.ReadInt64(); break;
                    case OperandType.InlineSwitch: var n = il.ReadInt32(); for (var i = 0; i < n; i++) il.ReadInt32(); break;
                    default: il.ReadInt32(); break;
                }
            }
        }
    }
    Console.WriteLine($"  types={types}; wrapper structs with fnptr 'Invoke' field={wrappers}; pinvokes={pinvoke}; [UnmanagedCallersOnly] methods={uco}");
    Console.WriteLine($"  function-pointer fields by calling convention: {(fieldConv.Count == 0 ? "none" : string.Join(", ", fieldConv.Select(kv => $"{kv.Key}={kv.Value}")))}");
    Console.WriteLine($"  calli sites by call-site calling convention: {(calliConv.Count == 0 ? "none" : string.Join(", ", calliConv.Select(kv => $"{kv.Key}={kv.Value}")))}");
    foreach (var kv in calliExamples) Console.WriteLine($"    e.g. [{kv.Key}] {kv.Value}");
    Console.WriteLine($"  ldftn sites={ldftn}; distinct same-assembly targets={ldftnTargets.Count}{(ldftnTargets.Count is > 0 and <= 60 ? ": " + string.Join(", ", ldftnTargets) : "")}");
}

static string AttrName(MetadataReader md, CustomAttribute ca)
{
    EntityHandle parent = ca.Constructor.Kind switch
    {
        HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)ca.Constructor).Parent,
        HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor).GetDeclaringType(),
        _ => default,
    };
    return parent.Kind switch
    {
        HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name),
        HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
        _ => "",
    };
}

sealed class Prov : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string e, ArrayShape s) => e + "[,]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => $"fnptr:{s.Header.CallingConvention}:{s.ReturnType}({string.Join(",", s.ParameterTypes)})";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => $"{g}<{string.Join(",", a)}>";
    public string GetGenericMethodParameter(object? c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? c, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool r) => u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString();
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => r.GetString(r.GetTypeDefinition(h).Name);
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => r.GetString(r.GetTypeReference(h).Name);
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
}
