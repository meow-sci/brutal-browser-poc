// exp-06: a managed "ICD" for the BRUTAL_MANAGED_FNPTR flavor of Brutal.Vulkan.
//
// Every entry point is a plain `static` C# method. vkGetInstanceProcAddr / vkGetDeviceProcAddr hand out managed
// function pointers (`&Method`, i.e. ldftn) wrapped in PfnVkVoidFunction. Brutal.Vulkan calls them with a managed
// `calli` - no [UnmanagedCallersOnly], no native transition, no JS trampoline, no wasm-tools relink.
//
// Probe entry points copy the raw argument values they receive into ManagedIcd.Probe so that the scenario can assert
// that 64-bit values, floats, structs-by-value and pointers arrive intact.
#if !BRUTAL_MANAGED_FNPTR
#error ManagedIcd.cs is only meaningful in the BRUTAL_MANAGED_FNPTR flavor (managed delegate*<...> tables).
#endif
using System.Runtime.InteropServices;
using System.Text;
using Brutal;
using Brutal.VulkanApi;

namespace Exp06;

public static unsafe class ManagedIcd
{
    public const string AppContextKey = "BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR";

    // ---- logging / bookkeeping -------------------------------------------------------------------------------------
    public static bool LogProcNames = true;
    public static readonly List<string> InstanceProcNames = new();
    public static readonly List<string> DeviceProcNames = new();
    public static readonly List<string> Calls = new();
    public static int InstanceLookups, DeviceLookups, InstanceLookupsNull, DeviceLookupsNull;
    public static long BenchCounter; // bumped by the cheap benchmark entry points

    public const nint FakeInstance = 0x1000, FakePhysicalDevice = 0x2000, FakeDevice = 0x3000, FakeQueue = 0x4000;
    private static nint _nextHandle = 0x10000;
    private static readonly Dictionary<nint, (nint Ptr, ulong Size)> Memory = new();

    // Debug-utils messenger state (ICD -> application callback, the reverse direction).
    private static PfnVkDebugUtilsMessengerCallbackEXT _messengerCallback;
    private static void* _messengerUserData;
    public static int MessengerCallbackReturns = -1;

    /// <summary>The managed vkGetInstanceProcAddr, as the nint that goes into the AppContext key.</summary>
    public static nint GetInstanceProcAddrPointer => (nint)(delegate*<VkInstance, byte*, PfnVkVoidFunction>)&GetInstanceProcAddr;

    public static void Install() => AppContext.SetData(AppContextKey, GetInstanceProcAddrPointer);

    // ---- name -> managed function pointer ----------------------------------------------------------------------
    private static readonly Dictionary<string, nint> Table = BuildTable();
    public static int ImplementedCount => Table.Count;

    private static Dictionary<string, nint> BuildTable() => new()
    {
        ["vkGetInstanceProcAddr"] = (nint)(delegate*<VkInstance, byte*, PfnVkVoidFunction>)&GetInstanceProcAddr,
        ["vkGetDeviceProcAddr"] = (nint)(delegate*<VkDevice, byte*, PfnVkVoidFunction>)&GetDeviceProcAddr,
        // global
        ["vkEnumerateInstanceVersion"] = (nint)(delegate*<int*, VkResult>)&EnumerateInstanceVersion,
        ["vkEnumerateInstanceExtensionProperties"] = (nint)(delegate*<byte*, int*, VkExtensionProperties*, VkResult>)&EnumerateInstanceExtensionProperties,
        ["vkEnumerateInstanceLayerProperties"] = (nint)(delegate*<int*, VkLayerProperties*, VkResult>)&EnumerateInstanceLayerProperties,
        ["vkCreateInstance"] = (nint)(delegate*<VkInstanceCreateInfo*, VkAllocationCallbacks*, VkInstance*, VkResult>)&CreateInstance,
        // instance level
        ["vkDestroyInstance"] = (nint)(delegate*<VkInstance, VkAllocationCallbacks*, void>)&DestroyInstance,
        ["vkEnumeratePhysicalDevices"] = (nint)(delegate*<VkInstance, int*, VkPhysicalDevice*, VkResult>)&EnumeratePhysicalDevices,
        ["vkGetPhysicalDeviceProperties"] = (nint)(delegate*<VkPhysicalDevice, VkPhysicalDeviceProperties*, void>)&GetPhysicalDeviceProperties,
        ["vkGetPhysicalDeviceProperties2"] = (nint)(delegate*<VkPhysicalDevice, VkPhysicalDeviceProperties2*, void>)&GetPhysicalDeviceProperties2,
        ["vkGetPhysicalDeviceQueueFamilyProperties"] = (nint)(delegate*<VkPhysicalDevice, int*, VkQueueFamilyProperties*, void>)&GetPhysicalDeviceQueueFamilyProperties,
        ["vkGetPhysicalDeviceMemoryProperties"] = (nint)(delegate*<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties*, void>)&GetPhysicalDeviceMemoryProperties,
        ["vkGetPhysicalDeviceFeatures"] = (nint)(delegate*<VkPhysicalDevice, VkPhysicalDeviceFeatures*, void>)&GetPhysicalDeviceFeatures,
        ["vkEnumerateDeviceExtensionProperties"] = (nint)(delegate*<VkPhysicalDevice, byte*, int*, VkExtensionProperties*, VkResult>)&EnumerateDeviceExtensionProperties,
        ["vkCreateDevice"] = (nint)(delegate*<VkPhysicalDevice, VkDeviceCreateInfo*, VkAllocationCallbacks*, VkDevice*, VkResult>)&CreateDevice,
        ["vkCreateDebugUtilsMessengerEXT"] = (nint)(delegate*<VkInstance, VkDebugUtilsMessengerCreateInfoEXT*, VkAllocationCallbacks*, VkDebugUtilsMessengerEXT*, VkResult>)&CreateDebugUtilsMessengerEXT,
        ["vkDestroyDebugUtilsMessengerEXT"] = (nint)(delegate*<VkInstance, VkDebugUtilsMessengerEXT, VkAllocationCallbacks*, void>)&DestroyDebugUtilsMessengerEXT,
        // device level
        ["vkDestroyDevice"] = (nint)(delegate*<VkDevice, VkAllocationCallbacks*, void>)&DestroyDevice,
        ["vkGetDeviceQueue"] = (nint)(delegate*<VkDevice, int, int, VkQueue*, void>)&GetDeviceQueue,
        ["vkCreateBuffer"] = (nint)(delegate*<VkDevice, VkBufferCreateInfo*, VkAllocationCallbacks*, VkBuffer*, VkResult>)&CreateBuffer,
        ["vkDestroyBuffer"] = (nint)(delegate*<VkDevice, VkBuffer, VkAllocationCallbacks*, void>)&DestroyBuffer,
        ["vkGetBufferMemoryRequirements"] = (nint)(delegate*<VkDevice, VkBuffer, VkMemoryRequirements*, void>)&GetBufferMemoryRequirements,
        ["vkAllocateMemory"] = (nint)(delegate*<VkDevice, VkMemoryAllocateInfo*, VkAllocationCallbacks*, VkDeviceMemory*, VkResult>)&AllocateMemory,
        ["vkFreeMemory"] = (nint)(delegate*<VkDevice, VkDeviceMemory, VkAllocationCallbacks*, void>)&FreeMemory,
        ["vkBindBufferMemory"] = (nint)(delegate*<VkDevice, VkBuffer, VkDeviceMemory, ByteSize64, VkResult>)&BindBufferMemory,
        ["vkMapMemory"] = (nint)(delegate*<VkDevice, VkDeviceMemory, ByteSize64, ByteSize64, VkMemoryMapFlags, void**, VkResult>)&MapMemory,
        ["vkUnmapMemory"] = (nint)(delegate*<VkDevice, VkDeviceMemory, void>)&UnmapMemory,
        ["vkSetDebugUtilsObjectNameEXT"] = (nint)(delegate*<VkDevice, VkDebugUtilsObjectNameInfoEXT*, VkResult>)&SetDebugUtilsObjectNameEXT,
        // signature-shape probes
        ["vkCmdBindDescriptorSets"] = (nint)(delegate*<VkCommandBuffer, VkPipelineBindPoint, VkPipelineLayout, int, int, VkDescriptorSet*, int, int*, void>)&CmdBindDescriptorSets,
        ["vkCmdSetDepthBias"] = (nint)(delegate*<VkCommandBuffer, float, float, float, void>)&CmdSetDepthBias,
        ["vkCmdSetBlendConstants"] = (nint)(delegate*<VkCommandBuffer, float*, void>)&CmdSetBlendConstants,
        ["vkCmdDrawIndexed"] = (nint)(delegate*<VkCommandBuffer, int, int, int, int, int, void>)&CmdDrawIndexed,
        ["vkCmdDrawIndexedIndirect"] = (nint)(delegate*<VkCommandBuffer, VkBuffer, ByteSize64, int, int, void>)&CmdDrawIndexedIndirect,
        ["vkCmdWriteTimestamp2"] = (nint)(delegate*<VkCommandBuffer, VkPipelineStageFlags2, VkQueryPool, int, void>)&CmdWriteTimestamp2,
        ["vkGetBufferDeviceAddress"] = (nint)(delegate*<VkDevice, VkBufferDeviceAddressInfo*, VkDeviceAddress>)&GetBufferDeviceAddress,
        ["vkCmdPushConstants"] = (nint)(delegate*<VkCommandBuffer, VkPipelineLayout, VkShaderStageFlags, int, int, void*, void>)&CmdPushConstants,
        ["vkWaitForFences"] = (nint)(delegate*<VkDevice, int, VkFence*, Bool32, nint, VkResult>)&WaitForFences,
        ["vkCmdPipelineBarrier"] = (nint)(delegate*<VkCommandBuffer, VkPipelineStageFlags, VkPipelineStageFlags, VkDependencyFlags, int, VkMemoryBarrier*, int, VkBufferMemoryBarrier*, int, VkImageMemoryBarrier*, void>)&CmdPipelineBarrier,
        ["vkCmdTraceRaysNV"] = (nint)(delegate*<VkCommandBuffer, VkBuffer, ByteSize64, VkBuffer, ByteSize64, ByteSize64, VkBuffer, ByteSize64, ByteSize64, VkBuffer, ByteSize64, ByteSize64, int, int, int, void>)&CmdTraceRaysNV,
        // benchmark + robustness
        ["vkCmdEndRenderPass"] = (nint)(delegate*<VkCommandBuffer, void>)&CmdEndRenderPass,
        ["vkCmdDispatch"] = (nint)(delegate*<VkCommandBuffer, int, int, int, void>)&CmdDispatch,
        // deliberately NOT implemented (NULL): vkCmdDraw, vkGetPhysicalDeviceFormatProperties, ... (robustness test)
    };

    private static PfnVkVoidFunction Lookup(string name) =>
        new PfnVkVoidFunction((delegate*<void>)Table.GetValueOrDefault(name));

    // ---- proc-addr ---------------------------------------------------------------------------------------------
    public static PfnVkVoidFunction GetInstanceProcAddr(VkInstance instance, byte* pName)
    {
        var name = Marshal.PtrToStringUTF8((nint)pName) ?? "<null>";
        var fn = Lookup(name);
        InstanceLookups++;
        if (fn.Invoke == null) InstanceLookupsNull++;
        if (LogProcNames)
            InstanceProcNames.Add($"{name}\t{(instance.IsNull() ? "instance=NULL" : "instance=0x" + instance.VkHandle.ToString("x"))}\t{(fn.Invoke == null ? "NULL" : "impl")}");
        return fn;
    }

    public static PfnVkVoidFunction GetDeviceProcAddr(VkDevice device, byte* pName)
    {
        var name = Marshal.PtrToStringUTF8((nint)pName) ?? "<null>";
        var fn = Lookup(name);
        DeviceLookups++;
        if (fn.Invoke == null) DeviceLookupsNull++;
        if (LogProcNames)
            DeviceProcNames.Add($"{name}\t{(fn.Invoke == null ? "NULL" : "impl")}");
        return fn;
    }

    // ---- global ------------------------------------------------------------------------------------------------
    public static VkResult EnumerateInstanceVersion(int* pApiVersion)
    {
        Log("vkEnumerateInstanceVersion");
        *pApiVersion = (1 << 22) | (3 << 12) | 0;
        return VkResult.Success;
    }

    private static readonly string[] InstanceExtensions = ["VK_KHR_surface", "VK_KHR_portability_enumeration", "VK_KHR_get_physical_device_properties2", "VK_EXT_debug_utils"];

    public static VkResult EnumerateInstanceExtensionProperties(byte* pLayerName, int* pCount, VkExtensionProperties* pProps)
    {
        Log($"vkEnumerateInstanceExtensionProperties(props={(pProps == null ? "NULL" : "buf")}, count={*pCount})");
        if (pProps == null)
        {
            *pCount = InstanceExtensions.Length;
            return VkResult.Success;
        }
        var n = Math.Min(*pCount, InstanceExtensions.Length);
        for (var i = 0; i < n; i++)
        {
            WriteUtf8(pProps[i].ExtensionName, 256, InstanceExtensions[i]);
            pProps[i].SpecVersion = 1;
        }
        *pCount = n;
        return n < InstanceExtensions.Length ? VkResult.Incomplete : VkResult.Success;
    }

    public static VkResult EnumerateInstanceLayerProperties(int* pCount, VkLayerProperties* pProps)
    {
        Log("vkEnumerateInstanceLayerProperties");
        *pCount = 0;
        return VkResult.Success;
    }

    public static VkResult CreateInstance(VkInstanceCreateInfo* ci, VkAllocationCallbacks* a, VkInstance* pInstance)
    {
        var sb = new StringBuilder("vkCreateInstance(apiVersion=");
        sb.Append(ci->ApplicationInfo == null ? "n/a" : FormatVersion(ci->ApplicationInfo->ApiVersion)).Append(", extensions=[");
        for (var i = 0; i < ci->EnabledExtensionCount; i++)
            sb.Append(i == 0 ? "" : ",").Append(Marshal.PtrToStringUTF8((nint)ci->ppEnabledExtensionNames[i]));
        Log(sb.Append("])").ToString());
        *pInstance = new VkInstance(FakeInstance);
        return VkResult.Success;
    }

    // ---- instance level ----------------------------------------------------------------------------------------
    public static void DestroyInstance(VkInstance instance, VkAllocationCallbacks* a) => Log($"vkDestroyInstance(0x{instance.VkHandle:x})");

    public static VkResult EnumeratePhysicalDevices(VkInstance instance, int* pCount, VkPhysicalDevice* pDevices)
    {
        Log($"vkEnumeratePhysicalDevices({(pDevices == null ? "count" : "write")})");
        if (pDevices != null)
        {
            if (*pCount < 1) return VkResult.Incomplete;
            pDevices[0] = new VkPhysicalDevice(FakePhysicalDevice);
        }
        *pCount = 1;
        return VkResult.Success;
    }

    public const string DeviceName = "BRUTAL exp-06 managed-fnptr ICD";

    public static void GetPhysicalDeviceProperties(VkPhysicalDevice pd, VkPhysicalDeviceProperties* p)
    {
        Log("vkGetPhysicalDeviceProperties");
        FillProperties(p);
    }

    public static void GetPhysicalDeviceProperties2(VkPhysicalDevice pd, VkPhysicalDeviceProperties2* p)
    {
        Log($"vkGetPhysicalDeviceProperties2(sType={*(VkStructureType*)p})"); // sType is internal; explicit layout puts it at offset 0
        FillProperties(&p->Properties);
    }

    private static void FillProperties(VkPhysicalDeviceProperties* p)
    {
        *p = new VkPhysicalDeviceProperties
        {
            ApiVersion = (1 << 22) | (3 << 12),
            DriverVersion = 6,
            VendorID = 0x10005,
            DeviceID = 0x0e06,
            DeviceType = VkPhysicalDeviceType.VirtualGpu,
        };
        WriteUtf8(p->DeviceName, 256, DeviceName);
    }

    public static void GetPhysicalDeviceQueueFamilyProperties(VkPhysicalDevice pd, int* pCount, VkQueueFamilyProperties* p)
    {
        Log($"vkGetPhysicalDeviceQueueFamilyProperties({(p == null ? "count" : "write")})");
        if (p != null)
            p[0] = new VkQueueFamilyProperties
            {
                QueueFlags = VkQueueFlags.GraphicsBit | VkQueueFlags.ComputeBit | VkQueueFlags.TransferBit,
                QueueCount = 1,
                TimestampValidBits = 64,
            };
        *pCount = 1;
    }

    public static void GetPhysicalDeviceMemoryProperties(VkPhysicalDevice pd, VkPhysicalDeviceMemoryProperties* p)
    {
        Log("vkGetPhysicalDeviceMemoryProperties");
        *p = default;
        p->MemoryHeapCount = 2;
        p->MemoryHeaps[0] = new VkMemoryHeap { Size = new ByteSize64(256UL << 20), Flags = VkMemoryHeapFlags.DeviceLocalBit };
        p->MemoryHeaps[1] = new VkMemoryHeap { Size = new ByteSize64(64UL << 20), Flags = VkMemoryHeapFlags.None };
        p->MemoryTypeCount = 2;
        p->MemoryTypes[0] = new VkMemoryType { PropertyFlags = VkMemoryPropertyFlags.DeviceLocalBit, HeapIndex = 0 };
        p->MemoryTypes[1] = new VkMemoryType { PropertyFlags = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit, HeapIndex = 1 };
    }

    public static void GetPhysicalDeviceFeatures(VkPhysicalDevice pd, VkPhysicalDeviceFeatures* p)
    {
        Log("vkGetPhysicalDeviceFeatures");
        *p = default;
        p->RobustBufferAccess = true;
        p->ShaderInt64 = true;
    }

    public static VkResult EnumerateDeviceExtensionProperties(VkPhysicalDevice pd, byte* pLayerName, int* pCount, VkExtensionProperties* p)
    {
        Log("vkEnumerateDeviceExtensionProperties");
        *pCount = 0;
        return VkResult.Success;
    }

    public static VkResult CreateDevice(VkPhysicalDevice pd, VkDeviceCreateInfo* ci, VkAllocationCallbacks* a, VkDevice* pDevice)
    {
        Log($"vkCreateDevice(queueCreateInfos={ci->QueueCreateInfoCount}, family={(ci->QueueCreateInfoCount > 0 ? ci->QueueCreateInfos[0].QueueFamilyIndex : -1)}, priority={(ci->QueueCreateInfoCount > 0 ? ci->QueueCreateInfos[0].QueuePriorities[0] : float.NaN)})");
        *pDevice = new VkDevice(FakeDevice);
        return VkResult.Success;
    }

    public static VkResult CreateDebugUtilsMessengerEXT(VkInstance instance, VkDebugUtilsMessengerCreateInfoEXT* ci, VkAllocationCallbacks* a, VkDebugUtilsMessengerEXT* pMessenger)
    {
        _messengerCallback = ci->PfnUserCallback;
        _messengerUserData = ci->UserData;
        *pMessenger = new VkDebugUtilsMessengerEXT(_nextHandle++);
        Log($"vkCreateDebugUtilsMessengerEXT(callback={(ci->PfnUserCallback.Invoke == null ? "NULL" : "set")}) -> 0x{pMessenger->VkHandle:x}");
        // Call the application back through the managed function pointer it supplied (ICD -> app direction).
        EmitDebugMessage(VkDebugUtilsMessageSeverityFlagsEXT.InfoBitEXT, "exp-06 ICD: debug messenger created");
        return VkResult.Success;
    }

    public static void EmitDebugMessage(VkDebugUtilsMessageSeverityFlagsEXT severity, string message)
    {
        if (_messengerCallback.Invoke == null) return;
        var bytes = Encoding.UTF8.GetBytes(message + "\0");
        fixed (byte* pMsg = bytes)
        {
            var data = new VkDebugUtilsMessengerCallbackDataEXT { Message = pMsg, MessageIdNumber = 6 };
            Bool32 r = _messengerCallback.Invoke(severity, VkDebugUtilsMessageTypeFlagsEXT.GeneralBitEXT, &data, _messengerUserData);
            MessengerCallbackReturns = r ? 1 : 0;
        }
    }

    public static void DestroyDebugUtilsMessengerEXT(VkInstance instance, VkDebugUtilsMessengerEXT m, VkAllocationCallbacks* a)
    {
        Log($"vkDestroyDebugUtilsMessengerEXT(0x{m.VkHandle:x})");
        _messengerCallback = default;
    }

    // ---- device level ------------------------------------------------------------------------------------------
    public static void DestroyDevice(VkDevice device, VkAllocationCallbacks* a) => Log($"vkDestroyDevice(0x{device.VkHandle:x})");

    public static void GetDeviceQueue(VkDevice device, int family, int index, VkQueue* pQueue)
    {
        BenchCounter++;
        *pQueue = new VkQueue(FakeQueue + family * 16 + index);
    }

    public static VkResult CreateBuffer(VkDevice device, VkBufferCreateInfo* ci, VkAllocationCallbacks* a, VkBuffer* pBuffer)
    {
        *pBuffer = new VkBuffer(_nextHandle++);
        Log($"vkCreateBuffer(size={ci->Size.Value}, usage={ci->Usage}) -> 0x{pBuffer->VkHandle:x}");
        return VkResult.Success;
    }

    public static void DestroyBuffer(VkDevice device, VkBuffer buffer, VkAllocationCallbacks* a) => Log($"vkDestroyBuffer(0x{buffer.VkHandle:x})");

    public static void GetBufferMemoryRequirements(VkDevice device, VkBuffer buffer, VkMemoryRequirements* r)
    {
        Log($"vkGetBufferMemoryRequirements(0x{buffer.VkHandle:x})");
        *r = new VkMemoryRequirements { Size = new ByteSize64(1024), Alignment = new ByteSize64(256), MemoryTypeBits = 0b11 };
    }

    /// <summary>When set, the next vkAllocateMemory fails with this result (VkResult -> VkResultError mapping test).</summary>
    public static VkResult NextAllocateMemoryResult = VkResult.Success;

    public static VkResult AllocateMemory(VkDevice device, VkMemoryAllocateInfo* info, VkAllocationCallbacks* a, VkDeviceMemory* pMemory)
    {
        if (NextAllocateMemoryResult != VkResult.Success)
        {
            var r = NextAllocateMemoryResult;
            NextAllocateMemoryResult = VkResult.Success;
            Log($"vkAllocateMemory(size={info->AllocationSize.Value}) -> {r}");
            return r;
        }
        var handle = _nextHandle++;
        var size = info->AllocationSize.Value;
        Memory[handle] = ((nint)NativeMemory.AllocZeroed((nuint)size), size);
        Log($"vkAllocateMemory(size={size}, type={info->MemoryTypeIndex}) -> 0x{handle:x}");
        *pMemory = new VkDeviceMemory(handle);
        return VkResult.Success;
    }

    public static void FreeMemory(VkDevice device, VkDeviceMemory memory, VkAllocationCallbacks* a)
    {
        Log($"vkFreeMemory(0x{memory.VkHandle:x})");
        if (Memory.Remove(memory.VkHandle, out var m))
            NativeMemory.Free((void*)m.Ptr);
    }

    public static VkResult BindBufferMemory(VkDevice device, VkBuffer buffer, VkDeviceMemory memory, ByteSize64 offset)
    {
        Probe.BindOffset = offset.Value;
        Log($"vkBindBufferMemory(buffer=0x{buffer.VkHandle:x}, memory=0x{memory.VkHandle:x}, offset=0x{offset.Value:x})");
        return VkResult.Success;
    }

    public static VkResult MapMemory(VkDevice device, VkDeviceMemory memory, ByteSize64 offset, ByteSize64 size, VkMemoryMapFlags flags, void** ppData)
    {
        Probe.MapOffset = offset.Value;
        Probe.MapSize = size.Value;
        Probe.MapFlags = (int)flags;
        Log($"vkMapMemory(memory=0x{memory.VkHandle:x}, offset=0x{offset.Value:x}, size=0x{size.Value:x}, flags={(int)flags})");
        if (!Memory.TryGetValue(memory.VkHandle, out var m))
        {
            *ppData = null;
            return VkResult.ErrorMemoryMapFailed;
        }
        *ppData = (byte*)m.Ptr + (nint)Math.Min(offset.Value, m.Size); // offset clamped: a probe may pass a huge 64-bit offset
        return VkResult.Success;
    }

    public static void UnmapMemory(VkDevice device, VkDeviceMemory memory) => Log($"vkUnmapMemory(0x{memory.VkHandle:x})");

    /// <summary>The shadow bytes behind a VkDeviceMemory (what a GPU upload would read).</summary>
    public static ReadOnlySpan<byte> Shadow(VkDeviceMemory memory) =>
        Memory.TryGetValue(memory.VkHandle, out var m) ? new ReadOnlySpan<byte>((void*)m.Ptr, (int)m.Size) : default;

    public static VkResult SetDebugUtilsObjectNameEXT(VkDevice device, VkDebugUtilsObjectNameInfoEXT* info)
    {
        Probe.ObjectNameType = info->ObjectType;
        Probe.ObjectNameHandle = (long)info->ObjectHandle;
        Probe.ObjectName = Marshal.PtrToStringUTF8((nint)info->ObjectName);
        return VkResult.Success;
    }

    // ---- signature-shape probes (record raw arguments) ---------------------------------------------------------
    public static class Probe
    {
        public static ulong BindOffset, MapOffset, MapSize;
        public static int MapFlags;
        public static VkObjectType ObjectNameType;
        public static long ObjectNameHandle;
        public static string? ObjectName;
        // vkCmdBindDescriptorSets
        public static nint BdsCb; public static VkPipelineBindPoint BdsBindPoint; public static nint BdsLayout;
        public static int BdsFirstSet, BdsSetCount, BdsDynCount; public static nint BdsSet0, BdsSetLast; public static int BdsDyn0, BdsDynLast;
        // vkCmdSetDepthBias / vkCmdSetBlendConstants
        public static float DbConstant, DbClamp, DbSlope;
        public static float Bc0, Bc1, Bc2, Bc3;
        // vkCmdDrawIndexed
        public static int DiIndexCount, DiInstanceCount, DiFirstIndex, DiVertexOffset, DiFirstInstance;
        // vkCmdDrawIndexedIndirect
        public static nint DiiBuffer; public static ulong DiiOffset; public static int DiiDrawCount, DiiStride;
        // vkCmdWriteTimestamp2
        public static ulong Wt2Stage; public static nint Wt2Pool; public static int Wt2Query;
        // vkGetBufferDeviceAddress
        public static nint GbdaBuffer;
        // vkCmdPushConstants
        public static nint PcLayout; public static VkShaderStageFlags PcStages; public static int PcOffset, PcSize; public static float PcFirstFloat; public static uint PcLastWord;
        // vkWaitForFences
        public static int WffCount; public static nint WffFence0; public static bool WffWaitAll; public static long WffTimeout;
        // vkCmdPipelineBarrier (10 args)
        public static VkPipelineStageFlags PbSrc, PbDst; public static VkDependencyFlags PbDep; public static int PbMemCount, PbBufCount, PbImgCount;
        public static nint PbBuf0; public static ulong PbBufOffset0, PbBufSize0; public static VkImageLayout PbImgNewLayout0; public static nint PbImg0;
        // vkCmdTraceRaysNV (15 args, 7 x ByteSize64 by value)
        public static nint[] TrBuffers = new nint[4]; public static ulong[] TrU64 = new ulong[7]; public static int TrW, TrH, TrD;
    }

    public static void CmdBindDescriptorSets(VkCommandBuffer cb, VkPipelineBindPoint bindPoint, VkPipelineLayout layout, int firstSet, int setCount, VkDescriptorSet* pSets, int dynCount, int* pDyn)
    {
        Probe.BdsCb = cb.VkHandle; Probe.BdsBindPoint = bindPoint; Probe.BdsLayout = layout.VkHandle;
        Probe.BdsFirstSet = firstSet; Probe.BdsSetCount = setCount; Probe.BdsDynCount = dynCount;
        Probe.BdsSet0 = setCount > 0 ? pSets[0].VkHandle : 0; Probe.BdsSetLast = setCount > 0 ? pSets[setCount - 1].VkHandle : 0;
        Probe.BdsDyn0 = dynCount > 0 ? pDyn[0] : 0; Probe.BdsDynLast = dynCount > 0 ? pDyn[dynCount - 1] : 0;
        BenchCounter++;
    }

    public static void CmdSetDepthBias(VkCommandBuffer cb, float constant, float clamp, float slope)
    {
        Probe.DbConstant = constant; Probe.DbClamp = clamp; Probe.DbSlope = slope;
        BenchCounter++;
    }

    public static void CmdSetBlendConstants(VkCommandBuffer cb, float* c)
    {
        Probe.Bc0 = c[0]; Probe.Bc1 = c[1]; Probe.Bc2 = c[2]; Probe.Bc3 = c[3];
    }

    public static void CmdDrawIndexed(VkCommandBuffer cb, int indexCount, int instanceCount, int firstIndex, int vertexOffset, int firstInstance)
    {
        Probe.DiIndexCount = indexCount; Probe.DiInstanceCount = instanceCount; Probe.DiFirstIndex = firstIndex;
        Probe.DiVertexOffset = vertexOffset; Probe.DiFirstInstance = firstInstance;
    }

    public static void CmdDrawIndexedIndirect(VkCommandBuffer cb, VkBuffer buffer, ByteSize64 offset, int drawCount, int stride)
    {
        Probe.DiiBuffer = buffer.VkHandle; Probe.DiiOffset = offset.Value; Probe.DiiDrawCount = drawCount; Probe.DiiStride = stride;
    }

    public static void CmdWriteTimestamp2(VkCommandBuffer cb, VkPipelineStageFlags2 stage, VkQueryPool pool, int query)
    {
        Probe.Wt2Stage = (ulong)stage; Probe.Wt2Pool = pool.VkHandle; Probe.Wt2Query = query;
        BenchCounter++;
    }

    public const ulong FakeDeviceAddress = 0xFEED_0000_0000_0042UL;

    public static VkDeviceAddress GetBufferDeviceAddress(VkDevice device, VkBufferDeviceAddressInfo* info)
    {
        Probe.GbdaBuffer = info->Buffer.VkHandle;
        return new VkDeviceAddress(FakeDeviceAddress + (ulong)info->Buffer.VkHandle);
    }

    public static void CmdPushConstants(VkCommandBuffer cb, VkPipelineLayout layout, VkShaderStageFlags stages, int offset, int size, void* pValues)
    {
        Probe.PcLayout = layout.VkHandle; Probe.PcStages = stages; Probe.PcOffset = offset; Probe.PcSize = size;
        Probe.PcFirstFloat = size >= 4 ? *(float*)pValues : float.NaN;
        Probe.PcLastWord = size >= 4 ? *(uint*)((byte*)pValues + size - 4) : 0;
    }

    public static VkResult WaitForFences(VkDevice device, int count, VkFence* pFences, Bool32 waitAll, nint timeout)
    {
        Probe.WffCount = count; Probe.WffFence0 = count > 0 ? pFences[0].VkHandle : 0; Probe.WffWaitAll = waitAll; Probe.WffTimeout = timeout;
        return VkResult.Success;
    }

    public static void CmdPipelineBarrier(VkCommandBuffer cb, VkPipelineStageFlags src, VkPipelineStageFlags dst, VkDependencyFlags dep,
        int memCount, VkMemoryBarrier* pMem, int bufCount, VkBufferMemoryBarrier* pBuf, int imgCount, VkImageMemoryBarrier* pImg)
    {
        Probe.PbSrc = src; Probe.PbDst = dst; Probe.PbDep = dep; Probe.PbMemCount = memCount; Probe.PbBufCount = bufCount; Probe.PbImgCount = imgCount;
        if (bufCount > 0) { Probe.PbBuf0 = pBuf[0].Buffer.VkHandle; Probe.PbBufOffset0 = pBuf[0].Offset.Value; Probe.PbBufSize0 = pBuf[0].Size.Value; }
        if (imgCount > 0) { Probe.PbImgNewLayout0 = pImg[0].NewLayout; Probe.PbImg0 = pImg[0].Image.VkHandle; }
    }

    public static void CmdTraceRaysNV(VkCommandBuffer cb, VkBuffer raygen, ByteSize64 raygenOffset, VkBuffer miss, ByteSize64 missOffset, ByteSize64 missStride,
        VkBuffer hit, ByteSize64 hitOffset, ByteSize64 hitStride, VkBuffer callable, ByteSize64 callableOffset, ByteSize64 callableStride, int w, int h, int d)
    {
        Probe.TrBuffers[0] = raygen.VkHandle; Probe.TrBuffers[1] = miss.VkHandle; Probe.TrBuffers[2] = hit.VkHandle; Probe.TrBuffers[3] = callable.VkHandle;
        Probe.TrU64[0] = raygenOffset.Value; Probe.TrU64[1] = missOffset.Value; Probe.TrU64[2] = missStride.Value; Probe.TrU64[3] = hitOffset.Value;
        Probe.TrU64[4] = hitStride.Value; Probe.TrU64[5] = callableOffset.Value; Probe.TrU64[6] = callableStride.Value;
        Probe.TrW = w; Probe.TrH = h; Probe.TrD = d;
    }

    public static void CmdEndRenderPass(VkCommandBuffer cb) => BenchCounter++;

    /// <summary>Throws a managed exception from inside the "driver" when groupCountX == 0xDEAD (robustness test).</summary>
    public static void CmdDispatch(VkCommandBuffer cb, int x, int y, int z)
    {
        if (x == 0xDEAD)
            throw new InvalidOperationException($"thrown inside ManagedIcd.CmdDispatch(x=0x{x:X}, y={y}, z={z})");
        BenchCounter++;
    }

    // ---- helpers -----------------------------------------------------------------------------------------------
    private static void Log(string s) => Calls.Add(s);

    public static string FormatVersion(int v) => $"{(v >> 22) & 0x7F}.{(v >> 12) & 0x3FF}.{v & 0xFFF}";

    private static void WriteUtf8(byte* dst, int capacity, string s)
    {
        var n = Encoding.UTF8.GetBytes(s, new Span<byte>(dst, capacity - 1));
        dst[n] = 0;
    }
}
