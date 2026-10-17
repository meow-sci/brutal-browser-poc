// Dumps a sorted, signature-level listing of every type/method/field/property in an assembly (metadata only).
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using var fs = File.OpenRead(args[0]);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var prov = new SigProvider(md);
var lines = new List<string>();
string TypeName(TypeDefinitionHandle h)
{
    var t = md.GetTypeDefinition(h);
    var name = md.GetString(t.Name);
    var decl = t.GetDeclaringType();
    if (!decl.IsNil) return TypeName(decl) + "+" + name;
    var ns = md.GetString(t.Namespace);
    return ns.Length > 0 ? ns + "." + name : name;
}
foreach (var th in md.TypeDefinitions)
{
    var t = md.GetTypeDefinition(th);
    var tn = TypeName(th);
    if (tn.Contains("<") || tn.StartsWith("Microsoft.CodeAnalysis") || tn.StartsWith("System.Runtime.CompilerServices")) continue;
    var vis = (t.Attributes & TypeAttributes.VisibilityMask);
    lines.Add($"T {vis} {tn}");
    foreach (var mh in t.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        var name = md.GetString(m.Name);
        if (name.Contains("<")) continue;
        var sig = m.DecodeSignature(prov, null);
        lines.Add($"M {tn}::{name}({string.Join(",", sig.ParameterTypes)}) : {sig.ReturnType} [{m.Attributes & MethodAttributes.MemberAccessMask}]");
    }
    foreach (var fh in t.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        var name = md.GetString(f.Name);
        if (name.Contains("<")) continue;
        lines.Add($"F {tn}::{name} : {f.DecodeSignature(prov, null)} [{f.Attributes & FieldAttributes.FieldAccessMask}]");
    }
}
lines.Sort(StringComparer.Ordinal);
File.WriteAllLines(args[1], lines);
Console.WriteLine($"{lines.Count} lines -> {args[1]}");

sealed class SigProvider(MetadataReader md) : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => $"fnptr<{s.Header.CallingConvention}>({string.Join(",", s.ParameterTypes)})->{s.ReturnType}";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object? c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? c, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool r) => u + (r ? " modreq(" : " modopt(") + m + ")";
    public string GetPinnedType(string e) => e + " pinned";
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
    {
        var t = r.GetTypeDefinition(h);
        var n = r.GetString(t.Name);
        var d = t.GetDeclaringType();
        return d.IsNil ? r.GetString(t.Namespace) + "." + n : GetTypeFromDefinition(r, d, k) + "+" + n;
    }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
    {
        var t = r.GetTypeReference(h);
        var n = r.GetString(t.Name);
        return t.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(r, (TypeReferenceHandle)t.ResolutionScope, k) + "+" + n
            : r.GetString(t.Namespace) + "." + n;
    }
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
}
