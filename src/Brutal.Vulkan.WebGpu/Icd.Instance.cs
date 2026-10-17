using System.Runtime.InteropServices;
using System.Text;
using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// The ICD entry points: plain static methods with BRUTAL's exact signatures (managed function-pointer flavor), registered by
/// name in <see cref="Registry"/>. One file per concern: Icd.Instance (global commands, instance, physical device), Icd.Device,
/// Icd.Memory, Icd.Buffers, Icd.Images, Icd.Shaders, Icd.Descriptors, Icd.Pipelines, Icd.RenderPasses, Icd.Commands,
/// Icd.Queue, Icd.Swapchain, Icd.Debug.
/// </summary>
internal static unsafe partial class Icd
{
    // ---- proc addr ----------------------------------------------------------------------------------------------------

    public static PfnVkVoidFunction vkGetInstanceProcAddr(VkInstance instance, byte* pName) =>
        new((delegate*<void>)Registry.Lookup(pName));

    public static PfnVkVoidFunction vkGetDeviceProcAddr(VkDevice device, byte* pName) =>
        new((delegate*<void>)Registry.Lookup(pName));

    // ---- global commands ------------------------------------------------------------------------------------------------

    public static VkResult vkEnumerateInstanceVersion(int* pApiVersion)
    {
        *pApiVersion = Profile.ApiVersion;
        return VkResult.Success;
    }

    public static VkResult vkEnumerateInstanceExtensionProperties(byte* pLayerName, int* pPropertyCount, VkExtensionProperties* pProperties)
    {
        if (pLayerName != null)
        {
            *pPropertyCount = 0;
            return VkResult.ErrorLayerNotPresent;
        }
        return WriteExtensions(Profile.InstanceExtensions, pPropertyCount, pProperties);
    }

    public static VkResult vkEnumerateInstanceLayerProperties(int* pPropertyCount, VkLayerProperties* pProperties)
    {
        *pPropertyCount = 0;
        return VkResult.Success;
    }

    internal static VkResult WriteExtensions((string Name, int SpecVersion)[] list, int* pCount, VkExtensionProperties* pProperties)
    {
        if (pProperties == null)
        {
            *pCount = list.Length;
            return VkResult.Success;
        }
        var n = Math.Min(*pCount, list.Length);
        for (var i = 0; i < n; i++)
        {
            Profile.WriteUtf8(pProperties[i].ExtensionName, 256, list[i].Name);
            pProperties[i].SpecVersion = list[i].SpecVersion;
        }
        *pCount = n;
        return n < list.Length ? VkResult.Incomplete : VkResult.Success;
    }

    public static VkResult vkCreateInstance(VkInstanceCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkInstance* pInstance)
    {
        const string fn = "vkCreateInstance";
        var instance = new InstanceObject();
        instance.ApiVersion = pCreateInfo->ApplicationInfo != null ? pCreateInfo->ApplicationInfo->ApiVersion : 1 << 22;

        for (var i = 0; i < pCreateInfo->EnabledExtensionCount; i++)
        {
            var name = Str(pCreateInfo->ppEnabledExtensionNames[i]);
            if (!Profile.HasExtension(Profile.InstanceExtensions, name))
            {
                if (WebIcd.Options.StrictExtensions)
                {
                    IcdLog.Error($"{fn}: instance extension {name} is not in profile {Profile.Name}");
                    return VkResult.ErrorExtensionNotPresent;
                }
                IcdLog.WarnOnce("instext|" + name, $"{fn}: instance extension {name} is not available on WebGPU; ignored");
                continue;
            }
            instance.Extensions.Add(name);
        }
        for (var i = 0; i < pCreateInfo->EnabledLayerCount; i++)
        {
            var name = Str(pCreateInfo->ppEnabledLayerNames[i]);
            if (WebIcd.Options.StrictExtensions)
            {
                IcdLog.Error($"{fn}: layer {name} is not available (the web ICD enumerates no layers)");
                return VkResult.ErrorLayerNotPresent;
            }
            IcdLog.WarnOnce("layer|" + name, $"{fn}: layer {name} is not available in the browser; ignored");
        }

        try
        {
            Profile.EnsureInitialized();
        }
        catch (Exception e)
        {
            IcdLog.Error($"{fn}: cannot read the WebGPU device limits through the bridge: {e.Message}");
            return VkResult.ErrorInitializationFailed;
        }

        HandleTable.Add(instance);
        var physicalDevice = new PhysicalDeviceObject { Instance = instance };
        HandleTable.Add(physicalDevice);
        instance.PhysicalDevice = physicalDevice;

        var messengerInfo = FindNext<VkDebugUtilsMessengerCreateInfoEXT>(pCreateInfo->Next, VkStructureType.DebugUtilsMessengerCreateInfoEXT);
        if (messengerInfo != null)
        {
            instance.CreateInfoMessenger = DebugMessengers.Create(messengerInfo);
            DebugMessengers.Add(instance.CreateInfoMessenger);
        }

        *pInstance = new VkInstance(instance.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyInstance(VkInstance instance, VkAllocationCallbacks* pAllocator)
    {
        var obj = HandleTable.Get<InstanceObject>(instance.VkHandle);
        if (obj is null)
            return;
        if (obj.CreateInfoMessenger is not null)
            DebugMessengers.Remove(obj.CreateInfoMessenger);
        HandleTable.Remove(obj.PhysicalDevice.Handle);
        HandleTable.Remove(obj.Handle);
    }

    public static VkResult vkEnumeratePhysicalDevices(VkInstance instance, int* pPhysicalDeviceCount, VkPhysicalDevice* pPhysicalDevices)
    {
        var obj = HandleTable.Lookup<InstanceObject>(instance.VkHandle, "vkEnumeratePhysicalDevices");
        if (obj is null)
            return VkResult.ErrorInitializationFailed;
        if (pPhysicalDevices == null)
        {
            *pPhysicalDeviceCount = 1;
            return VkResult.Success;
        }
        if (*pPhysicalDeviceCount < 1)
            return VkResult.Incomplete;
        pPhysicalDevices[0] = new VkPhysicalDevice(obj.PhysicalDevice.Handle);
        *pPhysicalDeviceCount = 1;
        return VkResult.Success;
    }

    public static VkResult vkEnumeratePhysicalDeviceGroups(VkInstance instance, int* pCount, VkPhysicalDeviceGroupProperties* pProperties)
    {
        var obj = HandleTable.Lookup<InstanceObject>(instance.VkHandle, "vkEnumeratePhysicalDeviceGroups");
        if (obj is null)
            return VkResult.ErrorInitializationFailed;
        if (pProperties == null)
        {
            *pCount = 1;
            return VkResult.Success;
        }
        if (*pCount < 1)
            return VkResult.Incomplete;
        pProperties[0].PhysicalDeviceCount = 1;
        pProperties[0].PhysicalDevices[0] = new VkPhysicalDevice(obj.PhysicalDevice.Handle);
        pProperties[0].SubsetAllocation = false;
        *pCount = 1;
        return VkResult.Success;
    }

    // ---- physical device queries ------------------------------------------------------------------------------------

    public static void vkGetPhysicalDeviceProperties(VkPhysicalDevice physicalDevice, VkPhysicalDeviceProperties* pProperties) =>
        Profile.FillProperties(pProperties);

    public static void vkGetPhysicalDeviceProperties2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceProperties2* pProperties)
    {
        Profile.FillProperties(&pProperties->Properties);
        for (var p = (VkBaseStructure*)pProperties->Next; p != null; p = (VkBaseStructure*)p->Next)
        {
            switch (p->SType)
            {
                case VkStructureType.PhysicalDevicePortabilitySubsetPropertiesKHR:
                    ((VkPhysicalDevicePortabilitySubsetPropertiesKHR*)p)->MinVertexInputBindingStrideAlignment = 4;
                    break;
                case VkStructureType.PhysicalDeviceMaintenance3Properties:
                {
                    var m = (VkPhysicalDeviceMaintenance3Properties*)p;
                    m->MaxPerSetDescriptors = Profile.MaxPerSetDescriptors;
                    m->MaxMemoryAllocationSize = (ByteSize64)(ulong)Profile.Limits.MaxBufferSize;
                    break;
                }
                case VkStructureType.PhysicalDeviceMaintenance4Properties:
                    ((VkPhysicalDeviceMaintenance4Properties*)p)->MaxBufferSize = (ByteSize64)(ulong)Profile.Limits.MaxBufferSize;
                    break;
                case VkStructureType.PhysicalDeviceDriverProperties:
                {
                    var d = (VkPhysicalDeviceDriverProperties*)p;
                    d->DriverID = default;
                    Profile.WriteUtf8(d->DriverName, 256, Profile.DriverName);
                    Profile.WriteUtf8(d->DriverInfo, 256, Profile.Name);
                    break;
                }
                case VkStructureType.PhysicalDeviceVulkan11Properties:
                    Profile.FillVulkan11Properties((VkPhysicalDeviceVulkan11Properties*)p);
                    break;
                case VkStructureType.PhysicalDeviceVulkan12Properties:
                    Profile.FillVulkan12Properties((VkPhysicalDeviceVulkan12Properties*)p);
                    break;
                case VkStructureType.PhysicalDeviceVulkan13Properties:
                    Profile.FillVulkan13Properties((VkPhysicalDeviceVulkan13Properties*)p);
                    break;
                default:
                    IcdLog.InfoOnce("props2|" + p->SType, $"vkGetPhysicalDeviceProperties2: {p->SType} in pNext is not filled by the web ICD");
                    break;
            }
        }
    }

    public static void vkGetPhysicalDeviceFeatures(VkPhysicalDevice physicalDevice, VkPhysicalDeviceFeatures* pFeatures) =>
        Profile.FillFeatures(pFeatures);

    public static void vkGetPhysicalDeviceFeatures2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceFeatures2* pFeatures)
    {
        Profile.FillFeatures(&pFeatures->Features);
        for (var p = (VkBaseStructure*)pFeatures->Next; p != null; p = (VkBaseStructure*)p->Next)
            if (!FillFeatureStruct(p))
                IcdLog.InfoOnce("feat2|" + p->SType, $"vkGetPhysicalDeviceFeatures2: {p->SType} in pNext is not filled by the web ICD");
    }

    /// <summary>Fills a known pNext feature struct with the supported values; false for unknown structs.</summary>
    internal static bool FillFeatureStruct(VkBaseStructure* p)
    {
        switch (p->SType)
        {
            case VkStructureType.PhysicalDeviceVulkan11Features:
                Profile.FillVulkan11Features((VkPhysicalDeviceVulkan11Features*)p);
                return true;
            case VkStructureType.PhysicalDeviceVulkan12Features:
                Profile.FillVulkan12Features((VkPhysicalDeviceVulkan12Features*)p);
                return true;
            case VkStructureType.PhysicalDeviceVulkan13Features:
                Profile.FillVulkan13Features((VkPhysicalDeviceVulkan13Features*)p);
                return true;
            case VkStructureType.PhysicalDeviceDynamicRenderingFeatures:
                ((VkPhysicalDeviceDynamicRenderingFeatures*)p)->DynamicRendering = true;
                return true;
            case VkStructureType.PhysicalDeviceSynchronization2Features:
                ((VkPhysicalDeviceSynchronization2Features*)p)->Synchronization2 = true;
                return true;
            case VkStructureType.PhysicalDeviceMaintenance4Features:
                ((VkPhysicalDeviceMaintenance4Features*)p)->Maintenance4 = true;
                return true;
            case VkStructureType.PhysicalDevicePortabilitySubsetFeaturesKHR:
                Profile.FillPortabilitySubsetFeatures((VkPhysicalDevicePortabilitySubsetFeaturesKHR*)p);
                return true;
            case VkStructureType.PhysicalDeviceImagelessFramebufferFeatures:
                ((VkPhysicalDeviceImagelessFramebufferFeatures*)p)->ImagelessFramebuffer = true;
                return true;
            case VkStructureType.PhysicalDeviceSeparateDepthStencilLayoutsFeatures:
                ((VkPhysicalDeviceSeparateDepthStencilLayoutsFeatures*)p)->SeparateDepthStencilLayouts = true;
                return true;
            case VkStructureType.PhysicalDeviceHostQueryResetFeatures:
                ((VkPhysicalDeviceHostQueryResetFeatures*)p)->HostQueryReset = true;
                return true;
            case VkStructureType.PhysicalDeviceShaderDemoteToHelperInvocationFeatures:
                ((VkPhysicalDeviceShaderDemoteToHelperInvocationFeatures*)p)->ShaderDemoteToHelperInvocation = true;
                return true;
            // the 1.3 features promoted from extensions, as stand-alone structs: same values as VkPhysicalDeviceVulkan13Features
            case VkStructureType.PhysicalDeviceImageRobustnessFeatures:
                ((VkPhysicalDeviceImageRobustnessFeatures*)p)->RobustImageAccess = true;
                return true;
            case VkStructureType.PhysicalDeviceShaderTerminateInvocationFeatures:
                ((VkPhysicalDeviceShaderTerminateInvocationFeatures*)p)->ShaderTerminateInvocation = true;
                return true;
            case VkStructureType.PhysicalDeviceZeroInitializeWorkgroupMemoryFeatures:
                ((VkPhysicalDeviceZeroInitializeWorkgroupMemoryFeatures*)p)->ShaderZeroInitializeWorkgroupMemory = true;
                return true;
            case VkStructureType.PhysicalDeviceMultiviewFeatures:
            case VkStructureType.PhysicalDeviceShaderDrawParametersFeatures:
            case VkStructureType.PhysicalDeviceDescriptorIndexingFeatures:
            case VkStructureType.PhysicalDeviceBufferDeviceAddressFeatures:
            case VkStructureType.PhysicalDeviceTimelineSemaphoreFeatures:
            case VkStructureType.PhysicalDeviceScalarBlockLayoutFeatures:
            case VkStructureType.PhysicalDevice16BitStorageFeatures:
            case VkStructureType.PhysicalDeviceExtendedDynamicStateFeaturesEXT:
            case VkStructureType.PhysicalDevicePrivateDataFeatures:
            case VkStructureType.PhysicalDevicePipelineCreationCacheControlFeatures:
            case VkStructureType.PhysicalDeviceInlineUniformBlockFeatures:
            case VkStructureType.PhysicalDeviceSubgroupSizeControlFeatures:
            case VkStructureType.PhysicalDeviceShaderIntegerDotProductFeatures:
                ClearFeatureStructOfKnownSize(p);
                return true;
            default:
                return false;
        }
    }

    private static void ClearFeatureStructOfKnownSize(VkBaseStructure* p)
    {
        var size = p->SType switch
        {
            VkStructureType.PhysicalDeviceMultiviewFeatures => sizeof(VkPhysicalDeviceMultiviewFeatures),
            VkStructureType.PhysicalDeviceShaderDrawParametersFeatures => sizeof(VkPhysicalDeviceShaderDrawParametersFeatures),
            VkStructureType.PhysicalDeviceDescriptorIndexingFeatures => sizeof(VkPhysicalDeviceDescriptorIndexingFeatures),
            VkStructureType.PhysicalDeviceBufferDeviceAddressFeatures => sizeof(VkPhysicalDeviceBufferDeviceAddressFeatures),
            VkStructureType.PhysicalDeviceTimelineSemaphoreFeatures => sizeof(VkPhysicalDeviceTimelineSemaphoreFeatures),
            VkStructureType.PhysicalDeviceScalarBlockLayoutFeatures => sizeof(VkPhysicalDeviceScalarBlockLayoutFeatures),
            VkStructureType.PhysicalDevice16BitStorageFeatures => sizeof(VkPhysicalDevice16BitStorageFeatures),
            VkStructureType.PhysicalDeviceExtendedDynamicStateFeaturesEXT => sizeof(VkPhysicalDeviceExtendedDynamicStateFeaturesEXT),
            VkStructureType.PhysicalDevicePrivateDataFeatures => sizeof(VkPhysicalDevicePrivateDataFeatures),
            VkStructureType.PhysicalDevicePipelineCreationCacheControlFeatures => sizeof(VkPhysicalDevicePipelineCreationCacheControlFeatures),
            VkStructureType.PhysicalDeviceInlineUniformBlockFeatures => sizeof(VkPhysicalDeviceInlineUniformBlockFeatures),
            VkStructureType.PhysicalDeviceSubgroupSizeControlFeatures => sizeof(VkPhysicalDeviceSubgroupSizeControlFeatures),
            VkStructureType.PhysicalDeviceShaderIntegerDotProductFeatures => sizeof(VkPhysicalDeviceShaderIntegerDotProductFeatures),
            _ => 16,
        };
        Profile.ClearBools(p, size);
    }

    public static void vkGetPhysicalDeviceQueueFamilyProperties(VkPhysicalDevice physicalDevice, int* pCount, VkQueueFamilyProperties* pProperties)
    {
        if (pProperties == null)
        {
            *pCount = 1;
            return;
        }
        if (*pCount < 1)
            return;
        Profile.FillQueueFamily(pProperties);
        *pCount = 1;
    }

    public static void vkGetPhysicalDeviceQueueFamilyProperties2(VkPhysicalDevice physicalDevice, int* pCount, VkQueueFamilyProperties2* pProperties)
    {
        if (pProperties == null)
        {
            *pCount = 1;
            return;
        }
        if (*pCount < 1)
            return;
        Profile.FillQueueFamily(&pProperties->QueueFamilyProperties);
        *pCount = 1;
    }

    public static void vkGetPhysicalDeviceMemoryProperties(VkPhysicalDevice physicalDevice, VkPhysicalDeviceMemoryProperties* pProperties) =>
        Profile.FillMemoryProperties(pProperties);

    public static void vkGetPhysicalDeviceMemoryProperties2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceMemoryProperties2* pProperties) =>
        Profile.FillMemoryProperties(&pProperties->MemoryProperties);

    public static void vkGetPhysicalDeviceFormatProperties(VkPhysicalDevice physicalDevice, VkFormat format, VkFormatProperties* pProperties)
    {
        pProperties->LinearTilingFeatures = VkFormatFeatureFlags.None;
        pProperties->OptimalTilingFeatures = Formats.OptimalTilingFeatures(format, Profile.GpuFeatures);
        pProperties->BufferFeatures = Formats.BufferFeatures(format);
    }

    public static void vkGetPhysicalDeviceFormatProperties2(VkPhysicalDevice physicalDevice, VkFormat format, VkFormatProperties2* pProperties) =>
        vkGetPhysicalDeviceFormatProperties(physicalDevice, format, &pProperties->FormatProperties);

    public static VkResult vkGetPhysicalDeviceImageFormatProperties(VkPhysicalDevice physicalDevice, VkFormat format, VkImageType type,
        VkImageTiling tiling, VkImageUsageFlags usage, VkImageCreateFlags flags, VkImageFormatProperties* pProperties)
    {
        *pProperties = default;
        var features = Formats.OptimalTilingFeatures(format, Profile.GpuFeatures);
        if (tiling != VkImageTiling.Optimal || features == VkFormatFeatureFlags.None || Mappings.TextureDimension(type) is null)
            return VkResult.ErrorFormatNotSupported;
        if ((usage & VkImageUsageFlags.ColorAttachmentBit) != 0 && (features & VkFormatFeatureFlags.ColorAttachmentBit) == 0
            || (usage & VkImageUsageFlags.DepthStencilAttachmentBit) != 0 && (features & VkFormatFeatureFlags.DepthStencilAttachmentBit) == 0
            || (usage & VkImageUsageFlags.SampledBit) != 0 && (features & VkFormatFeatureFlags.SampledImageBit) == 0
            || (usage & VkImageUsageFlags.StorageBit) != 0 && (features & VkFormatFeatureFlags.StorageImageBit) == 0)
            return VkResult.ErrorFormatNotSupported;
        ref readonly var l = ref Profile.Limits;
        var max = type switch { VkImageType._1D => l.MaxTextureDimension1D, VkImageType._3D => l.MaxTextureDimension3D, _ => l.MaxTextureDimension2D };
        pProperties->MaxExtent = new VkExtent3D(max, type == VkImageType._1D ? 1 : max, type == VkImageType._3D ? max : 1);
        pProperties->MaxMipLevels = type == VkImageType._1D ? 1 : BitOperationsLog2(max) + 1;
        pProperties->MaxArrayLayers = type == VkImageType._3D ? 1 : l.MaxTextureArrayLayers;
        pProperties->SampleCounts = type == VkImageType._2D && (features & (VkFormatFeatureFlags.ColorAttachmentBit | VkFormatFeatureFlags.DepthStencilAttachmentBit)) != 0
            ? VkSampleCountFlags._1Bit | VkSampleCountFlags._4Bit
            : VkSampleCountFlags._1Bit;
        pProperties->MaxResourceSize = (ByteSize64)(ulong)l.MaxBufferSize;
        return VkResult.Success;
    }

    public static VkResult vkGetPhysicalDeviceImageFormatProperties2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceImageFormatInfo2* pInfo,
        VkImageFormatProperties2* pProperties) =>
        vkGetPhysicalDeviceImageFormatProperties(physicalDevice, pInfo->Format, pInfo->Type, pInfo->Tiling, pInfo->Usage, pInfo->Flags,
            &pProperties->ImageFormatProperties);

    public static void vkGetPhysicalDeviceSparseImageFormatProperties(VkPhysicalDevice physicalDevice, VkFormat format, VkImageType type,
        VkSampleCountFlags samples, VkImageUsageFlags usage, VkImageTiling tiling, int* pCount, VkSparseImageFormatProperties* pProperties) =>
        *pCount = 0;

    public static VkResult vkGetPhysicalDeviceToolProperties(VkPhysicalDevice physicalDevice, int* pCount, VkPhysicalDeviceToolProperties* pProperties)
    {
        *pCount = 0;
        return VkResult.Success;
    }

    public static VkResult vkEnumerateDeviceExtensionProperties(VkPhysicalDevice physicalDevice, byte* pLayerName, int* pCount,
        VkExtensionProperties* pProperties)
    {
        if (pLayerName != null)
        {
            *pCount = 0;
            return VkResult.ErrorLayerNotPresent;
        }
        return WriteExtensions(Profile.DeviceExtensions, pCount, pProperties);
    }

    public static VkResult vkEnumerateDeviceLayerProperties(VkPhysicalDevice physicalDevice, int* pCount, VkLayerProperties* pProperties)
    {
        *pCount = 0;
        return VkResult.Success;
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    internal static string Str(byte* utf8) => Marshal.PtrToStringUTF8((nint)utf8) ?? "";

    internal static T* FindNext<T>(void* pNext, VkStructureType sType) where T : unmanaged
    {
        for (var p = (VkBaseStructure*)pNext; p != null; p = (VkBaseStructure*)p->Next)
            if (p->SType == sType)
                return (T*)p;
        return null;
    }

    private static int BitOperationsLog2(int value) => System.Numerics.BitOperations.Log2((uint)value);
}

/// <summary>The common header of Vulkan extensible structs (BRUTAL's 64-bit explicit layout: sType @0, pNext @8).</summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal unsafe struct VkBaseStructure
{
    [FieldOffset(0)] public VkStructureType SType;
    [FieldOffset(8)] public void* Next;
}
