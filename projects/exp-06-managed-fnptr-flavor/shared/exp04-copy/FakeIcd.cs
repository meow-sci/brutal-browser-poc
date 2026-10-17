// Verbatim copy of projects/exp-04-brutal-vulkan-source-build/shared/FakeIcd.cs (exp-06 unmanaged-flavor regression check).
// exp-04: a minimal managed "ICD" (C# implementations of Vulkan entry points) used to drive the source-built
// Brutal.Vulkan through the pluggable-loader patch. It records every name Brutal.Vulkan asks for, in order.
// Shared (linked) by apps/ManagedLoaderSmoke (desktop) and apps/BrowserCompileCheck (browser-wasm).
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Brutal;
using Brutal.VulkanApi;

namespace Exp04;

public static unsafe class FakeIcd
{
    public const string AppContextKey = "BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR";

    // Ordered log of names passed to vkGetInstanceProcAddr / vkGetDeviceProcAddr.
    public static readonly List<string> InstanceProcNames = new();
    public static readonly List<string> DeviceProcNames = new();
    // Free-form log of calls into implemented entry points.
    public static readonly List<string> Calls = new();

    private static readonly nint FakeInstance = 0x1000;
    private static readonly nint FakePhysicalDevice = 0x2000;
    private static readonly nint FakeDevice = 0x3000;
    private static readonly nint FakeQueue = 0x4000;
    private static nint _nextNonDispatchable = 0x10000;

    // Names this ICD implements, split by the proc-addr function that hands them out.
    public static readonly string[] InstanceLevelNames =
    [
        "vkGetInstanceProcAddr", "vkEnumerateInstanceVersion", "vkEnumerateInstanceExtensionProperties",
        "vkEnumerateInstanceLayerProperties", "vkCreateInstance", "vkDestroyInstance", "vkEnumeratePhysicalDevices",
        "vkGetPhysicalDeviceProperties", "vkGetPhysicalDeviceQueueFamilyProperties", "vkGetPhysicalDeviceMemoryProperties",
        "vkEnumerateDeviceExtensionProperties", "vkCreateDevice", "vkGetDeviceProcAddr",
    ];
    public static readonly string[] DeviceLevelNames =
    [
        "vkGetDeviceProcAddr", "vkDestroyDevice", "vkGetDeviceQueue", "vkCreateBuffer", "vkDestroyBuffer",
        "vkGetBufferMemoryRequirements", "vkAllocateMemory", "vkFreeMemory", "vkBindBufferMemory",
    ];

    // name -> callable function pointer. Filled either with [UnmanagedCallersOnly] pointers (UcoEntryPoints, desktop)
    // or with JS Module.addFunction trampolines into [JSExport] wrappers (browser without native relink).
    public static readonly Dictionary<string, nint> EntryPoints = new();

    /// <summary>Installs this ICD as Brutal.Vulkan's loader. Must run before the first use of Vulkan.</summary>
    public static void Install(nint getInstanceProcAddr) => AppContext.SetData(AppContextKey, getInstanceProcAddr);

    // ---------------------------------------------------------------------------------------------------------
    // Proc-addr entry points
    // ---------------------------------------------------------------------------------------------------------

    public static PfnVkVoidFunction GetInstanceProcAddr(VkInstance instance, byte* pName)
    {
        var name = Marshal.PtrToStringUTF8((nint)pName) ?? "<null>";
        var fn = Array.IndexOf(InstanceLevelNames, name) >= 0 ? EntryPoints.GetValueOrDefault(name) : 0;
        lock (InstanceProcNames)
            InstanceProcNames.Add($"{name}\t{(instance.IsNull() ? "instance=NULL" : "instance=0x" + instance.VkHandle.ToString("x"))}\t{(fn == 0 ? "NULL" : "impl")}");
        return new PfnVkVoidFunction((delegate* unmanaged[Cdecl]<void>)fn);
    }

    public static PfnVkVoidFunction GetDeviceProcAddr(VkDevice device, byte* pName)
    {
        var name = Marshal.PtrToStringUTF8((nint)pName) ?? "<null>";
        var fn = Array.IndexOf(DeviceLevelNames, name) >= 0 ? EntryPoints.GetValueOrDefault(name) : 0;
        lock (DeviceProcNames)
            DeviceProcNames.Add($"{name}\t{(device.IsNull() ? "device=NULL" : "device=0x" + device.VkHandle.ToString("x"))}\t{(fn == 0 ? "NULL" : "impl")}");
        return new PfnVkVoidFunction((delegate* unmanaged[Cdecl]<void>)fn);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Global commands
    // ---------------------------------------------------------------------------------------------------------

    public static VkResult EnumerateInstanceVersion(int* pApiVersion)
    {
        Log("vkEnumerateInstanceVersion");
        *pApiVersion = (1 << 22) | (3 << 12) | 0; // 1.3.0
        return VkResult.Success;
    }

    private static readonly string[] InstanceExtensions = ["VK_KHR_surface", "VK_KHR_portability_enumeration", "VK_KHR_get_physical_device_properties2"];

    public static VkResult EnumerateInstanceExtensionProperties(byte* pLayerName, int* pPropertyCount, VkExtensionProperties* pProperties)
    {
        Log($"vkEnumerateInstanceExtensionProperties(layer={(pLayerName == null ? "NULL" : Marshal.PtrToStringUTF8((nint)pLayerName))}, props={(pProperties == null ? "NULL" : "buf")}, count={*pPropertyCount})");
        if (pProperties == null)
        {
            *pPropertyCount = InstanceExtensions.Length;
            return VkResult.Success;
        }
        var n = Math.Min(*pPropertyCount, InstanceExtensions.Length);
        for (var i = 0; i < n; i++)
        {
            WriteUtf8(pProperties[i].ExtensionName, 256, InstanceExtensions[i]);
            pProperties[i].SpecVersion = 1;
        }
        *pPropertyCount = n;
        return n < InstanceExtensions.Length ? VkResult.Incomplete : VkResult.Success;
    }

    public static VkResult EnumerateInstanceLayerProperties(int* pPropertyCount, VkLayerProperties* pProperties)
    {
        Log($"vkEnumerateInstanceLayerProperties(props={(pProperties == null ? "NULL" : "buf")}, count={*pPropertyCount})");
        *pPropertyCount = 0;
        return VkResult.Success;
    }

    public static VkResult CreateInstance(VkInstanceCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkInstance* pInstance)
    {
        var sb = new StringBuilder("vkCreateInstance(flags=").Append(pCreateInfo->Flags).Append(", apiVersion=");
        sb.Append(pCreateInfo->ApplicationInfo == null ? "n/a" : FormatVersion(pCreateInfo->ApplicationInfo->ApiVersion));
        sb.Append(", extensions=[");
        for (var i = 0; i < pCreateInfo->EnabledExtensionCount; i++)
            sb.Append(i == 0 ? "" : ", ").Append(Marshal.PtrToStringUTF8((nint)pCreateInfo->ppEnabledExtensionNames[i]));
        sb.Append("], layers=").Append(pCreateInfo->EnabledLayerCount).Append(')');
        Log(sb.ToString());
        *pInstance = new VkInstance(FakeInstance);
        return VkResult.Success;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Instance-level commands
    // ---------------------------------------------------------------------------------------------------------

    public static void DestroyInstance(VkInstance instance, VkAllocationCallbacks* pAllocator) =>
        Log($"vkDestroyInstance(0x{instance.VkHandle:x})");

    public static VkResult EnumeratePhysicalDevices(VkInstance instance, int* pCount, VkPhysicalDevice* pDevices)
    {
        Log($"vkEnumeratePhysicalDevices(devices={(pDevices == null ? "NULL" : "buf")}, count={*pCount})");
        if (pDevices == null)
        {
            *pCount = 1;
            return VkResult.Success;
        }
        if (*pCount < 1)
            return VkResult.Incomplete;
        pDevices[0] = new VkPhysicalDevice(FakePhysicalDevice);
        *pCount = 1;
        return VkResult.Success;
    }

    public static void GetPhysicalDeviceProperties(VkPhysicalDevice physicalDevice, VkPhysicalDeviceProperties* pProperties)
    {
        Log("vkGetPhysicalDeviceProperties");
        *pProperties = new VkPhysicalDeviceProperties
        {
            ApiVersion = (1 << 22) | (3 << 12),
            DriverVersion = 1,
            VendorID = 0x10005, // VK_VENDOR_ID_MESA-style "no PCI vendor" id
            DeviceID = 0x0e04,
            DeviceType = VkPhysicalDeviceType.VirtualGpu,
        };
        WriteUtf8(pProperties->DeviceName, 256, "BRUTAL exp-04 managed fake ICD");
    }

    public static void GetPhysicalDeviceQueueFamilyProperties(VkPhysicalDevice physicalDevice, int* pCount, VkQueueFamilyProperties* pProps)
    {
        Log($"vkGetPhysicalDeviceQueueFamilyProperties(props={(pProps == null ? "NULL" : "buf")})");
        if (pProps == null)
        {
            *pCount = 1;
            return;
        }
        pProps[0] = new VkQueueFamilyProperties
        {
            QueueFlags = VkQueueFlags.GraphicsBit | VkQueueFlags.ComputeBit | VkQueueFlags.TransferBit,
            QueueCount = 1,
            TimestampValidBits = 0,
        };
        *pCount = 1;
    }

    public static void GetPhysicalDeviceMemoryProperties(VkPhysicalDevice physicalDevice, VkPhysicalDeviceMemoryProperties* pProps)
    {
        Log("vkGetPhysicalDeviceMemoryProperties");
        *pProps = default;
        pProps->MemoryHeapCount = 1;
        pProps->MemoryHeaps[0] = new VkMemoryHeap { Size = new ByteSize64(256UL << 20), Flags = VkMemoryHeapFlags.DeviceLocalBit };
        pProps->MemoryTypeCount = 1;
        pProps->MemoryTypes[0] = new VkMemoryType
        {
            PropertyFlags = VkMemoryPropertyFlags.DeviceLocalBit | VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,
            HeapIndex = 0,
        };
    }

    public static VkResult EnumerateDeviceExtensionProperties(VkPhysicalDevice physicalDevice, byte* pLayerName, int* pCount, VkExtensionProperties* pProps)
    {
        Log("vkEnumerateDeviceExtensionProperties");
        *pCount = 0;
        return VkResult.Success;
    }

    public static VkResult CreateDevice(VkPhysicalDevice physicalDevice, VkDeviceCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkDevice* pDevice)
    {
        Log($"vkCreateDevice(queueCreateInfos={pCreateInfo->QueueCreateInfoCount}, family={(pCreateInfo->QueueCreateInfoCount > 0 ? pCreateInfo->QueueCreateInfos[0].QueueFamilyIndex : -1)}, extensions={pCreateInfo->EnabledExtensionCount})");
        *pDevice = new VkDevice(FakeDevice);
        return VkResult.Success;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Device-level commands
    // ---------------------------------------------------------------------------------------------------------

    public static void DestroyDevice(VkDevice device, VkAllocationCallbacks* pAllocator) => Log($"vkDestroyDevice(0x{device.VkHandle:x})");

    public static void GetDeviceQueue(VkDevice device, int family, int index, VkQueue* pQueue)
    {
        Log($"vkGetDeviceQueue(family={family}, index={index})");
        *pQueue = new VkQueue(FakeQueue);
    }

    public static VkResult CreateBuffer(VkDevice device, VkBufferCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkBuffer* pBuffer)
    {
        var handle = _nextNonDispatchable++;
        Log($"vkCreateBuffer(size={pCreateInfo->Size.Value}, usage={pCreateInfo->Usage}) -> 0x{handle:x}");
        *pBuffer = new VkBuffer(handle);
        return VkResult.Success;
    }

    public static void DestroyBuffer(VkDevice device, VkBuffer buffer, VkAllocationCallbacks* pAllocator) => Log($"vkDestroyBuffer(0x{buffer.VkHandle:x})");

    public static void GetBufferMemoryRequirements(VkDevice device, VkBuffer buffer, VkMemoryRequirements* pReq)
    {
        Log($"vkGetBufferMemoryRequirements(0x{buffer.VkHandle:x})");
        *pReq = new VkMemoryRequirements { Size = new ByteSize64(1024), Alignment = new ByteSize64(256), MemoryTypeBits = 1 };
    }

    public static VkResult AllocateMemory(VkDevice device, VkMemoryAllocateInfo* pInfo, VkAllocationCallbacks* pAllocator, VkDeviceMemory* pMemory)
    {
        var handle = _nextNonDispatchable++;
        Log($"vkAllocateMemory(size={pInfo->AllocationSize.Value}, type={pInfo->MemoryTypeIndex}) -> 0x{handle:x}");
        *pMemory = new VkDeviceMemory(handle);
        return VkResult.Success;
    }

    public static void FreeMemory(VkDevice device, VkDeviceMemory memory, VkAllocationCallbacks* pAllocator) => Log($"vkFreeMemory(0x{memory.VkHandle:x})");

    public static VkResult BindBufferMemory(VkDevice device, VkBuffer buffer, VkDeviceMemory memory, ByteSize64 offset)
    {
        Log($"vkBindBufferMemory(buffer=0x{buffer.VkHandle:x}, memory=0x{memory.VkHandle:x}, offset={offset.Value})");
        return VkResult.Success;
    }

    // ---------------------------------------------------------------------------------------------------------

    private static void Log(string s)
    {
        lock (Calls)
            Calls.Add(s);
    }

    public static string FormatVersion(int v) => $"{(v >> 22) & 0x7F}.{(v >> 12) & 0x3FF}.{v & 0xFFF}";

    private static void WriteUtf8(byte* dst, int capacity, string s)
    {
        var n = Encoding.UTF8.GetBytes(s, new Span<byte>(dst, capacity - 1));
        dst[n] = 0;
    }
}

/// <summary>[UnmanagedCallersOnly] wrappers around FakeIcd: the "native-looking" function pointers a real managed ICD would hand out.</summary>
public static unsafe class UcoEntryPoints
{
    /// <summary>Fills FakeIcd.EntryPoints with UCO pointers and returns the vkGetInstanceProcAddr pointer (0 when the runtime has no thunk for it).</summary>
    public static nint Register()
    {
        var map = FakeIcd.EntryPoints;
        map["vkGetInstanceProcAddr"] = (nint)(delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction>)&GetInstanceProcAddr;
        map["vkGetDeviceProcAddr"] = (nint)(delegate* unmanaged[Cdecl]<VkDevice, byte*, PfnVkVoidFunction>)&GetDeviceProcAddr;
        map["vkEnumerateInstanceVersion"] = (nint)(delegate* unmanaged[Cdecl]<int*, VkResult>)&EnumerateInstanceVersion;
        map["vkEnumerateInstanceExtensionProperties"] = (nint)(delegate* unmanaged[Cdecl]<byte*, int*, VkExtensionProperties*, VkResult>)&EnumerateInstanceExtensionProperties;
        map["vkEnumerateInstanceLayerProperties"] = (nint)(delegate* unmanaged[Cdecl]<int*, VkLayerProperties*, VkResult>)&EnumerateInstanceLayerProperties;
        map["vkCreateInstance"] = (nint)(delegate* unmanaged[Cdecl]<VkInstanceCreateInfo*, VkAllocationCallbacks*, VkInstance*, VkResult>)&CreateInstance;
        map["vkDestroyInstance"] = (nint)(delegate* unmanaged[Cdecl]<VkInstance, VkAllocationCallbacks*, void>)&DestroyInstance;
        map["vkEnumeratePhysicalDevices"] = (nint)(delegate* unmanaged[Cdecl]<VkInstance, int*, VkPhysicalDevice*, VkResult>)&EnumeratePhysicalDevices;
        map["vkGetPhysicalDeviceProperties"] = (nint)(delegate* unmanaged[Cdecl]<VkPhysicalDevice, VkPhysicalDeviceProperties*, void>)&GetPhysicalDeviceProperties;
        map["vkGetPhysicalDeviceQueueFamilyProperties"] = (nint)(delegate* unmanaged[Cdecl]<VkPhysicalDevice, int*, VkQueueFamilyProperties*, void>)&GetPhysicalDeviceQueueFamilyProperties;
        map["vkGetPhysicalDeviceMemoryProperties"] = (nint)(delegate* unmanaged[Cdecl]<VkPhysicalDevice, VkPhysicalDeviceMemoryProperties*, void>)&GetPhysicalDeviceMemoryProperties;
        map["vkEnumerateDeviceExtensionProperties"] = (nint)(delegate* unmanaged[Cdecl]<VkPhysicalDevice, byte*, int*, VkExtensionProperties*, VkResult>)&EnumerateDeviceExtensionProperties;
        map["vkCreateDevice"] = (nint)(delegate* unmanaged[Cdecl]<VkPhysicalDevice, VkDeviceCreateInfo*, VkAllocationCallbacks*, VkDevice*, VkResult>)&CreateDevice;
        map["vkDestroyDevice"] = (nint)(delegate* unmanaged[Cdecl]<VkDevice, VkAllocationCallbacks*, void>)&DestroyDevice;
        map["vkGetDeviceQueue"] = (nint)(delegate* unmanaged[Cdecl]<VkDevice, int, int, VkQueue*, void>)&GetDeviceQueue;
        map["vkCreateBuffer"] = (nint)(delegate* unmanaged[Cdecl]<VkDevice, VkBufferCreateInfo*, VkAllocationCallbacks*, VkBuffer*, VkResult>)&CreateBuffer;
        map["vkDestroyBuffer"] = (nint)(delegate* unmanaged[Cdecl]<VkDevice, VkBuffer, VkAllocationCallbacks*, void>)&DestroyBuffer;
        map["vkGetBufferMemoryRequirements"] = (nint)(delegate* unmanaged[Cdecl]<VkDevice, VkBuffer, VkMemoryRequirements*, void>)&GetBufferMemoryRequirements;
        map["vkAllocateMemory"] = (nint)(delegate* unmanaged[Cdecl]<VkDevice, VkMemoryAllocateInfo*, VkAllocationCallbacks*, VkDeviceMemory*, VkResult>)&AllocateMemory;
        map["vkFreeMemory"] = (nint)(delegate* unmanaged[Cdecl]<VkDevice, VkDeviceMemory, VkAllocationCallbacks*, void>)&FreeMemory;
        map["vkBindBufferMemory"] = (nint)(delegate* unmanaged[Cdecl]<VkDevice, VkBuffer, VkDeviceMemory, ByteSize64, VkResult>)&BindBufferMemory;
        return map["vkGetInstanceProcAddr"];
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static PfnVkVoidFunction GetInstanceProcAddr(VkInstance instance, byte* pName) => FakeIcd.GetInstanceProcAddr(instance, pName);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static PfnVkVoidFunction GetDeviceProcAddr(VkDevice device, byte* pName) => FakeIcd.GetDeviceProcAddr(device, pName);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VkResult EnumerateInstanceVersion(int* p) => FakeIcd.EnumerateInstanceVersion(p);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VkResult EnumerateInstanceExtensionProperties(byte* l, int* c, VkExtensionProperties* p) => FakeIcd.EnumerateInstanceExtensionProperties(l, c, p);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VkResult EnumerateInstanceLayerProperties(int* c, VkLayerProperties* p) => FakeIcd.EnumerateInstanceLayerProperties(c, p);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VkResult CreateInstance(VkInstanceCreateInfo* ci, VkAllocationCallbacks* a, VkInstance* i) => FakeIcd.CreateInstance(ci, a, i);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DestroyInstance(VkInstance i, VkAllocationCallbacks* a) => FakeIcd.DestroyInstance(i, a);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VkResult EnumeratePhysicalDevices(VkInstance i, int* c, VkPhysicalDevice* d) => FakeIcd.EnumeratePhysicalDevices(i, c, d);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void GetPhysicalDeviceProperties(VkPhysicalDevice pd, VkPhysicalDeviceProperties* p) => FakeIcd.GetPhysicalDeviceProperties(pd, p);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void GetPhysicalDeviceQueueFamilyProperties(VkPhysicalDevice pd, int* c, VkQueueFamilyProperties* p) => FakeIcd.GetPhysicalDeviceQueueFamilyProperties(pd, c, p);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void GetPhysicalDeviceMemoryProperties(VkPhysicalDevice pd, VkPhysicalDeviceMemoryProperties* p) => FakeIcd.GetPhysicalDeviceMemoryProperties(pd, p);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VkResult EnumerateDeviceExtensionProperties(VkPhysicalDevice pd, byte* l, int* c, VkExtensionProperties* p) => FakeIcd.EnumerateDeviceExtensionProperties(pd, l, c, p);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VkResult CreateDevice(VkPhysicalDevice pd, VkDeviceCreateInfo* ci, VkAllocationCallbacks* a, VkDevice* d) => FakeIcd.CreateDevice(pd, ci, a, d);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DestroyDevice(VkDevice d, VkAllocationCallbacks* a) => FakeIcd.DestroyDevice(d, a);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void GetDeviceQueue(VkDevice d, int f, int i, VkQueue* q) => FakeIcd.GetDeviceQueue(d, f, i, q);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VkResult CreateBuffer(VkDevice d, VkBufferCreateInfo* ci, VkAllocationCallbacks* a, VkBuffer* b) => FakeIcd.CreateBuffer(d, ci, a, b);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DestroyBuffer(VkDevice d, VkBuffer b, VkAllocationCallbacks* a) => FakeIcd.DestroyBuffer(d, b, a);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void GetBufferMemoryRequirements(VkDevice d, VkBuffer b, VkMemoryRequirements* r) => FakeIcd.GetBufferMemoryRequirements(d, b, r);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VkResult AllocateMemory(VkDevice d, VkMemoryAllocateInfo* i, VkAllocationCallbacks* a, VkDeviceMemory* m) => FakeIcd.AllocateMemory(d, i, a, m);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FreeMemory(VkDevice d, VkDeviceMemory m, VkAllocationCallbacks* a) => FakeIcd.FreeMemory(d, m, a);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VkResult BindBufferMemory(VkDevice d, VkBuffer b, VkDeviceMemory m, ByteSize64 o) => FakeIcd.BindBufferMemory(d, b, m, o);
}
