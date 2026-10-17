// exp-06: the scenario run by apps/DesktopFlavorSmoke (CoreCLR JIT) and apps/BrowserFlavorSmoke (Mono interpreter,
// browser-wasm, no wasm-tools). Drives the managed-fnptr flavor of Brutal.Vulkan + Brutal.Vulkan.Abstractions through
// ManagedIcd and prints one "STEP <id> PASS|FAIL ..." line per check, probe round-trips, timings and benchmarks.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace Exp06;

public static unsafe class FlavorScenario
{
    public sealed record Options(bool Bench = true, int BenchIterations = 1_000_000, bool CallNull = false);

    private static Action<string> _print = Console.WriteLine;
    private static int _pass, _fail;

    private static void Step(string id, bool ok, string detail)
    {
        if (ok) _pass++; else _fail++;
        _print($"STEP {id} {(ok ? "PASS" : "FAIL")} {detail}");
    }

    private static void Check<T>(string id, string what, T sent, T received)
    {
        var ok = EqualityComparer<T>.Default.Equals(sent, received);
        if (ok) _pass++; else _fail++;
        _print($"PROBE {id} {(ok ? "PASS" : "FAIL")} {what}: sent={Fmt(sent)} received={Fmt(received)}");
    }

    private static string Fmt<T>(T v) => v switch
    {
        ulong u => $"0x{u:X}",
        long l => $"0x{l:X}",
        nint n => $"0x{n:X}",
        float f => f.ToString("R"),
        _ => v?.ToString() ?? "null",
    };

    public static int Run(Action<string> print, Options opt)
    {
        _print = print;
        var isMono = Type.GetType("Mono.RuntimeStructs") != null;
        print($"ENV os={RuntimeInformation.OSDescription} rid={RuntimeInformation.RuntimeIdentifier} framework={RuntimeInformation.FrameworkDescription} isBrowser={OperatingSystem.IsBrowser()} mono={isMono} nint={sizeof(nint) * 8}-bit stopwatchHz={Stopwatch.Frequency}");
        var vkAsm = typeof(Vulkan).Assembly;
        var absAsm = typeof(DeviceEx).Assembly;
        var vkInfo = vkAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        var absInfo = absAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        Step("S00-assemblies", vkInfo.Contains("exp06-managed") && absInfo.Contains("exp06-managed"),
            $"Brutal.Vulkan={vkInfo}; Brutal.Vulkan.Abstractions={absInfo}");
        var invokeField = typeof(VkCmdDraw).GetField("Invoke")!;
        Step("S00-fnptr-type", !invokeField.FieldType.IsUnmanagedFunctionPointer,
            $"VkCmdDraw.Invoke field type: IsFunctionPointer={invokeField.FieldType.IsFunctionPointer} IsUnmanagedFunctionPointer={invokeField.FieldType.IsUnmanagedFunctionPointer}");

        // 1. Install the managed vkGetInstanceProcAddr BEFORE anything touches Brutal.VulkanApi.Vulkan.
        ManagedIcd.Install();
        var gipa = ManagedIcd.GetInstanceProcAddrPointer;
        Step("S01-install", gipa != 0, $"AppContext[{ManagedIcd.AppContextKey}] = 0x{gipa:x} (managed &ManagedIcd.GetInstanceProcAddr; ICD implements {ManagedIcd.ImplementedCount} names)");

        // 2. Global commands through vkGetInstanceProcAddr(NULL, ...).
        var sw = Stopwatch.StartNew();
        int apiVersion;
        Vulkan.EnumerateInstanceVersion(&apiVersion);
        var tCctor = sw.Elapsed.TotalMilliseconds;
        Step("S02-global-version", apiVersion == ((1 << 22) | (3 << 12)), $"vkEnumerateInstanceVersion -> {ManagedIcd.FormatVersion(apiVersion)} (first touch incl. Vulkan cctor: {tCctor:F2} ms)");
        var extCount = Vulkan.EnumerateInstanceExtensionProperties_Count([]);
        Step("S02-global-extensions", extCount == 4, $"vkEnumerateInstanceExtensionProperties count -> {extCount}");
        Step("S02-validation", !VulkanHelpers.Validation.IsSupported, $"VulkanHelpers.Validation.IsSupported -> {VulkanHelpers.Validation.IsSupported} (no layers)");
        Step("S02-gipa-identity", (nint)Vulkan.GetInstanceProcAddr.Invoke == gipa, $"Vulkan.GetInstanceProcAddr.Invoke == supplied pointer");

        // 3. Instance (vkCreateInstance + 105 table lookups + 2x vkEnumeratePhysicalDevices).
        var lookupsBefore = ManagedIcd.InstanceLookups;
        Instance instance;
        sw.Restart();
        instance = CreateInstance();
        var tInstance = sw.Elapsed.TotalMilliseconds;
        var instLookups = ManagedIcd.InstanceLookups - lookupsBefore;
        Step("S03-instance", instance.VkHandle == ManagedIcd.FakeInstance && instance.PhysicalDevices.Length == 1,
            $"Vulkan.CreateInstance: handle=0x{instance.VkHandle:x} physicalDevices={instance.PhysicalDevices.Length} tableLookups={instLookups} ({ManagedIcd.InstanceLookupsNull} NULL so far) time={tInstance:F2} ms ({tInstance * 1000 / Math.Max(1, instLookups):F2} us/lookup incl. create+enumerate)");

        // 4. Physical device queries.
        var pd = instance.PhysicalDevices[0];
        var props = pd.GetProperties();
        Step("S04-properties", pd.GetDeviceName() == ManagedIcd.DeviceName && props.DeviceType == VkPhysicalDeviceType.VirtualGpu,
            $"'{pd.GetDeviceName()}' api={ManagedIcd.FormatVersion(props.ApiVersion)} type={props.DeviceType} vendor=0x{props.VendorID:x}");
        var props2 = pd.GetProperties2();
        Step("S04-properties2", VulkanDeviceInfo.GetDeviceName(props2.Properties) == ManagedIcd.DeviceName, $"GetProperties2 -> '{VulkanDeviceInfo.GetDeviceName(props2.Properties)}' sType={*(VkStructureType*)&props2}");
        var feats = pd.GetFeatures();
        Step("S04-features", feats.RobustBufferAccess && feats.ShaderInt64 && !feats.GeometryShader, $"robustBufferAccess={(bool)feats.RobustBufferAccess} shaderInt64={(bool)feats.ShaderInt64} geometryShader={(bool)feats.GeometryShader}");
        var memProps = pd.GetMemoryProperties();
        Step("S04-memory-properties", memProps.MemoryTypeCount == 2 && memProps.MemoryTypes[1].PropertyFlags.HasFlag(VkMemoryPropertyFlags.HostVisibleBit),
            $"types={memProps.MemoryTypeCount} [0]={memProps.MemoryTypes[0].PropertyFlags} [1]={memProps.MemoryTypes[1].PropertyFlags} heaps={memProps.MemoryHeapCount}");
        var family = pd.FindQueueFamily(VkQueueFlags.GraphicsBit);
        Step("S04-queue-family", family == 0, $"FindQueueFamily(Graphics) -> {family}");

        // 5. ICD -> application callbacks through managed function pointers (debug-utils messenger).
        DebugMessengerChecks(instance);

        // 6. Device (DeviceEx via Abstractions): vkCreateDevice + vkGetDeviceProcAddr + 697 table lookups.
        var devLookupsBefore = ManagedIcd.DeviceLookups;
        sw.Restart();
        var device = CreateDevice(pd, family);
        var tDevice = sw.Elapsed.TotalMilliseconds;
        var devLookups = ManagedIcd.DeviceLookups - devLookupsBefore;
        Step("S06-device", device.VkHandle == ManagedIcd.FakeDevice && devLookups == 697,
            $"PhysicalDevice.CreateDevice (DeviceEx): handle=0x{device.VkHandle:x} vkGetDeviceProcAddr lookups={devLookups} ({ManagedIcd.DeviceLookupsNull} NULL) time={tDevice:F2} ms ({tDevice * 1000 / Math.Max(1, devLookups):F2} us/lookup)");
        var queue = device.GetQueue(family, 0);
        Step("S06-queue", queue.VkHandle == ManagedIcd.FakeQueue, $"Device.GetQueue(0,0) -> 0x{queue.VkHandle:x}");

        // Second instance/device with proc-name logging off: lookup cost without the ICD's string bookkeeping.
        ManagedIcd.LogProcNames = false;
        sw.Restart();
        var instance2 = CreateInstance();
        var tInstance2 = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var device2 = CreateDevice(instance2.PhysicalDevices[0], 0);
        var tDevice2 = sw.Elapsed.TotalMilliseconds;
        device2.Dispose();
        instance2.Dispose();
        ManagedIcd.LogProcNames = true;
        print($"TIMING create-instance first={tInstance:F2} ms warm(no name log)={tInstance2:F2} ms; create-device first={tDevice:F2} ms warm(no name log)={tDevice2:F2} ms ({tDevice2 * 1000 / 697:F2} us per device-level lookup)");

        // 7. Buffer through Abstractions: create, memory requirements, memory type selection, allocate, bind, map/write/unmap.
        var bufInfo = device.AllocateBuffer(
            "exp06-buffer",
            new VkBufferCreateInfo
            {
                Size = new ByteSize64(1024),
                Usage = VkBufferUsageFlags.StorageBufferBit | VkBufferUsageFlags.TransferDstBit,
                SharingMode = VkSharingMode.Exclusive,
            },
            new AllocationInfo.CreateInfo(MemoryPreference.PreferCpu, VkMemoryPropertyFlags.HostVisibleBit, VkMemoryPropertyFlags.None, 1.0f, null)
        );
        Step("S07-allocate-buffer", bufInfo.AllocationInfo.MemoryTypeIndex == 1 && ManagedIcd.Probe.BindOffset == 0,
            $"AllocateBuffer: buffer=0x{bufInfo.VkBuffer.VkHandle:x} memory=0x{bufInfo.AllocationInfo.Memory.VkHandle:x} memoryType={bufInfo.AllocationInfo.MemoryTypeIndex} (HOST_VISIBLE|HOST_COHERENT) allocSize={bufInfo.AllocationInfo.AllocSize}");
        var bufferEx = new BufferEx(bufInfo);
        var mapped = bufferEx.Map();
        var p = (byte*)(void*)mapped.MapPtr;
        for (var i = 0; i < 1024; i++) p[i] = (byte)(i * 7 + 3);
        mapped.Unmap();
        var shadow = ManagedIcd.Shadow(bufInfo.AllocationInfo.Memory);
        var shadowOk = shadow.Length == 1024;
        for (var i = 0; shadowOk && i < 1024; i++) shadowOk = shadow[i] == (byte)(i * 7 + 3);
        Step("S07-map-write-unmap", shadowOk && ManagedIcd.Probe.MapOffset == 0 && ManagedIcd.Probe.MapSize == 1024,
            $"BufferEx.Map() -> vkMapMemory(offset=0x{ManagedIcd.Probe.MapOffset:x}, size=0x{ManagedIcd.Probe.MapSize:x}) ptr=0x{(nint)p:x}; wrote 1024 bytes; ICD shadow memory matches={shadowOk}");

        // 8. Signature-shape probes through the real Brutal.Vulkan wrappers.
        Probes(device, bufInfo);

        // 9. VkResult error codes surface as VkResultError (generated switch in the wrapper).
        ManagedIcd.NextAllocateMemoryResult = VkResult.ErrorOutOfDeviceMemory;
        try
        {
            device.AllocateMemory(new VkMemoryAllocateInfo { AllocationSize = new ByteSize64(64), MemoryTypeIndex = 0 }, null);
            Step("S09-vkresult-error", false, "no exception");
        }
        catch (VkResultError e)
        {
            Step("S09-vkresult-error", e.Message.Contains("VK_ERROR_OUT_OF_DEVICE_MEMORY"), $"{e.GetType().Name}: {e.Message}");
        }

        // 10. A managed exception thrown inside the ICD propagates through the managed calli to the caller.
        var cb = new CommandBuffer(device, new VkCommandBuffer(0x5000));
        try
        {
            cb.Dispatch(0xDEAD, 2, 3);
            Step("S10-icd-exception", false, "no exception");
        }
        catch (InvalidOperationException e)
        {
            var frames = new StackTrace(e, false).GetFrames().Select(f => DiagnosticMethodInfo.Create(f)?.Name ?? "?").Take(4);
            Step("S10-icd-exception", e.Message.Contains("ManagedIcd.CmdDispatch"),
                $"caught {e.GetType().Name} \"{e.Message}\"; top frames=[{string.Join(" <- ", frames)}]");
        }
        var before = ManagedIcd.BenchCounter;
        cb.Dispatch(1, 1, 1);
        Step("S10-after-exception", ManagedIcd.BenchCounter == before + 1, "runtime and ICD still usable after the exception (cb.Dispatch(1,1,1) ran)");

        // 11. Benchmarks.
        if (opt.Bench)
            Benchmarks(device, cb, opt.BenchIterations);

        // 12. NULL table entry (vkCmdDraw is not implemented by the ICD).
        if (opt.CallNull)
            CallNull(device, cb);

        // 13. Dispose.
        device.FreeAllocation(bufInfo);
        device.Dispose();
        instance.Dispose();
        Step("S13-dispose", ManagedIcd.Calls.Contains("vkDestroyDevice(0x3000)") && ManagedIcd.Calls.Contains("vkDestroyInstance(0x1000)"), "FreeAllocation + Device.Dispose + Instance.Dispose");

        print($"SUMMARY vkGetInstanceProcAddr requests={ManagedIcd.InstanceLookups} (NULL {ManagedIcd.InstanceLookupsNull}); vkGetDeviceProcAddr requests={ManagedIcd.DeviceLookups} (NULL {ManagedIcd.DeviceLookupsNull}); logged names inst={ManagedIcd.InstanceProcNames.Count} dev={ManagedIcd.DeviceProcNames.Count}");
        print("ICD call log:");
        foreach (var c in ManagedIcd.Calls)
            print("  " + c);
        print($"RESULT: {(_fail == 0 ? "OK" : "FAIL")} pass={_pass} fail={_fail}");
        return _fail == 0 ? 0 : 1;
    }

    private static Instance CreateInstance()
    {
        var appName = "exp-06 FlavorScenario\0"u8;
        fixed (byte* pAppName = appName)
        {
            return Vulkan.CreateInstance(
                new Instance.CreateInfo
                {
                    AppInfo = new VkApplicationInfo { ApplicationName = pAppName, ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3 },
                    Flags = VkInstanceCreateFlags.EnumeratePortabilityBitKHR,
                    EnabledExtensions = [VK.KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME, VK.KHR_GET_PHYSICAL_DEVICE_PROPERTIES_2_EXTENSION_NAME, VK.EXT_DEBUG_UTILS_EXTENSION_NAME],
                    EnabledLayers = [],
                }
            );
        }
    }

    private static DeviceEx CreateDevice(PhysicalDevice pd, int family)
    {
        var priority = 1.0f;
        Span<VkDeviceQueueCreateInfo> queues = [new VkDeviceQueueCreateInfo { QueueFamilyIndex = family, QueueCount = 1, QueuePriorities = &priority }];
        return pd.CreateDevice(new DeviceEx.CreateInfo { QueueCreateInfos = queues });
    }

    // ---- ICD -> app callbacks --------------------------------------------------------------------------------------
    private static string? _appCallbackMessage;
    private static VkDebugUtilsMessageSeverityFlagsEXT _appCallbackSeverity;

    // A plain static method: in the managed flavor the ICD calls it through a managed delegate*<...>.
    private static Bool32 AppDebugCallback(VkDebugUtilsMessageSeverityFlagsEXT severity, VkDebugUtilsMessageTypeFlagsEXT types, VkDebugUtilsMessengerCallbackDataEXT* data, void* userData)
    {
        _appCallbackSeverity = severity;
        _appCallbackMessage = Marshal.PtrToStringUTF8((nint)data->Message);
        return false;
    }

    private static void DebugMessengerChecks(Instance instance)
    {
        var ci = VulkanHelpers.BuildDefaultDebugMessengerCreateInfo(
            new PfnVkDebugUtilsMessengerCallbackEXT(&AppDebugCallback));
        var messenger = instance.CreateDebugUtilsMessengerEXT(ci, null);
        Step("S05-callback-app", _appCallbackMessage == "exp-06 ICD: debug messenger created" && ManagedIcd.MessengerCallbackReturns == 0,
            $"ICD called the app's managed callback: severity={_appCallbackSeverity} message=\"{_appCallbackMessage}\" returned={ManagedIcd.MessengerCallbackReturns}");
        instance.DestroyDebugUtilsMessengerEXT(messenger, null);

        // Abstractions' own callback. Upstream it is [UnmanagedCallersOnly]; transform rule R5 makes it a plain static
        // method, so `&VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet` is a valid managed function pointer.
        ManagedIcd.MessengerCallbackReturns = -1;
        var ci2 = VulkanHelpers.BuildDefaultDebugMessengerCreateInfo(
            new PfnVkDebugUtilsMessengerCallbackEXT(&VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet));
        var messenger2 = instance.CreateDebugUtilsMessengerEXT(ci2, null);
        Step("S05-callback-abstractions", ManagedIcd.MessengerCallbackReturns == 0,
            $"ICD called VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet (UCO attribute stripped by R5): returned={ManagedIcd.MessengerCallbackReturns}");
        instance.DestroyDebugUtilsMessengerEXT(messenger2, null);
    }

    // ---- probes -----------------------------------------------------------------------------------------------------
    private static void Probes(DeviceEx device, BufferAllocationInfo bufInfo)
    {
        var cb = new CommandBuffer(device, new VkCommandBuffer(0x5000));
        var big = 0x1_0000_0002UL; // the value exp-01 saw truncated to 0x2 on an interp->native calli

        // vkSetDebugUtilsObjectNameEXT (struct with nint object handle + string)
        var name = "exp06-object\0"u8;
        fixed (byte* pName = name)
            device.SetDebugUtilsObjectNameEXT(new VkDebugUtilsObjectNameInfoEXT { ObjectType = VkObjectType.Buffer, ObjectHandle = bufInfo.VkBuffer.VkHandle, ObjectName = pName });
        Check("P01-objname", "vkSetDebugUtilsObjectNameEXT name", "exp06-object", ManagedIcd.Probe.ObjectName);
        Check("P01-objname", "vkSetDebugUtilsObjectNameEXT objectHandle", (long)bufInfo.VkBuffer.VkHandle, ManagedIcd.Probe.ObjectNameHandle);

        // vkCmdBindDescriptorSets: 8 args (handle, enum, handle, int, int, ptr, int, ptr)
        ReadOnlySpan<VkDescriptorSet> sets = [new VkDescriptorSet(0x8001), new VkDescriptorSet(0x8002), new VkDescriptorSet(0x8003)];
        Span<ByteSize32> dyn = [(ByteSize32)256u, (ByteSize32)0x7FFF_FF00u];
        cb.BindDescriptorSets(VkPipelineBindPoint.Compute, new VkPipelineLayout(0x7001), 2, sets, dyn);
        Check("P02-bindsets", "commandBuffer", (nint)0x5000, ManagedIcd.Probe.BdsCb);
        Check("P02-bindsets", "bindPoint", VkPipelineBindPoint.Compute, ManagedIcd.Probe.BdsBindPoint);
        Check("P02-bindsets", "layout", (nint)0x7001, ManagedIcd.Probe.BdsLayout);
        Check("P02-bindsets", "firstSet/count", (2, 3), (ManagedIcd.Probe.BdsFirstSet, ManagedIcd.Probe.BdsSetCount));
        Check("P02-bindsets", "sets[0]/sets[2]", ((nint)0x8001, (nint)0x8003), (ManagedIcd.Probe.BdsSet0, ManagedIcd.Probe.BdsSetLast));
        Check("P02-bindsets", "dynCount/dyn[0]/dyn[1]", (2, 256, 0x7FFF_FF00), (ManagedIcd.Probe.BdsDynCount, ManagedIcd.Probe.BdsDyn0, ManagedIcd.Probe.BdsDynLast));

        // vkCmdSetDepthBias: 3 floats by value
        cb.SetDepthBias(1.5f, -2.25f, 3.125e-7f);
        Check("P03-depthbias", "floats", (1.5f, -2.25f, 3.125e-7f), (ManagedIcd.Probe.DbConstant, ManagedIcd.Probe.DbClamp, ManagedIcd.Probe.DbSlope));

        // vkCmdSetBlendConstants: float*
        var blend = stackalloc float[4] { 0.25f, 0.5f, 0.75f, 1.5f };
        cb.SetBlendConstants(blend);
        Check("P04-blend", "float[4]", (0.25f, 0.5f, 0.75f, 1.5f), (ManagedIcd.Probe.Bc0, ManagedIcd.Probe.Bc1, ManagedIcd.Probe.Bc2, ManagedIcd.Probe.Bc3));

        // vkCmdDrawIndexed: 5 ints (one negative)
        cb.DrawIndexed(36, 2, 3, -4, 5);
        Check("P05-drawindexed", "5 ints", (36, 2, 3, -4, 5), (ManagedIcd.Probe.DiIndexCount, ManagedIcd.Probe.DiInstanceCount, ManagedIcd.Probe.DiFirstIndex, ManagedIcd.Probe.DiVertexOffset, ManagedIcd.Probe.DiFirstInstance));

        // vkCmdDrawIndexedIndirect: handle, ByteSize64 (8-byte struct by value), int, int
        cb.DrawIndexedIndirect(bufInfo.VkBuffer, new ByteSize64(big), 7, (ByteSize32)20u);
        Check("P06-drawindirect", "offset (ByteSize64 by value)", big, ManagedIcd.Probe.DiiOffset);
        Check("P06-drawindirect", "buffer/drawCount/stride", (bufInfo.VkBuffer.VkHandle, 7, 20), (ManagedIcd.Probe.DiiBuffer, ManagedIcd.Probe.DiiDrawCount, ManagedIcd.Probe.DiiStride));

        // vkCmdWriteTimestamp2: VkPipelineStageFlags2 is `enum : ulong` - a 64-bit enum by value
        var stage = VkPipelineStageFlags2.CopyBit | VkPipelineStageFlags2.DrawIndirectBit; // == 0x1_0000_0002
        cb.WriteTimestamp2(stage, new VkQueryPool(0x9001), 11);
        Check("P07-timestamp2", "stage (64-bit enum by value)", big, ManagedIcd.Probe.Wt2Stage);
        Check("P07-timestamp2", "queryPool/query", ((nint)0x9001, 11), (ManagedIcd.Probe.Wt2Pool, ManagedIcd.Probe.Wt2Query));

        // vkGetBufferDeviceAddress: 64-bit struct return
        var addr = device.GetBufferDeviceAddress(new VkBufferDeviceAddressInfo { Buffer = bufInfo.VkBuffer });
        Check("P08-deviceaddress", "returned VkDeviceAddress (64-bit)", ManagedIcd.FakeDeviceAddress + (ulong)bufInfo.VkBuffer.VkHandle, addr.Value);

        // vkCmdPushConstants: handle, flags, int, int, void*
        Span<byte> pc = stackalloc byte[32];
        MemoryMarshal.Write(pc, 1.5f);
        MemoryMarshal.Write(pc[28..], 0xCAFEBABEu);
        cb.PushConstants(new VkPipelineLayout(0x7002), VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit, 16, pc);
        Check("P09-pushconstants", "layout/stages/offset/size", ((nint)0x7002, VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit, 16, 32),
            (ManagedIcd.Probe.PcLayout, ManagedIcd.Probe.PcStages, ManagedIcd.Probe.PcOffset, ManagedIcd.Probe.PcSize));
        Check("P09-pushconstants", "payload first float / last word", (1.5f, 0xCAFEBABEu), (ManagedIcd.Probe.PcFirstFloat, ManagedIcd.Probe.PcLastWord));

        // vkWaitForFences: BRUTAL types the uint64_t timeout as nint (32-bit on wasm32).
        ReadOnlySpan<VkFence> fences = [new VkFence(0xA001)];
        var timeout = unchecked((nint)(long)big); // == 0x100000002 on 64-bit, 0x2 on wasm32 (truncated by the C# type, before any call)
        var r = device.WaitForFences(fences, true, timeout);
        Check("P10-waitforfences", "timeout (nint, as typed by BRUTAL)", (long)timeout, ManagedIcd.Probe.WffTimeout);
        Check("P10-waitforfences", "count/fence0/waitAll/result", (1, (nint)0xA001, true, VkResult.Success), (ManagedIcd.Probe.WffCount, ManagedIcd.Probe.WffFence0, ManagedIcd.Probe.WffWaitAll, r));
        _print($"NOTE P10 uint64_t timeout typed as nint by BRUTAL: sizeof(nint)={sizeof(nint)}; 0x100000002 {(sizeof(nint) == 8 ? "is representable" : "is truncated to 0x" + timeout.ToString("X") + " by the C# type (not by the call)")}");

        // vkMapMemory with 64-bit offset and size through the raw FunctionTable entry (ByteSize64 x2 + flags + void**).
        void* mappedPtr = null;
        var mapResult = device.PInvoke.vkMapMemory.Invoke(device.Handle, bufInfo.AllocationInfo.Memory, new ByteSize64(big), new ByteSize64(0x3_0000_0004UL), VkMemoryMapFlags.None, &mappedPtr);
        Check("P11-mapmemory", "offset/size (2x ByteSize64 by value)", (big, 0x3_0000_0004UL), (ManagedIcd.Probe.MapOffset, ManagedIcd.Probe.MapSize));
        Check("P11-mapmemory", "result/ptr!=null", (VkResult.Success, true), (mapResult, mappedPtr != null));
        device.UnmapMemory(bufInfo.AllocationInfo.Memory);

        // vkCmdPipelineBarrier: 10 args (handle, 2 enums, flags, 3 x (count, pointer)) - beyond the 6-arg interp->native fast path
        ReadOnlySpan<VkBufferMemoryBarrier> bufBarriers = [new VkBufferMemoryBarrier { Buffer = bufInfo.VkBuffer, Offset = new ByteSize64(big), Size = new ByteSize64(0x3_0000_0004UL) }];
        ReadOnlySpan<VkImageMemoryBarrier> imgBarriers = [new VkImageMemoryBarrier { Image = new VkImage(0xC001), NewLayout = VkImageLayout.TransferDstOptimal }, new VkImageMemoryBarrier { Image = new VkImage(0xC002) }];
        cb.PipelineBarrier(VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.FragmentShaderBit, VkDependencyFlags.ByRegionBit, [], bufBarriers, imgBarriers);
        Check("P12-pipelinebarrier", "src/dst/dep", (VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.FragmentShaderBit, VkDependencyFlags.ByRegionBit), (ManagedIcd.Probe.PbSrc, ManagedIcd.Probe.PbDst, ManagedIcd.Probe.PbDep));
        Check("P12-pipelinebarrier", "counts mem/buf/img", (0, 1, 2), (ManagedIcd.Probe.PbMemCount, ManagedIcd.Probe.PbBufCount, ManagedIcd.Probe.PbImgCount));
        Check("P12-pipelinebarrier", "bufBarrier[0] buffer/offset/size (64-bit in struct)", (bufInfo.VkBuffer.VkHandle, big, 0x3_0000_0004UL), (ManagedIcd.Probe.PbBuf0, ManagedIcd.Probe.PbBufOffset0, ManagedIcd.Probe.PbBufSize0));
        Check("P12-pipelinebarrier", "imgBarrier[0] image/newLayout", ((nint)0xC001, VkImageLayout.TransferDstOptimal), (ManagedIcd.Probe.PbImg0, ManagedIcd.Probe.PbImgNewLayout0));

        // vkCmdTraceRaysNV: the widest generated signature - 15 args, 7 of them ByteSize64 (8-byte struct) by value
        cb.TraceRaysNV(new VkBuffer(0xB001), new ByteSize64(0x1_0000_0001UL), new VkBuffer(0xB002), new ByteSize64(0x2_0000_0002UL), new ByteSize64(0x3_0000_0003UL),
            new VkBuffer(0xB003), new ByteSize64(0x4_0000_0004UL), new ByteSize64(0x5_0000_0005UL), new VkBuffer(0xB004), new ByteSize64(0x6_0000_0006UL), new ByteSize64(0x7_0000_0007UL),
            1920, 1080, -3);
        Check("P13-traceraysnv", "4 buffer handles", "0xB001,0xB002,0xB003,0xB004", string.Join(",", ManagedIcd.Probe.TrBuffers.Select(h => $"0x{h:X}")));
        Check("P13-traceraysnv", "7 x ByteSize64 by value", "0x100000001,0x200000002,0x300000003,0x400000004,0x500000005,0x600000006,0x700000007", string.Join(",", ManagedIcd.Probe.TrU64.Select(v => $"0x{v:X}")));
        Check("P13-traceraysnv", "width/height/depth", (1920, 1080, -3), (ManagedIcd.Probe.TrW, ManagedIcd.Probe.TrH, ManagedIcd.Probe.TrD));
    }

    // ---- benchmarks -------------------------------------------------------------------------------------------------
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int AddDirect(int a, int b) => a + b;

    private static double Time(string label, int iters, Func<int, long> body, List<(string, double)> results)
    {
        body(Math.Min(iters, 20000)); // warm-up (interpreter tiering / JIT tier-up), as exp-01
        var best = double.MaxValue;
        var all = new List<string>();
        for (var rep = 0; rep < 3; rep++)
        {
            var sw = Stopwatch.StartNew();
            body(iters);
            sw.Stop();
            var ns = sw.Elapsed.TotalMilliseconds * 1e6 / iters;
            all.Add(ns.ToString("F1"));
            best = Math.Min(best, ns);
        }
        results.Add((label, best));
        _print($"BENCH {label}: {best:F1} ns/call (best of 3 x {iters:N0}; runs={string.Join("/", all)})");
        return best;
    }

    private static void Benchmarks(DeviceEx device, CommandBuffer cb, int n)
    {
        var results = new List<(string, double)>();
        var counterBefore = ManagedIcd.BenchCounter;
        var cbHandle = cb.Handle;
        var devHandle = device.Handle;
        var sets = (VkDescriptorSet*)NativeMemory.Alloc(3, (nuint)sizeof(VkDescriptorSet));
        var dynRaw = (int*)NativeMemory.Alloc(2, sizeof(int));
        sets[0] = new VkDescriptorSet(1); sets[1] = new VkDescriptorSet(2); sets[2] = new VkDescriptorSet(3);
        dynRaw[0] = 0; dynRaw[1] = 256;
        var setsArr = new[] { new VkDescriptorSet(1), new VkDescriptorSet(2), new VkDescriptorSet(3) };
        var dynArr = new[] { (ByteSize32)0u, (ByteSize32)256u };
        var layout = new VkPipelineLayout(0x7001);
        var stage = VkPipelineStageFlags2.CopyBit | VkPipelineStageFlags2.DrawIndirectBit;
        var pool = new VkQueryPool(0x9001);

        // In-run baselines (same shapes as exp-01 T13a / T13f)
        Time("baseline: direct static call AddDirect(int,int) [exp-01 T13a shape]", n, k => { long s = 0; for (var i = 0; i < k; i++) s += AddDirect(i, 1); return s; }, results);
        delegate*<int, int, int> add = &AddDirect;
        Time("baseline: delegate*<int,int,int> -> static [exp-01 T13f shape]", n, k => { long s = 0; for (var i = 0; i < k; i++) s += add(i, 1); return s; }, results);
        Time("baseline: direct call ManagedIcd.CmdEndRenderPass(cb) (no fnptr)", n, k => { for (var i = 0; i < k; i++) ManagedIcd.CmdEndRenderPass(cbHandle); return 0; }, results);

        // Through Brutal.Vulkan (managed fnptr tables)
        Time("cb.EndRenderPass() wrapper -> vkCmdEndRenderPass (1 arg, void)", n, k => { for (var i = 0; i < k; i++) cb.EndRenderPass(); return 0; }, results);
        Time("device.PInvoke.vkGetDeviceQueue.Invoke (4 args incl. out ptr)", n, k => { VkQueue q; long s = 0; for (var i = 0; i < k; i++) { device.PInvoke.vkGetDeviceQueue.Invoke(devHandle, 0, 0, &q); s += q.VkHandle; } return s; }, results);
        Time("cb.PInvoke.vkCmdBindDescriptorSets.Invoke raw (8 args)", n, k => { for (var i = 0; i < k; i++) cb.PInvoke.vkCmdBindDescriptorSets.Invoke(cbHandle, VkPipelineBindPoint.Graphics, layout, 0, 3, sets, 2, dynRaw); return 0; }, results);
        Time("cb.BindDescriptorSets(...) wrapper (8-arg shape + 2x fixed spans)", n, k => { for (var i = 0; i < k; i++) cb.BindDescriptorSets(VkPipelineBindPoint.Graphics, layout, 0, setsArr, dynArr); return 0; }, results);
        Time("cb.SetDepthBias(f,f,f) wrapper (3 floats)", n, k => { for (var i = 0; i < k; i++) cb.SetDepthBias(1.5f, 2.5f, i); return 0; }, results);
        Time("cb.WriteTimestamp2(ulong enum, handle, int) wrapper (64-bit enum)", n, k => { for (var i = 0; i < k; i++) cb.WriteTimestamp2(stage, pool, i); return 0; }, results);
        Time("cb.Dispatch(x,y,z) wrapper (4 args, callee has a throw path)", n, k => { for (var i = 0; i < k; i++) cb.Dispatch(1, 1, 1); return 0; }, results);

        NativeMemory.Free(sets);
        NativeMemory.Free(dynRaw);
        _print($"BENCH done: ICD counter advanced by {ManagedIcd.BenchCounter - counterBefore:N0} (expected {(long)n * 3 * 8 + 20000 * 8:N0} = 8 ICD-counting benches x (3 x {n:N0} + 20,000 warm-up))");
    }

    // ---- NULL entry --------------------------------------------------------------------------------------------------
    private static void CallNull(DeviceEx device, CommandBuffer cb)
    {
        var isNull = device.PInvoke.vkCmdDraw.Invoke == null;
        _print($"CALLNULL vkCmdDraw table entry is NULL: {isNull}; calling cb.Draw(3,1,0,0) inside try/catch ...");
        try
        {
            cb.Draw(3, 1, 0, 0);
            Step("S12-call-null", false, "returned normally (?!)");
        }
        catch (Exception e)
        {
            Step("S12-call-null", true, $"caught {e.GetType().FullName}: {e.Message}");
        }
        _print("CALLNULL runtime still alive after the NULL call");
    }
}
