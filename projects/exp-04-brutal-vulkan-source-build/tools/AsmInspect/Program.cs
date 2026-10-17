using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

foreach (var path in args)
{
    Console.WriteLine($"=== {path}");
    using var fs = File.OpenRead(path);
    using var pe = new PEReader(fs);
    var md = pe.GetMetadataReader();
    var asm = md.GetAssemblyDefinition();
    var an = asm.GetAssemblyName();
    var tok = an.GetPublicKeyToken();
    Console.WriteLine($"AssemblyName: {an.FullName}");
    Console.WriteLine($"PublicKeyToken: {(tok is { Length: > 0 } ? Convert.ToHexString(tok) : "null (not strong-named)")}");
    Console.WriteLine($"CorFlags: {pe.PEHeaders.CorHeader!.Flags}; StrongNameSignature dir size: {pe.PEHeaders.CorHeader.StrongNameSignatureDirectory.Size}");
    Console.WriteLine($"MVID: {md.GetGuid(md.GetModuleDefinition().Mvid)}");
    foreach (var cah in asm.GetCustomAttributes())
    {
        var ca = md.GetCustomAttribute(cah);
        string typeName = "";
        if (ca.Constructor.Kind == HandleKind.MemberReference)
        {
            var mr = md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
            if (mr.Parent.Kind == HandleKind.TypeReference)
                typeName = md.GetString(md.GetTypeReference((TypeReferenceHandle)mr.Parent).Name);
        }
        var blob = md.GetBlobReader(ca.Value);
        blob.ReadUInt16();
        string? arg = null;
        try { arg = blob.ReadSerializedString(); } catch { }
        Console.WriteLine($"  [{typeName}({arg})]");
    }
    foreach (var arh in md.AssemblyReferences)
    {
        var ar = md.GetAssemblyReference(arh);
        Console.WriteLine($"  ref: {ar.GetAssemblyName().FullName}");
    }
    int pinvokes = 0;
    foreach (var mh in md.MethodDefinitions)
    {
        var m = md.GetMethodDefinition(mh);
        var imp = m.GetImport();
        if (imp.Module.IsNil) continue;
        pinvokes++;
        var mod = md.GetModuleReference(imp.Module);
        var t = md.GetTypeDefinition(m.GetDeclaringType());
        Console.WriteLine($"  pinvoke: {md.GetString(t.Name)}.{md.GetString(m.Name)} -> module '{md.GetString(mod.Name)}' entry '{md.GetString(imp.Name)}' attrs={imp.Attributes}");
    }
    Console.WriteLine($"  pinvoke count: {pinvokes}");
    var interesting = new List<string>();
    var cur = MetadataTokens.UserStringHandle(1);
    int heapSize = md.GetHeapSize(HeapIndex.UserString);
    while (MetadataTokens.GetHeapOffset(cur) < heapSize)
    {
        var s = md.GetUserString(cur);
        if (s.Contains("ulkan", StringComparison.Ordinal) || s.StartsWith("vk") || s.Contains("BRUTAL_"))
            interesting.Add(s);
        cur = md.GetNextHandle(cur);
        if (cur.IsNil) break;
    }
    Console.WriteLine($"  user strings (filtered): {string.Join(" | ", interesting)}");
}
