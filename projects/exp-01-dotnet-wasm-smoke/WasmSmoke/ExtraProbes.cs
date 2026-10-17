// Hand-written follow-up probes for the generated signature probes (run with ?tests=P46 etc., one per page load).
using System;
using System.Runtime.InteropServices.JavaScript;
using Brutal;
using Brutal.VulkanApi;

public static unsafe partial class SmokeTests
{
    [JSImport("makeJsSigRec", "main.js")] internal static partial int MakeJsSigRec(string sig);
    [JSImport("jsSigLastArgs", "main.js")] internal static partial string JsSigLastArgs(string sig);

    static void RunExtraProbes()
    {
        RunProbe("P46", "i64 as raw ulong param (vijii)", P46_RawUlong);
        RunProbe("P47", "64-bit enum (VkPipelineStageFlags2 : ulong) vs JS sig viiii", P47_EnumAsI32);
        RunProbe("P48", "i64 value correctness via ByteSize64 (VkBindBufferMemory, iiiij)", P48_ByteSize64Value);
        RunProbe("P49", "float arg via raw fnptr (vif)", P49_Float);
    }

    static string P46_RawUlong()
    {
        var f = (delegate* unmanaged[Cdecl]<nint, ulong, nint, int, void>)(nint)MakeJsSigRec("vijii");
        f(0x11, 0x1_0000_0002UL, 0x33, 4);
        return "JS received args: " + JsSigLastArgs("vijii");
    }

    static string P47_EnumAsI32()
    {
        // If Mono classifies a ulong-backed enum as a 32-bit value in its interp->native cookie, this call
        // (JS function typed (i32,i32,i32,i32)->void) succeeds and JS sees only the low 32 bits.
        var f = (VkCmdWriteTimestamp2)new PfnVkVoidFunction((delegate* unmanaged[Cdecl]<void>)(nint)MakeJsSigRec("viiii"));
        f.Invoke(new VkCommandBuffer(0x11), (VkPipelineStageFlags2)0x1_0000_0002UL, new VkQueryPool(0x33), 4);
        return "JS (typed viiii) received args: " + JsSigLastArgs("viiii") + " (sent 0x11, 0x100000002, 0x33, 4)";
    }

    static string P48_ByteSize64Value()
    {
        var f = (VkBindBufferMemory)new PfnVkVoidFunction((delegate* unmanaged[Cdecl]<void>)(nint)MakeJsSigRec("iiiij"));
        var r = f.Invoke(new VkDevice(0x11), new VkBuffer(0x22), new VkDeviceMemory(0x33), (ByteSize64)0x1_2345_6789UL);
        return $"ret={r}; JS received args: " + JsSigLastArgs("iiiij") + " (sent 0x11, 0x22, 0x33, 0x123456789)";
    }

    static string P49_Float()
    {
        var f = (delegate* unmanaged[Cdecl]<nint, float, void>)(nint)MakeJsSigRec("vif");
        f(0x11, 1.5f);
        return "JS received args: " + JsSigLastArgs("vif");
    }
}
