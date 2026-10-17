using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

internal static unsafe partial class Icd
{
    public static VkResult vkCreateDevice(VkPhysicalDevice physicalDevice, VkDeviceCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator,
        VkDevice* pDevice)
    {
        const string fn = "vkCreateDevice";
        var pd = HandleTable.Lookup<PhysicalDeviceObject>(physicalDevice.VkHandle, fn);
        if (pd is null)
            return VkResult.ErrorInitializationFailed;

        for (var i = 0; i < pCreateInfo->QueueCreateInfoCount; i++)
        {
            ref var q = ref pCreateInfo->QueueCreateInfos[i];
            if (q.QueueFamilyIndex != 0 || q.QueueCount > 1)
            {
                IcdLog.Error($"{fn}: queue family {q.QueueFamilyIndex} x{q.QueueCount} requested; WebGPU has one queue (family 0, count 1)");
                return VkResult.ErrorInitializationFailed;
            }
        }

        var device = new DeviceObject { PhysicalDevice = pd };
        for (var i = 0; i < pCreateInfo->EnabledExtensionCount; i++)
        {
            var name = Str(pCreateInfo->ppEnabledExtensionNames[i]);
            if (!Profile.HasExtension(Profile.DeviceExtensions, name))
            {
                if (WebIcd.Options.StrictExtensions)
                {
                    IcdLog.Error($"{fn}: device extension {name} is not in profile {Profile.Name}");
                    return VkResult.ErrorExtensionNotPresent;
                }
                IcdLog.WarnOnce("devext|" + name, $"{fn}: device extension {name} is not available on WebGPU; ignored");
                continue;
            }
            device.Extensions.Add(name);
        }

        // features: everything requested must be supported (VK_ERROR_FEATURE_NOT_PRESENT otherwise)
        VkPhysicalDeviceFeatures supported;
        Profile.FillFeatures(&supported);
        if (pCreateInfo->EnabledFeatures != null
            && !CheckFeatures(pCreateInfo->EnabledFeatures, &supported, 0, sizeof(VkPhysicalDeviceFeatures), FeatureNames.Features, "VkPhysicalDeviceFeatures"))
            return VkResult.ErrorFeatureNotPresent;

        var copy = stackalloc byte[256]; // larger than every feature struct checked below
        for (var p = (VkBaseStructure*)pCreateInfo->Next; p != null; p = (VkBaseStructure*)p->Next)
        {
            if (p->SType == VkStructureType.PhysicalDeviceFeatures2)
            {
                if (!CheckFeatures(&((VkPhysicalDeviceFeatures2*)p)->Features, &supported, 0, sizeof(VkPhysicalDeviceFeatures), FeatureNames.Features,
                        "VkPhysicalDeviceFeatures2.features"))
                    return VkResult.ErrorFeatureNotPresent;
                continue;
            }
            var size = FeatureStructSize(p->SType, out var names);
            if (size == 0)
            {
                IcdLog.InfoOnce("devnext|" + p->SType, $"{fn}: {p->SType} in pNext is ignored");
                continue;
            }
            new Span<byte>(copy, size).Clear();
            ((VkBaseStructure*)copy)->SType = p->SType;
            ((VkBaseStructure*)copy)->Next = null;
            FillFeatureStruct((VkBaseStructure*)copy);
            if (!CheckFeatures(p, copy, 16, size, names, p->SType.ToString()))
                return VkResult.ErrorFeatureNotPresent;
        }

        HandleTable.Add(device);
        var queue = new QueueObject { Device = device };
        HandleTable.Add(queue);
        device.Queue = queue;
        *pDevice = new VkDevice(device.Handle);
        return VkResult.Success;
    }

    private static int FeatureStructSize(VkStructureType sType, out string[]? names)
    {
        names = null;
        switch (sType)
        {
            case VkStructureType.PhysicalDeviceVulkan11Features: names = FeatureNames.Vulkan11Features; return sizeof(VkPhysicalDeviceVulkan11Features);
            case VkStructureType.PhysicalDeviceVulkan12Features: names = FeatureNames.Vulkan12Features; return sizeof(VkPhysicalDeviceVulkan12Features);
            case VkStructureType.PhysicalDeviceVulkan13Features: names = FeatureNames.Vulkan13Features; return sizeof(VkPhysicalDeviceVulkan13Features);
            case VkStructureType.PhysicalDevicePortabilitySubsetFeaturesKHR: names = FeatureNames.PortabilitySubsetFeatures; return sizeof(VkPhysicalDevicePortabilitySubsetFeaturesKHR);
            case VkStructureType.PhysicalDeviceDynamicRenderingFeatures: return sizeof(VkPhysicalDeviceDynamicRenderingFeatures);
            case VkStructureType.PhysicalDeviceSynchronization2Features: return sizeof(VkPhysicalDeviceSynchronization2Features);
            case VkStructureType.PhysicalDeviceMaintenance4Features: return sizeof(VkPhysicalDeviceMaintenance4Features);
            case VkStructureType.PhysicalDeviceImagelessFramebufferFeatures: return sizeof(VkPhysicalDeviceImagelessFramebufferFeatures);
            case VkStructureType.PhysicalDeviceSeparateDepthStencilLayoutsFeatures: return sizeof(VkPhysicalDeviceSeparateDepthStencilLayoutsFeatures);
            case VkStructureType.PhysicalDeviceHostQueryResetFeatures: return sizeof(VkPhysicalDeviceHostQueryResetFeatures);
            case VkStructureType.PhysicalDeviceShaderDemoteToHelperInvocationFeatures: return sizeof(VkPhysicalDeviceShaderDemoteToHelperInvocationFeatures);
            case VkStructureType.PhysicalDeviceMultiviewFeatures: return sizeof(VkPhysicalDeviceMultiviewFeatures);
            case VkStructureType.PhysicalDeviceShaderDrawParametersFeatures: return sizeof(VkPhysicalDeviceShaderDrawParametersFeatures);
            case VkStructureType.PhysicalDeviceDescriptorIndexingFeatures: return sizeof(VkPhysicalDeviceDescriptorIndexingFeatures);
            case VkStructureType.PhysicalDeviceBufferDeviceAddressFeatures: return sizeof(VkPhysicalDeviceBufferDeviceAddressFeatures);
            case VkStructureType.PhysicalDeviceTimelineSemaphoreFeatures: return sizeof(VkPhysicalDeviceTimelineSemaphoreFeatures);
            case VkStructureType.PhysicalDeviceScalarBlockLayoutFeatures: return sizeof(VkPhysicalDeviceScalarBlockLayoutFeatures);
            case VkStructureType.PhysicalDevice16BitStorageFeatures: return sizeof(VkPhysicalDevice16BitStorageFeatures);
            case VkStructureType.PhysicalDeviceExtendedDynamicStateFeaturesEXT: return sizeof(VkPhysicalDeviceExtendedDynamicStateFeaturesEXT);
            case VkStructureType.PhysicalDeviceImageRobustnessFeatures: return sizeof(VkPhysicalDeviceImageRobustnessFeatures);
            case VkStructureType.PhysicalDeviceShaderTerminateInvocationFeatures: return sizeof(VkPhysicalDeviceShaderTerminateInvocationFeatures);
            case VkStructureType.PhysicalDeviceZeroInitializeWorkgroupMemoryFeatures: return sizeof(VkPhysicalDeviceZeroInitializeWorkgroupMemoryFeatures);
            case VkStructureType.PhysicalDevicePrivateDataFeatures: return sizeof(VkPhysicalDevicePrivateDataFeatures);
            case VkStructureType.PhysicalDevicePipelineCreationCacheControlFeatures: return sizeof(VkPhysicalDevicePipelineCreationCacheControlFeatures);
            case VkStructureType.PhysicalDeviceInlineUniformBlockFeatures: return sizeof(VkPhysicalDeviceInlineUniformBlockFeatures);
            case VkStructureType.PhysicalDeviceSubgroupSizeControlFeatures: return sizeof(VkPhysicalDeviceSubgroupSizeControlFeatures);
            case VkStructureType.PhysicalDeviceShaderIntegerDotProductFeatures: return sizeof(VkPhysicalDeviceShaderIntegerDotProductFeatures);
            default: return 0;
        }
    }

    /// <summary>Compares the Bool32 members in [start, size): every requested feature must be supported.</summary>
    private static bool CheckFeatures(void* requested, void* supported, int start, int size, string[]? names, string structName)
    {
        var ok = true;
        for (var offset = start; offset + 4 <= size; offset += 4)
        {
            if (*(int*)((byte*)requested + offset) != 0 && *(int*)((byte*)supported + offset) == 0)
            {
                var index = (offset - start) / 4;
                var member = names is not null && index < names.Length ? names[index] : $"member at offset {offset}";
                IcdLog.Error($"vkCreateDevice: feature {structName}.{member} is not supported by the web ICD (profile {Profile.Name})");
                ok = false;
            }
        }
        return ok;
    }

    public static void vkDestroyDevice(VkDevice device, VkAllocationCallbacks* pAllocator)
    {
        var obj = HandleTable.Get<DeviceObject>(device.VkHandle);
        if (obj is null)
            return;
        obj.Queue.SubmitStream.Dispose();
        HandleTable.Remove(obj.Queue.Handle);
        HandleTable.Remove(obj.Handle);
    }

    public static void vkGetDeviceQueue(VkDevice device, int queueFamilyIndex, int queueIndex, VkQueue* pQueue)
    {
        var obj = HandleTable.Lookup<DeviceObject>(device.VkHandle, "vkGetDeviceQueue");
        if (queueFamilyIndex != 0 || queueIndex != 0)
            IcdLog.ErrorOnce("queue", $"vkGetDeviceQueue: family {queueFamilyIndex} index {queueIndex} does not exist; returning queue (0,0)");
        *pQueue = new VkQueue(obj?.Queue.Handle ?? 0);
    }

    public static void vkGetDeviceQueue2(VkDevice device, VkDeviceQueueInfo2* pQueueInfo, VkQueue* pQueue) =>
        vkGetDeviceQueue(device, pQueueInfo->QueueFamilyIndex, pQueueInfo->QueueIndex, pQueue);

    public static VkResult vkDeviceWaitIdle(VkDevice device)
    {
        IcdLog.InfoOnce("waitidle", "vkDeviceWaitIdle/vkQueueWaitIdle return immediately in the browser (work is ordered on the one WebGPU queue; GPU→CPU data arrives on a later frame)");
        return VkResult.Success;
    }
}
