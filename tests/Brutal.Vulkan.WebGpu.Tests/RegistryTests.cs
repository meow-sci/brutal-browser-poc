using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;

namespace WebGpuIcd.Tests;

public sealed unsafe class RegistryTests
{
    public RegistryTests() => TestIcd.Reset();

    private static string[] ProcNames(string file) =>
        File.ReadAllLines(Path.Combine(TestIcd.RepoRoot, "projects/exp-04-brutal-vulkan-source-build/out", file))
            .Select(l => l.Split('\t')[1]).ToArray();

    private static nint Gipa(string name, nint instance = 0)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return (nint)Icd.vkGetInstanceProcAddr(new VkInstance(instance), p).Invoke;
    }

    private static nint Gdpa(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return (nint)Icd.vkGetDeviceProcAddr(new VkDevice(0x1234), p).Invoke;
    }

    [Fact]
    public void EveryNameBrutalRequestsResolvesToATypedFunction()
    {
        var instanceNames = ProcNames("instance-proc-names.txt");
        var deviceNames = ProcNames("device-proc-names.txt");
        Assert.Equal(110, instanceNames.Length);
        Assert.Equal(697, deviceNames.Length);
        Assert.All(instanceNames, n => Assert.NotEqual(0, Gipa(n, n.StartsWith("vkCreateInstance") || n.StartsWith("vkEnumerateInstance") ? 0 : 0x1000)));
        Assert.All(deviceNames, n => Assert.NotEqual(0, Gdpa(n)));
    }

    [Fact]
    public void GlobalCommandsResolveWithANullInstance()
    {
        foreach (var name in new[] { "vkCreateInstance", "vkEnumerateInstanceExtensionProperties", "vkEnumerateInstanceLayerProperties", "vkEnumerateInstanceVersion", "vkGetInstanceProcAddr" })
            Assert.NotEqual(0, Gipa(name));
        Assert.NotEqual(0, Gipa("vkCreateWebSurfaceBRUTAL", 0x1000));
        Assert.Equal(0, Gipa("vkNotARealCommand"));
        Assert.Equal(0, Gipa(""));
    }

    [Fact]
    public void RegistryHasAStubOrImplementationForEveryCommand()
    {
        // 804 wrapper structs (800 vk + 4 vx) + the 4 global commands + vkCreateWebSurfaceBRUTAL
        Assert.Equal(804, IcdStubTable.Count);
        Assert.Equal(809, Registry.AllNames.Count);
        Assert.True(Registry.ImplementedNames.Count > 200, $"{Registry.ImplementedNames.Count} implemented");
    }

    [Fact]
    public void ImplementedSetCoversThePocSurface()
    {
        // every command named in the PoC tables of docs/research/webgpu/brutal-abstractions-icd-surface.md
        var doc = File.ReadAllText(Path.Combine(TestIcd.RepoRoot, "docs/research/webgpu/brutal-abstractions-icd-surface.md"));
        var start = doc.IndexOf("# Calls made by Manual/Classes", StringComparison.Ordinal);
        var end = doc.IndexOf("# What the reference game uses beyond that", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        var names = Regex.Matches(doc[start..end], @"\bv[kx][A-Z][A-Za-z0-9]+")
            .Select(m => m.Value).Where(n => Registry.AllNames.Contains(n)).Distinct().ToArray();
        Assert.True(names.Length > 90, $"{names.Length} names parsed");
        var missing = names.Where(n => !Registry.ImplementedNames.Contains(n)).ToArray();
        Assert.True(missing.Length == 0, "not implemented: " + string.Join(", ", missing));

        // plus the hello-mesh browser checklist (desktop-hello-mesh-recipe.md) and the task's command list
        string[] required =
        [
            "vkCmdBeginRendering", "vkCmdEndRendering", "vkCmdPipelineBarrier2", "vkQueueSubmit2", "vkCmdCopyBufferToImage", "vkCmdCopyImageToBuffer",
            "vkCmdCopyImage", "vkCmdFillBuffer", "vkCmdUpdateBuffer", "vkCmdDrawIndirect", "vkCmdDrawIndexedIndirect", "vkCmdDispatch",
            "vkCmdDispatchIndirect", "vkCmdSetBlendConstants", "vkCmdSetStencilReference", "vkCmdExecuteCommands", "vkCmdSetEvent2",
            "vkCmdResetEvent2", "vkCmdWaitEvents2", "vkGetPhysicalDeviceFeatures2", "vkGetPhysicalDeviceProperties2",
            "vkGetPhysicalDeviceMemoryProperties2", "vkGetPhysicalDeviceQueueFamilyProperties2", "vkGetBufferMemoryRequirements2",
            "vkGetImageMemoryRequirements2", "vkBindBufferMemory2", "vkResetDescriptorPool", "vkFreeDescriptorSets", "vkCreatePipelineCache",
            "vkDestroyPipelineCache", "vkCreateComputePipelines", "vkGetFenceStatus", "vkCreateWebSurfaceBRUTAL", "vkSetDebugUtilsObjectNameEXT",
            "vkCmdInsertDebugUtilsLabelEXT", "vxDestroyImages", "vxDestroyImageViews", "vxDestroyBuffers", "vxDestroyBufferViews",
            "vkCmdBeginRenderingKHR", "vkQueueSubmit2KHR", "vkCmdPipelineBarrier2KHR",
        ];
        var notImplemented = required.Where(n => !Registry.ImplementedNames.Contains(n)).ToArray();
        Assert.True(notImplemented.Length == 0, "not implemented: " + string.Join(", ", notImplemented));
    }

    [Fact]
    public void CallingAStubThrowsVulkanUnsupportedExceptionWithTheName()
    {
        var fn = (VkCmdDrawMeshTasksEXT)new PfnVkVoidFunction((delegate*<void>)Gdpa("vkCmdDrawMeshTasksEXT"));
        var e = Assert.Throws<VulkanUnsupportedException>(() => fn.Invoke(new VkCommandBuffer(1), 1, 1, 1));
        Assert.Equal("vkCmdDrawMeshTasksEXT", e.Command);
        Assert.Contains("vkCmdDrawMeshTasksEXT", e.Message);

        var rt = (VkCreateRayTracingPipelinesKHR)new PfnVkVoidFunction((delegate*<void>)Gdpa("vkCreateRayTracingPipelinesKHR"));
        Assert.Equal("vkCreateRayTracingPipelinesKHR",
            Assert.Throws<VulkanUnsupportedException>(() => rt.Invoke(default, default, default, 0, null, null, null)).Command);
    }

    [Fact]
    public void OpcodeTableMatchesGlue()
    {
        var js = File.ReadAllText(Path.Combine(TestIcd.RepoRoot, "src/Brutal.Vulkan.WebGpu/wwwroot/webgpu-glue.js"));
        var opBlock = Regex.Match(js, @"export const OP = Object\.freeze\(\{(.*?)\}\);", RegexOptions.Singleline).Groups[1].Value;
        var jsOps = Regex.Matches(opBlock, @"(\w+):\s*(\d+)").ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
        var csOps = Enum.GetValues<Op>().ToDictionary(o => o.ToString(), o => (int)o);
        Assert.Equal(csOps.OrderBy(p => p.Value), jsOps.OrderBy(p => p.Value));

        var reservedBlock = Regex.Match(js, @"const RESERVED = Object\.freeze\(\{(.*?)\}\);", RegexOptions.Singleline).Groups[1].Value;
        var jsReserved = Regex.Matches(reservedBlock, @"(\w+):\s*(\d+)").ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
        var csReserved = typeof(ReservedHandles).GetFields().ToDictionary(f => f.Name, f => (int)f.GetRawConstantValue()!);
        Assert.Equal(csReserved.OrderBy(p => p.Key), jsReserved.OrderBy(p => p.Key));
    }

    [Fact]
    public void GeneratedStubsAreUpToDate()
    {
        var script = Path.Combine(TestIcd.RepoRoot, "tools/gen-icd-stubs.py");
        ProcessStartInfo psi = new("python3", ["-I", script, "--check"]) { RedirectStandardError = true, RedirectStandardOutput = true };
        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return; // no python3 on this machine
        }
        p!.WaitForExit(60000);
        Assert.True(p.ExitCode == 0, "tools/gen-icd-stubs.py --check: " + p.StandardError.ReadToEnd());
    }
}
