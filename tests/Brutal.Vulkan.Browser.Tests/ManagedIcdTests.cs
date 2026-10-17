using Brutal.Vulkan.Browser.Tests.Fakes;

namespace Brutal.Vulkan.Browser.Tests;

/// <summary>The one-per-process run of <see cref="FlavorScenario"/>; every test in <see cref="ManagedIcdTests"/> asserts on its report.</summary>
internal static class Scenario
{
    public static ScenarioReport Report => Lazy.Value;
    private static readonly Lazy<ScenarioReport> Lazy = new(FlavorScenario.Run);
}

/// <summary>
/// Drives the managed flavor of Brutal.Vulkan + Brutal.Vulkan.Abstractions through <see cref="ManagedIcd"/> (plain static C#
/// methods behind managed function pointers): instance -> physical device -> device -> queue -> buffer -> map/write/unmap
/// -> dispose, plus signature-shape probes that check what the ICD received.
/// </summary>
public class ManagedIcdTests
{
    private static ScenarioReport Report => Scenario.Report;

    private static void AssertPassed(params string[] ids)
    {
        foreach (var id in ids)
        {
            var entries = Report.Entries.Where(e => e.Id == id).ToList();
            Assert.True(entries.Count > 0, $"scenario entry {id} was not recorded");
            Assert.All(entries, e => Assert.True(e.Passed, $"{e.Id}: {e.Detail}"));
        }
    }

    private static void AssertAllPassed(string idPrefix)
    {
        var entries = Report.Where(idPrefix).ToList();
        Assert.True(entries.Count > 0, $"no scenario entries with prefix {idPrefix}");
        Assert.All(entries, e => Assert.True(e.Passed, $"{e.Id}: {e.Detail}"));
    }

    [Fact]
    public void Flow_InstanceToPhysicalDeviceToDeviceToQueue()
    {
        AssertPassed("S01-install", "S02-global-version", "S02-global-extensions", "S02-validation",
            "S03-instance", "S04-properties", "S04-properties2", "S04-features", "S04-memory-properties", "S04-queue-family",
            "S06-device", "S06-queue");
    }

    [Fact]
    public void Flow_AllocateBuffer_MapWriteUnmap_ThroughDeviceEx()
    {
        AssertPassed("S07-allocate-buffer", "S07-map-write-unmap");
        Assert.Contains(Report.IcdCalls, c => c.StartsWith("vkCreateBuffer(size=1024"));
        Assert.Contains(Report.IcdCalls, c => c.StartsWith("vkAllocateMemory(size=1024, type=1)"));
        Assert.Contains(Report.IcdCalls, c => c.StartsWith("vkUnmapMemory("));
    }

    [Fact]
    public void Flow_Dispose_DestroysDeviceAndInstance()
    {
        AssertPassed("S13-dispose");
        Assert.True(Array.IndexOf(Report.IcdCalls, "vkDestroyDevice(0x3000)") < Array.IndexOf(Report.IcdCalls, "vkDestroyInstance(0x1000)"));
    }

    [Fact]
    public void Flow_AllEntriesPass()
    {
        var failed = Report.Entries.Where(e => !e.Passed).Select(e => $"{e.Id}: {e.Detail}").ToList();
        Assert.True(failed.Count == 0, string.Join(Environment.NewLine, failed));
        Assert.True(Report.Entries.Count >= 50, $"expected the 50+ checks of the scenario, got {Report.Entries.Count}");
    }

    [Fact]
    public void ProcNames_Instance_AreTheFourGlobalsThen105TableEntriesThenGetDeviceProcAddr()
    {
        var names = Report.InstanceNames;
        Assert.Equal(110, names.Length);
        Assert.Equal(110, names.Distinct().Count());
        Assert.Equal(["vkCreateInstance", "vkEnumerateInstanceExtensionProperties", "vkEnumerateInstanceLayerProperties", "vkEnumerateInstanceVersion"], names[..4]);
        Assert.Equal("vkGetDeviceProcAddr", names[^1]);
        Assert.All(names, n => Assert.StartsWith("vk", n));
        // The four global commands are resolved with GIPA(NULL, name); everything after goes through the created instance.
        Assert.All(ManagedIcd.InstanceProcNames.Take(4), l => Assert.Contains("instance=NULL", l));
        Assert.All(ManagedIcd.InstanceProcNames.Skip(4), l => Assert.Contains("instance=0x1000", l));
    }

    [Fact]
    public void ProcNames_Device_Are697()
    {
        var names = Report.DeviceNames;
        Assert.Equal(697, names.Length);
        Assert.Equal(697, names.Distinct().Count());
        // BRUTAL adds four batch-destroy commands of its own (vx prefix); the ICD must answer these names too.
        Assert.Equal(["vxDestroyImages", "vxDestroyImageViews", "vxDestroyBuffers", "vxDestroyBufferViews"], names.Where(n => n.StartsWith("vx")));
        Assert.All(names.Where(n => !n.StartsWith("vx")), n => Assert.StartsWith("vk", n));
        Assert.DoesNotContain("vkGetDeviceProcAddr", names); // an instance-level name
    }

    [Fact]
    public void ProcNames_AreRequestedPerCreatedInstanceAndDevice()
    {
        // The scenario creates a second instance + device with name logging off: 105 table entries + vkGetDeviceProcAddr
        // again for the instance, 697 again for the device. The logged lists above hold the first instance/device only.
        Assert.Equal(110 + 106, Report.InstanceNameRequests);
        Assert.Equal(2 * 697, Report.DeviceNameRequests);
        // 41 entry points are implemented; the rest answer NULL here (the real ICD returns throwing stubs, never NULL).
        Assert.Equal(41, ManagedIcd.ImplementedCount);
        Assert.True(ManagedIcd.InstanceLookupsNull > 0 && ManagedIcd.DeviceLookupsNull > 0);
    }

    [Fact]
    public void Loader_UsesTheSuppliedProcAddr()
    {
        Assert.NotEqual(0, Report.SuppliedGetInstanceProcAddr);
        Assert.Equal(Report.SuppliedGetInstanceProcAddr, Report.LoadedGetInstanceProcAddr);
        AssertPassed("S02-gipa-identity");
    }

    [Fact]
    public void Probes_EightAndTenArguments()
    {
        AssertAllPassed("P02-bindsets"); // vkCmdBindDescriptorSets: 8 args
        AssertAllPassed("P12-pipelinebarrier"); // vkCmdPipelineBarrier: 10 args
        AssertAllPassed("P13-traceraysnv"); // vkCmdTraceRaysNV: 15 args, 7 x ByteSize64 by value
        Assert.Equal(1920, ManagedIcd.Probe.TrW);
        Assert.Equal(-3, ManagedIcd.Probe.TrD);
    }

    [Fact]
    public void Probes_Floats()
    {
        AssertAllPassed("P03-depthbias");
        AssertAllPassed("P04-blend");
        Assert.Equal(3.125e-7f, ManagedIcd.Probe.DbSlope);
        Assert.Equal(1.5f, ManagedIcd.Probe.Bc3);
    }

    [Fact]
    public void Probes_SixtyFourBitEnumByValue_ArrivesIntact()
    {
        AssertAllPassed("P07-timestamp2");
        Assert.Equal(0x1_0000_0002UL, ManagedIcd.Probe.Wt2Stage); // VkPipelineStageFlags2 is `enum : ulong`
        AssertAllPassed("P06-drawindirect");
        Assert.Equal(0x1_0000_0002UL, ManagedIcd.Probe.DiiOffset); // ByteSize64 by value
        Assert.Equal(0x1_0000_0002UL, ManagedIcd.Probe.MapOffset);
        Assert.Equal(0x3_0000_0004UL, ManagedIcd.Probe.MapSize);
    }

    [Fact]
    public void Probes_SixtyFourBitReturnValue()
    {
        AssertAllPassed("P08-deviceaddress");
    }

    [Fact]
    public void Probes_PointersStructsAndNintArguments()
    {
        AssertAllPassed("P01-objname");
        AssertAllPassed("P09-pushconstants");
        AssertAllPassed("P10-waitforfences");
        AssertAllPassed("P11-mapmemory");
        Assert.Equal(unchecked((nint)0x1_0000_0002L), (nint)ManagedIcd.Probe.WffTimeout); // BRUTAL types uint64_t timeout as nint
    }

    [Fact]
    public void IcdToApplicationCallbacks_UseManagedFunctionPointers()
    {
        AssertPassed("S05-callback-app", "S05-callback-abstractions");
    }

    [Fact]
    public void VkResultErrors_SurfaceAsVkResultError()
    {
        AssertPassed("S09-vkresult-error");
    }

    [Fact]
    public void ManagedExceptionInsideIcd_PropagatesToTheCaller_AndTheRuntimeKeepsWorking()
    {
        AssertPassed("S10-icd-exception", "S10-after-exception");
    }
}
