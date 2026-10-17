using System.Text;
using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// UTF-8 command name → managed function pointer, for <c>vkGetInstanceProcAddr</c>/<c>vkGetDeviceProcAddr</c>. The generated
/// stub table (one exact-signature stub per command, <c>Generated/StubTable.g.cs</c>) is loaded first and the hand-written
/// <see cref="ImplementedTable"/> overrides it, so every name BRUTAL requests resolves to a correctly typed function.
/// Unknown names return 0 (only for names BRUTAL never requests). Lookups do not allocate.
/// </summary>
internal static class Registry
{
    private static Dictionary<string, nint>? s_table;
    private static HashSet<string>? s_implemented;
    private static Dictionary<string, nint>.AlternateLookup<ReadOnlySpan<char>> s_lookup;

    public static void EnsureBuilt()
    {
        if (s_table is not null)
            return;
        var table = new Dictionary<string, nint>(1024, StringComparer.Ordinal);
        IcdStubTable.AddAll(table);
        var implemented = new Dictionary<string, nint>(256, StringComparer.Ordinal);
        ImplementedTable.AddAll(implemented);
        foreach (var (name, fn) in implemented)
            table[name] = fn;
        s_implemented = new HashSet<string>(implemented.Keys, StringComparer.Ordinal);
        s_lookup = table.GetAlternateLookup<ReadOnlySpan<char>>();
        s_table = table;
    }

    /// <summary>Every name the ICD answers (stubs and implementations).</summary>
    public static IReadOnlyCollection<string> AllNames
    {
        get
        {
            EnsureBuilt();
            return s_table!.Keys;
        }
    }

    /// <summary>The names with a hand-written implementation.</summary>
    public static IReadOnlySet<string> ImplementedNames
    {
        get
        {
            EnsureBuilt();
            return s_implemented!;
        }
    }

    public static unsafe nint Lookup(byte* pName)
    {
        EnsureBuilt();
        if (pName == null)
            return 0;
        var length = 0;
        while (length < 256 && pName[length] != 0)
            length++;
        Span<char> chars = stackalloc char[length];
        var n = Encoding.UTF8.GetChars(new ReadOnlySpan<byte>(pName, length), chars);
        return s_lookup.TryGetValue(chars[..n], out var fn) ? fn : 0;
    }

    public static nint Lookup(string name)
    {
        EnsureBuilt();
        return s_table!.TryGetValue(name, out var fn) ? fn : 0;
    }
}

/// <summary>
/// The hand-written entry points. Each address is taken through BRUTAL's own function-pointer struct for that command, so the
/// compiler checks every signature against the binding. KHR/EXT aliases share the core implementation.
/// </summary>
internal static unsafe class ImplementedTable
{
    private static void Add(Dictionary<string, nint> t, string name, nint fn) => t[name] = fn;

    public static void AddAll(Dictionary<string, nint> t)
    {
        // global commands (no wrapper struct in Functions.gen.cs; the patched loader casts GIPA(NULL, name) to these types)
        Add(t, "vkGetInstanceProcAddr", (nint)new VkGetInstanceProcAddr(&Icd.vkGetInstanceProcAddr).Invoke);
        Add(t, "vkCreateInstance", (nint)(delegate*<VkInstanceCreateInfo*, VkAllocationCallbacks*, VkInstance*, VkResult>)&Icd.vkCreateInstance);
        Add(t, "vkEnumerateInstanceExtensionProperties", (nint)(delegate*<byte*, int*, VkExtensionProperties*, VkResult>)&Icd.vkEnumerateInstanceExtensionProperties);
        Add(t, "vkEnumerateInstanceLayerProperties", (nint)(delegate*<int*, VkLayerProperties*, VkResult>)&Icd.vkEnumerateInstanceLayerProperties);
        Add(t, "vkEnumerateInstanceVersion", (nint)(delegate*<int*, VkResult>)&Icd.vkEnumerateInstanceVersion);
        // VK_BRUTAL_web_surface
        Add(t, "vkCreateWebSurfaceBRUTAL",
            (nint)(delegate*<VkInstance, VkWebSurfaceCreateInfoBRUTAL*, VkAllocationCallbacks*, VkSurfaceKHR*, VkResult>)&Icd.vkCreateWebSurfaceBRUTAL);

        // Icd.Buffers.cs
        Add(t, "vkCreateBuffer", (nint)new VkCreateBuffer(&Icd.vkCreateBuffer).Invoke);
        Add(t, "vkDestroyBuffer", (nint)new VkDestroyBuffer(&Icd.vkDestroyBuffer).Invoke);
        Add(t, "vxDestroyBuffers", (nint)new VxDestroyBuffers(&Icd.vxDestroyBuffers).Invoke);
        Add(t, "vxDestroyBufferViews", (nint)new VxDestroyBufferViews(&Icd.vxDestroyBufferViews).Invoke);
        Add(t, "vkGetBufferMemoryRequirements", (nint)new VkGetBufferMemoryRequirements(&Icd.vkGetBufferMemoryRequirements).Invoke);
        Add(t, "vkGetBufferMemoryRequirements2", (nint)new VkGetBufferMemoryRequirements2(&Icd.vkGetBufferMemoryRequirements2).Invoke);
        Add(t, "vkGetBufferMemoryRequirements2KHR", (nint)new VkGetBufferMemoryRequirements2KHR(&Icd.vkGetBufferMemoryRequirements2).Invoke);
        Add(t, "vkGetDeviceBufferMemoryRequirements", (nint)new VkGetDeviceBufferMemoryRequirements(&Icd.vkGetDeviceBufferMemoryRequirements).Invoke);
        Add(t, "vkGetDeviceBufferMemoryRequirementsKHR", (nint)new VkGetDeviceBufferMemoryRequirementsKHR(&Icd.vkGetDeviceBufferMemoryRequirements).Invoke);
        Add(t, "vkBindBufferMemory", (nint)new VkBindBufferMemory(&Icd.vkBindBufferMemory).Invoke);
        Add(t, "vkBindBufferMemory2", (nint)new VkBindBufferMemory2(&Icd.vkBindBufferMemory2).Invoke);
        Add(t, "vkBindBufferMemory2KHR", (nint)new VkBindBufferMemory2KHR(&Icd.vkBindBufferMemory2).Invoke);
        Add(t, "vkGetBufferDeviceAddress", (nint)new VkGetBufferDeviceAddress(&Icd.vkGetBufferDeviceAddress).Invoke);
        Add(t, "vkGetBufferDeviceAddressKHR", (nint)new VkGetBufferDeviceAddressKHR(&Icd.vkGetBufferDeviceAddress).Invoke);
        Add(t, "vkGetBufferDeviceAddressEXT", (nint)new VkGetBufferDeviceAddressEXT(&Icd.vkGetBufferDeviceAddress).Invoke);
        // Icd.Commands.cs
        Add(t, "vkCreateCommandPool", (nint)new VkCreateCommandPool(&Icd.vkCreateCommandPool).Invoke);
        Add(t, "vkDestroyCommandPool", (nint)new VkDestroyCommandPool(&Icd.vkDestroyCommandPool).Invoke);
        Add(t, "vkResetCommandPool", (nint)new VkResetCommandPool(&Icd.vkResetCommandPool).Invoke);
        Add(t, "vkTrimCommandPool", (nint)new VkTrimCommandPool(&Icd.vkTrimCommandPool).Invoke);
        Add(t, "vkTrimCommandPoolKHR", (nint)new VkTrimCommandPoolKHR(&Icd.vkTrimCommandPool).Invoke);
        Add(t, "vkAllocateCommandBuffers", (nint)new VkAllocateCommandBuffers(&Icd.vkAllocateCommandBuffers).Invoke);
        Add(t, "vkFreeCommandBuffers", (nint)new VkFreeCommandBuffers(&Icd.vkFreeCommandBuffers).Invoke);
        Add(t, "vkBeginCommandBuffer", (nint)new VkBeginCommandBuffer(&Icd.vkBeginCommandBuffer).Invoke);
        Add(t, "vkEndCommandBuffer", (nint)new VkEndCommandBuffer(&Icd.vkEndCommandBuffer).Invoke);
        Add(t, "vkResetCommandBuffer", (nint)new VkResetCommandBuffer(&Icd.vkResetCommandBuffer).Invoke);
        Add(t, "vkCmdBindPipeline", (nint)new VkCmdBindPipeline(&Icd.vkCmdBindPipeline).Invoke);
        Add(t, "vkCmdBindDescriptorSets", (nint)new VkCmdBindDescriptorSets(&Icd.vkCmdBindDescriptorSets).Invoke);
        Add(t, "vkCmdBindVertexBuffers", (nint)new VkCmdBindVertexBuffers(&Icd.vkCmdBindVertexBuffers).Invoke);
        Add(t, "vkCmdBindVertexBuffers2", (nint)new VkCmdBindVertexBuffers2(&Icd.vkCmdBindVertexBuffers2).Invoke);
        Add(t, "vkCmdBindVertexBuffers2EXT", (nint)new VkCmdBindVertexBuffers2EXT(&Icd.vkCmdBindVertexBuffers2).Invoke);
        Add(t, "vkCmdBindIndexBuffer", (nint)new VkCmdBindIndexBuffer(&Icd.vkCmdBindIndexBuffer).Invoke);
        Add(t, "vkCmdBindIndexBuffer2", (nint)new VkCmdBindIndexBuffer2(&Icd.vkCmdBindIndexBuffer2).Invoke);
        Add(t, "vkCmdBindIndexBuffer2KHR", (nint)new VkCmdBindIndexBuffer2KHR(&Icd.vkCmdBindIndexBuffer2).Invoke);
        Add(t, "vkCmdSetViewport", (nint)new VkCmdSetViewport(&Icd.vkCmdSetViewport).Invoke);
        Add(t, "vkCmdSetViewportWithCount", (nint)new VkCmdSetViewportWithCount(&Icd.vkCmdSetViewportWithCount).Invoke);
        Add(t, "vkCmdSetViewportWithCountEXT", (nint)new VkCmdSetViewportWithCountEXT(&Icd.vkCmdSetViewportWithCount).Invoke);
        Add(t, "vkCmdSetScissor", (nint)new VkCmdSetScissor(&Icd.vkCmdSetScissor).Invoke);
        Add(t, "vkCmdSetScissorWithCount", (nint)new VkCmdSetScissorWithCount(&Icd.vkCmdSetScissorWithCount).Invoke);
        Add(t, "vkCmdSetScissorWithCountEXT", (nint)new VkCmdSetScissorWithCountEXT(&Icd.vkCmdSetScissorWithCount).Invoke);
        Add(t, "vkCmdSetBlendConstants", (nint)new VkCmdSetBlendConstants(&Icd.vkCmdSetBlendConstants).Invoke);
        Add(t, "vkCmdSetStencilReference", (nint)new VkCmdSetStencilReference(&Icd.vkCmdSetStencilReference).Invoke);
        Add(t, "vkCmdSetLineWidth", (nint)new VkCmdSetLineWidth(&Icd.vkCmdSetLineWidth).Invoke);
        Add(t, "vkCmdSetDepthBias", (nint)new VkCmdSetDepthBias(&Icd.vkCmdSetDepthBias).Invoke);
        Add(t, "vkCmdSetDepthBounds", (nint)new VkCmdSetDepthBounds(&Icd.vkCmdSetDepthBounds).Invoke);
        Add(t, "vkCmdSetStencilCompareMask", (nint)new VkCmdSetStencilCompareMask(&Icd.vkCmdSetStencilCompareMask).Invoke);
        Add(t, "vkCmdSetStencilWriteMask", (nint)new VkCmdSetStencilWriteMask(&Icd.vkCmdSetStencilWriteMask).Invoke);
        Add(t, "vkCmdPushConstants", (nint)new VkCmdPushConstants(&Icd.vkCmdPushConstants).Invoke);
        Add(t, "vkCmdDraw", (nint)new VkCmdDraw(&Icd.vkCmdDraw).Invoke);
        Add(t, "vkCmdDrawIndexed", (nint)new VkCmdDrawIndexed(&Icd.vkCmdDrawIndexed).Invoke);
        Add(t, "vkCmdDrawIndirect", (nint)new VkCmdDrawIndirect(&Icd.vkCmdDrawIndirect).Invoke);
        Add(t, "vkCmdDrawIndexedIndirect", (nint)new VkCmdDrawIndexedIndirect(&Icd.vkCmdDrawIndexedIndirect).Invoke);
        Add(t, "vkCmdDispatch", (nint)new VkCmdDispatch(&Icd.vkCmdDispatch).Invoke);
        Add(t, "vkCmdDispatchIndirect", (nint)new VkCmdDispatchIndirect(&Icd.vkCmdDispatchIndirect).Invoke);
        Add(t, "vkCmdPipelineBarrier", (nint)new VkCmdPipelineBarrier(&Icd.vkCmdPipelineBarrier).Invoke);
        Add(t, "vkCmdPipelineBarrier2", (nint)new VkCmdPipelineBarrier2(&Icd.vkCmdPipelineBarrier2).Invoke);
        Add(t, "vkCmdPipelineBarrier2KHR", (nint)new VkCmdPipelineBarrier2KHR(&Icd.vkCmdPipelineBarrier2).Invoke);
        Add(t, "vkCmdSetEvent", (nint)new VkCmdSetEvent(&Icd.vkCmdSetEvent).Invoke);
        Add(t, "vkCmdResetEvent", (nint)new VkCmdResetEvent(&Icd.vkCmdResetEvent).Invoke);
        Add(t, "vkCmdWaitEvents", (nint)new VkCmdWaitEvents(&Icd.vkCmdWaitEvents).Invoke);
        Add(t, "vkCmdSetEvent2", (nint)new VkCmdSetEvent2(&Icd.vkCmdSetEvent2).Invoke);
        Add(t, "vkCmdSetEvent2KHR", (nint)new VkCmdSetEvent2KHR(&Icd.vkCmdSetEvent2).Invoke);
        Add(t, "vkCmdResetEvent2", (nint)new VkCmdResetEvent2(&Icd.vkCmdResetEvent2).Invoke);
        Add(t, "vkCmdResetEvent2KHR", (nint)new VkCmdResetEvent2KHR(&Icd.vkCmdResetEvent2).Invoke);
        Add(t, "vkCmdWaitEvents2", (nint)new VkCmdWaitEvents2(&Icd.vkCmdWaitEvents2).Invoke);
        Add(t, "vkCmdWaitEvents2KHR", (nint)new VkCmdWaitEvents2KHR(&Icd.vkCmdWaitEvents2).Invoke);
        Add(t, "vkCmdExecuteCommands", (nint)new VkCmdExecuteCommands(&Icd.vkCmdExecuteCommands).Invoke);
        // Icd.Debug.cs
        Add(t, "vkCreateDebugUtilsMessengerEXT", (nint)new VkCreateDebugUtilsMessengerEXT(&Icd.vkCreateDebugUtilsMessengerEXT).Invoke);
        Add(t, "vkDestroyDebugUtilsMessengerEXT", (nint)new VkDestroyDebugUtilsMessengerEXT(&Icd.vkDestroyDebugUtilsMessengerEXT).Invoke);
        Add(t, "vkSubmitDebugUtilsMessageEXT", (nint)new VkSubmitDebugUtilsMessageEXT(&Icd.vkSubmitDebugUtilsMessageEXT).Invoke);
        Add(t, "vkSetDebugUtilsObjectNameEXT", (nint)new VkSetDebugUtilsObjectNameEXT(&Icd.vkSetDebugUtilsObjectNameEXT).Invoke);
        Add(t, "vkSetDebugUtilsObjectTagEXT", (nint)new VkSetDebugUtilsObjectTagEXT(&Icd.vkSetDebugUtilsObjectTagEXT).Invoke);
        Add(t, "vkQueueBeginDebugUtilsLabelEXT", (nint)new VkQueueBeginDebugUtilsLabelEXT(&Icd.vkQueueBeginDebugUtilsLabelEXT).Invoke);
        Add(t, "vkQueueEndDebugUtilsLabelEXT", (nint)new VkQueueEndDebugUtilsLabelEXT(&Icd.vkQueueEndDebugUtilsLabelEXT).Invoke);
        Add(t, "vkQueueInsertDebugUtilsLabelEXT", (nint)new VkQueueInsertDebugUtilsLabelEXT(&Icd.vkQueueInsertDebugUtilsLabelEXT).Invoke);
        Add(t, "vkCmdBeginDebugUtilsLabelEXT", (nint)new VkCmdBeginDebugUtilsLabelEXT(&Icd.vkCmdBeginDebugUtilsLabelEXT).Invoke);
        Add(t, "vkCmdEndDebugUtilsLabelEXT", (nint)new VkCmdEndDebugUtilsLabelEXT(&Icd.vkCmdEndDebugUtilsLabelEXT).Invoke);
        Add(t, "vkCmdInsertDebugUtilsLabelEXT", (nint)new VkCmdInsertDebugUtilsLabelEXT(&Icd.vkCmdInsertDebugUtilsLabelEXT).Invoke);
        // Icd.Descriptors.cs
        Add(t, "vkCreateDescriptorSetLayout", (nint)new VkCreateDescriptorSetLayout(&Icd.vkCreateDescriptorSetLayout).Invoke);
        Add(t, "vkDestroyDescriptorSetLayout", (nint)new VkDestroyDescriptorSetLayout(&Icd.vkDestroyDescriptorSetLayout).Invoke);
        Add(t, "vkGetDescriptorSetLayoutSupport", (nint)new VkGetDescriptorSetLayoutSupport(&Icd.vkGetDescriptorSetLayoutSupport).Invoke);
        Add(t, "vkGetDescriptorSetLayoutSupportKHR", (nint)new VkGetDescriptorSetLayoutSupportKHR(&Icd.vkGetDescriptorSetLayoutSupport).Invoke);
        Add(t, "vkCreateDescriptorPool", (nint)new VkCreateDescriptorPool(&Icd.vkCreateDescriptorPool).Invoke);
        Add(t, "vkDestroyDescriptorPool", (nint)new VkDestroyDescriptorPool(&Icd.vkDestroyDescriptorPool).Invoke);
        Add(t, "vkResetDescriptorPool", (nint)new VkResetDescriptorPool(&Icd.vkResetDescriptorPool).Invoke);
        Add(t, "vkAllocateDescriptorSets", (nint)new VkAllocateDescriptorSets(&Icd.vkAllocateDescriptorSets).Invoke);
        Add(t, "vkFreeDescriptorSets", (nint)new VkFreeDescriptorSets(&Icd.vkFreeDescriptorSets).Invoke);
        Add(t, "vkUpdateDescriptorSets", (nint)new VkUpdateDescriptorSets(&Icd.vkUpdateDescriptorSets).Invoke);
        // Icd.Device.cs
        Add(t, "vkCreateDevice", (nint)new VkCreateDevice(&Icd.vkCreateDevice).Invoke);
        Add(t, "vkDestroyDevice", (nint)new VkDestroyDevice(&Icd.vkDestroyDevice).Invoke);
        Add(t, "vkGetDeviceQueue", (nint)new VkGetDeviceQueue(&Icd.vkGetDeviceQueue).Invoke);
        Add(t, "vkGetDeviceQueue2", (nint)new VkGetDeviceQueue2(&Icd.vkGetDeviceQueue2).Invoke);
        Add(t, "vkDeviceWaitIdle", (nint)new VkDeviceWaitIdle(&Icd.vkDeviceWaitIdle).Invoke);
        // Icd.Images.cs
        Add(t, "vkCreateImage", (nint)new VkCreateImage(&Icd.vkCreateImage).Invoke);
        Add(t, "vkDestroyImage", (nint)new VkDestroyImage(&Icd.vkDestroyImage).Invoke);
        Add(t, "vxDestroyImages", (nint)new VxDestroyImages(&Icd.vxDestroyImages).Invoke);
        Add(t, "vkGetImageMemoryRequirements", (nint)new VkGetImageMemoryRequirements(&Icd.vkGetImageMemoryRequirements).Invoke);
        Add(t, "vkGetImageMemoryRequirements2", (nint)new VkGetImageMemoryRequirements2(&Icd.vkGetImageMemoryRequirements2).Invoke);
        Add(t, "vkGetImageMemoryRequirements2KHR", (nint)new VkGetImageMemoryRequirements2KHR(&Icd.vkGetImageMemoryRequirements2).Invoke);
        Add(t, "vkGetDeviceImageMemoryRequirements", (nint)new VkGetDeviceImageMemoryRequirements(&Icd.vkGetDeviceImageMemoryRequirements).Invoke);
        Add(t, "vkGetDeviceImageMemoryRequirementsKHR", (nint)new VkGetDeviceImageMemoryRequirementsKHR(&Icd.vkGetDeviceImageMemoryRequirements).Invoke);
        Add(t, "vkGetImageSparseMemoryRequirements", (nint)new VkGetImageSparseMemoryRequirements(&Icd.vkGetImageSparseMemoryRequirements).Invoke);
        Add(t, "vkBindImageMemory", (nint)new VkBindImageMemory(&Icd.vkBindImageMemory).Invoke);
        Add(t, "vkBindImageMemory2", (nint)new VkBindImageMemory2(&Icd.vkBindImageMemory2).Invoke);
        Add(t, "vkBindImageMemory2KHR", (nint)new VkBindImageMemory2KHR(&Icd.vkBindImageMemory2).Invoke);
        Add(t, "vkCreateImageView", (nint)new VkCreateImageView(&Icd.vkCreateImageView).Invoke);
        Add(t, "vkDestroyImageView", (nint)new VkDestroyImageView(&Icd.vkDestroyImageView).Invoke);
        Add(t, "vxDestroyImageViews", (nint)new VxDestroyImageViews(&Icd.vxDestroyImageViews).Invoke);
        Add(t, "vkCreateSampler", (nint)new VkCreateSampler(&Icd.vkCreateSampler).Invoke);
        Add(t, "vkDestroySampler", (nint)new VkDestroySampler(&Icd.vkDestroySampler).Invoke);
        // Icd.Instance.cs
        Add(t, "vkGetDeviceProcAddr", (nint)new VkGetDeviceProcAddr(&Icd.vkGetDeviceProcAddr).Invoke);
        Add(t, "vkDestroyInstance", (nint)new VkDestroyInstance(&Icd.vkDestroyInstance).Invoke);
        Add(t, "vkEnumeratePhysicalDevices", (nint)new VkEnumeratePhysicalDevices(&Icd.vkEnumeratePhysicalDevices).Invoke);
        Add(t, "vkEnumeratePhysicalDeviceGroups", (nint)new VkEnumeratePhysicalDeviceGroups(&Icd.vkEnumeratePhysicalDeviceGroups).Invoke);
        Add(t, "vkEnumeratePhysicalDeviceGroupsKHR", (nint)new VkEnumeratePhysicalDeviceGroupsKHR(&Icd.vkEnumeratePhysicalDeviceGroups).Invoke);
        Add(t, "vkGetPhysicalDeviceProperties", (nint)new VkGetPhysicalDeviceProperties(&Icd.vkGetPhysicalDeviceProperties).Invoke);
        Add(t, "vkGetPhysicalDeviceProperties2", (nint)new VkGetPhysicalDeviceProperties2(&Icd.vkGetPhysicalDeviceProperties2).Invoke);
        Add(t, "vkGetPhysicalDeviceProperties2KHR", (nint)new VkGetPhysicalDeviceProperties2KHR(&Icd.vkGetPhysicalDeviceProperties2).Invoke);
        Add(t, "vkGetPhysicalDeviceFeatures", (nint)new VkGetPhysicalDeviceFeatures(&Icd.vkGetPhysicalDeviceFeatures).Invoke);
        Add(t, "vkGetPhysicalDeviceFeatures2", (nint)new VkGetPhysicalDeviceFeatures2(&Icd.vkGetPhysicalDeviceFeatures2).Invoke);
        Add(t, "vkGetPhysicalDeviceFeatures2KHR", (nint)new VkGetPhysicalDeviceFeatures2KHR(&Icd.vkGetPhysicalDeviceFeatures2).Invoke);
        Add(t, "vkGetPhysicalDeviceQueueFamilyProperties", (nint)new VkGetPhysicalDeviceQueueFamilyProperties(&Icd.vkGetPhysicalDeviceQueueFamilyProperties).Invoke);
        Add(t, "vkGetPhysicalDeviceQueueFamilyProperties2", (nint)new VkGetPhysicalDeviceQueueFamilyProperties2(&Icd.vkGetPhysicalDeviceQueueFamilyProperties2).Invoke);
        Add(t, "vkGetPhysicalDeviceQueueFamilyProperties2KHR", (nint)new VkGetPhysicalDeviceQueueFamilyProperties2KHR(&Icd.vkGetPhysicalDeviceQueueFamilyProperties2).Invoke);
        Add(t, "vkGetPhysicalDeviceMemoryProperties", (nint)new VkGetPhysicalDeviceMemoryProperties(&Icd.vkGetPhysicalDeviceMemoryProperties).Invoke);
        Add(t, "vkGetPhysicalDeviceMemoryProperties2", (nint)new VkGetPhysicalDeviceMemoryProperties2(&Icd.vkGetPhysicalDeviceMemoryProperties2).Invoke);
        Add(t, "vkGetPhysicalDeviceMemoryProperties2KHR", (nint)new VkGetPhysicalDeviceMemoryProperties2KHR(&Icd.vkGetPhysicalDeviceMemoryProperties2).Invoke);
        Add(t, "vkGetPhysicalDeviceFormatProperties", (nint)new VkGetPhysicalDeviceFormatProperties(&Icd.vkGetPhysicalDeviceFormatProperties).Invoke);
        Add(t, "vkGetPhysicalDeviceFormatProperties2", (nint)new VkGetPhysicalDeviceFormatProperties2(&Icd.vkGetPhysicalDeviceFormatProperties2).Invoke);
        Add(t, "vkGetPhysicalDeviceFormatProperties2KHR", (nint)new VkGetPhysicalDeviceFormatProperties2KHR(&Icd.vkGetPhysicalDeviceFormatProperties2).Invoke);
        Add(t, "vkGetPhysicalDeviceImageFormatProperties", (nint)new VkGetPhysicalDeviceImageFormatProperties(&Icd.vkGetPhysicalDeviceImageFormatProperties).Invoke);
        Add(t, "vkGetPhysicalDeviceImageFormatProperties2", (nint)new VkGetPhysicalDeviceImageFormatProperties2(&Icd.vkGetPhysicalDeviceImageFormatProperties2).Invoke);
        Add(t, "vkGetPhysicalDeviceImageFormatProperties2KHR", (nint)new VkGetPhysicalDeviceImageFormatProperties2KHR(&Icd.vkGetPhysicalDeviceImageFormatProperties2).Invoke);
        Add(t, "vkGetPhysicalDeviceSparseImageFormatProperties", (nint)new VkGetPhysicalDeviceSparseImageFormatProperties(&Icd.vkGetPhysicalDeviceSparseImageFormatProperties).Invoke);
        Add(t, "vkGetPhysicalDeviceToolProperties", (nint)new VkGetPhysicalDeviceToolProperties(&Icd.vkGetPhysicalDeviceToolProperties).Invoke);
        Add(t, "vkGetPhysicalDeviceToolPropertiesEXT", (nint)new VkGetPhysicalDeviceToolPropertiesEXT(&Icd.vkGetPhysicalDeviceToolProperties).Invoke);
        Add(t, "vkEnumerateDeviceExtensionProperties", (nint)new VkEnumerateDeviceExtensionProperties(&Icd.vkEnumerateDeviceExtensionProperties).Invoke);
        Add(t, "vkEnumerateDeviceLayerProperties", (nint)new VkEnumerateDeviceLayerProperties(&Icd.vkEnumerateDeviceLayerProperties).Invoke);
        // Icd.Memory.cs
        Add(t, "vkAllocateMemory", (nint)new VkAllocateMemory(&Icd.vkAllocateMemory).Invoke);
        Add(t, "vkFreeMemory", (nint)new VkFreeMemory(&Icd.vkFreeMemory).Invoke);
        Add(t, "vkMapMemory", (nint)new VkMapMemory(&Icd.vkMapMemory).Invoke);
        Add(t, "vkMapMemory2", (nint)new VkMapMemory2(&Icd.vkMapMemory2).Invoke);
        Add(t, "vkMapMemory2KHR", (nint)new VkMapMemory2KHR(&Icd.vkMapMemory2).Invoke);
        Add(t, "vkUnmapMemory", (nint)new VkUnmapMemory(&Icd.vkUnmapMemory).Invoke);
        Add(t, "vkUnmapMemory2", (nint)new VkUnmapMemory2(&Icd.vkUnmapMemory2).Invoke);
        Add(t, "vkUnmapMemory2KHR", (nint)new VkUnmapMemory2KHR(&Icd.vkUnmapMemory2).Invoke);
        Add(t, "vkFlushMappedMemoryRanges", (nint)new VkFlushMappedMemoryRanges(&Icd.vkFlushMappedMemoryRanges).Invoke);
        Add(t, "vkInvalidateMappedMemoryRanges", (nint)new VkInvalidateMappedMemoryRanges(&Icd.vkInvalidateMappedMemoryRanges).Invoke);
        Add(t, "vkGetDeviceMemoryCommitment", (nint)new VkGetDeviceMemoryCommitment(&Icd.vkGetDeviceMemoryCommitment).Invoke);
        // Icd.Pipelines.cs
        Add(t, "vkCreatePipelineLayout", (nint)new VkCreatePipelineLayout(&Icd.vkCreatePipelineLayout).Invoke);
        Add(t, "vkDestroyPipelineLayout", (nint)new VkDestroyPipelineLayout(&Icd.vkDestroyPipelineLayout).Invoke);
        Add(t, "vkCreateGraphicsPipelines", (nint)new VkCreateGraphicsPipelines(&Icd.vkCreateGraphicsPipelines).Invoke);
        Add(t, "vkCreateComputePipelines", (nint)new VkCreateComputePipelines(&Icd.vkCreateComputePipelines).Invoke);
        Add(t, "vkDestroyPipeline", (nint)new VkDestroyPipeline(&Icd.vkDestroyPipeline).Invoke);
        // Icd.Queue.cs
        Add(t, "vkQueueSubmit", (nint)new VkQueueSubmit(&Icd.vkQueueSubmit).Invoke);
        Add(t, "vkQueueSubmit2", (nint)new VkQueueSubmit2(&Icd.vkQueueSubmit2).Invoke);
        Add(t, "vkQueueSubmit2KHR", (nint)new VkQueueSubmit2KHR(&Icd.vkQueueSubmit2).Invoke);
        Add(t, "vkQueueWaitIdle", (nint)new VkQueueWaitIdle(&Icd.vkQueueWaitIdle).Invoke);
        Add(t, "vkQueueBindSparse", (nint)new VkQueueBindSparse(&Icd.vkQueueBindSparse).Invoke);
        Add(t, "vkCreateFence", (nint)new VkCreateFence(&Icd.vkCreateFence).Invoke);
        Add(t, "vkDestroyFence", (nint)new VkDestroyFence(&Icd.vkDestroyFence).Invoke);
        Add(t, "vkResetFences", (nint)new VkResetFences(&Icd.vkResetFences).Invoke);
        Add(t, "vkGetFenceStatus", (nint)new VkGetFenceStatus(&Icd.vkGetFenceStatus).Invoke);
        Add(t, "vkWaitForFences", (nint)new VkWaitForFences(&Icd.vkWaitForFences).Invoke);
        Add(t, "vkCreateSemaphore", (nint)new VkCreateSemaphore(&Icd.vkCreateSemaphore).Invoke);
        Add(t, "vkDestroySemaphore", (nint)new VkDestroySemaphore(&Icd.vkDestroySemaphore).Invoke);
        Add(t, "vkCreateEvent", (nint)new VkCreateEvent(&Icd.vkCreateEvent).Invoke);
        Add(t, "vkDestroyEvent", (nint)new VkDestroyEvent(&Icd.vkDestroyEvent).Invoke);
        Add(t, "vkGetEventStatus", (nint)new VkGetEventStatus(&Icd.vkGetEventStatus).Invoke);
        Add(t, "vkSetEvent", (nint)new VkSetEvent(&Icd.vkSetEvent).Invoke);
        Add(t, "vkResetEvent", (nint)new VkResetEvent(&Icd.vkResetEvent).Invoke);
        // Icd.RenderPasses.cs
        Add(t, "vkCmdBeginRendering", (nint)new VkCmdBeginRendering(&Icd.vkCmdBeginRendering).Invoke);
        Add(t, "vkCmdBeginRenderingKHR", (nint)new VkCmdBeginRenderingKHR(&Icd.vkCmdBeginRendering).Invoke);
        Add(t, "vkCmdEndRendering", (nint)new VkCmdEndRendering(&Icd.vkCmdEndRendering).Invoke);
        Add(t, "vkCmdEndRenderingKHR", (nint)new VkCmdEndRenderingKHR(&Icd.vkCmdEndRendering).Invoke);
        Add(t, "vkCreateRenderPass", (nint)new VkCreateRenderPass(&Icd.vkCreateRenderPass).Invoke);
        Add(t, "vkCreateRenderPass2", (nint)new VkCreateRenderPass2(&Icd.vkCreateRenderPass2).Invoke);
        Add(t, "vkCreateRenderPass2KHR", (nint)new VkCreateRenderPass2KHR(&Icd.vkCreateRenderPass2).Invoke);
        Add(t, "vkDestroyRenderPass", (nint)new VkDestroyRenderPass(&Icd.vkDestroyRenderPass).Invoke);
        Add(t, "vkGetRenderAreaGranularity", (nint)new VkGetRenderAreaGranularity(&Icd.vkGetRenderAreaGranularity).Invoke);
        Add(t, "vkCreateFramebuffer", (nint)new VkCreateFramebuffer(&Icd.vkCreateFramebuffer).Invoke);
        Add(t, "vkDestroyFramebuffer", (nint)new VkDestroyFramebuffer(&Icd.vkDestroyFramebuffer).Invoke);
        Add(t, "vkCmdBeginRenderPass", (nint)new VkCmdBeginRenderPass(&Icd.vkCmdBeginRenderPass).Invoke);
        Add(t, "vkCmdBeginRenderPass2", (nint)new VkCmdBeginRenderPass2(&Icd.vkCmdBeginRenderPass2).Invoke);
        Add(t, "vkCmdBeginRenderPass2KHR", (nint)new VkCmdBeginRenderPass2KHR(&Icd.vkCmdBeginRenderPass2).Invoke);
        Add(t, "vkCmdNextSubpass", (nint)new VkCmdNextSubpass(&Icd.vkCmdNextSubpass).Invoke);
        Add(t, "vkCmdNextSubpass2", (nint)new VkCmdNextSubpass2(&Icd.vkCmdNextSubpass2).Invoke);
        Add(t, "vkCmdNextSubpass2KHR", (nint)new VkCmdNextSubpass2KHR(&Icd.vkCmdNextSubpass2).Invoke);
        Add(t, "vkCmdEndRenderPass", (nint)new VkCmdEndRenderPass(&Icd.vkCmdEndRenderPass).Invoke);
        Add(t, "vkCmdEndRenderPass2", (nint)new VkCmdEndRenderPass2(&Icd.vkCmdEndRenderPass2).Invoke);
        Add(t, "vkCmdEndRenderPass2KHR", (nint)new VkCmdEndRenderPass2KHR(&Icd.vkCmdEndRenderPass2).Invoke);
        // Icd.Shaders.cs
        Add(t, "vkCreateShaderModule", (nint)new VkCreateShaderModule(&Icd.vkCreateShaderModule).Invoke);
        Add(t, "vkDestroyShaderModule", (nint)new VkDestroyShaderModule(&Icd.vkDestroyShaderModule).Invoke);
        Add(t, "vkCreatePipelineCache", (nint)new VkCreatePipelineCache(&Icd.vkCreatePipelineCache).Invoke);
        Add(t, "vkDestroyPipelineCache", (nint)new VkDestroyPipelineCache(&Icd.vkDestroyPipelineCache).Invoke);
        Add(t, "vkGetPipelineCacheData", (nint)new VkGetPipelineCacheData(&Icd.vkGetPipelineCacheData).Invoke);
        Add(t, "vkMergePipelineCaches", (nint)new VkMergePipelineCaches(&Icd.vkMergePipelineCaches).Invoke);
        // Icd.Swapchain.cs
        Add(t, "vkDestroySurfaceKHR", (nint)new VkDestroySurfaceKHR(&Icd.vkDestroySurfaceKHR).Invoke);
        Add(t, "vkGetPhysicalDeviceSurfaceSupportKHR", (nint)new VkGetPhysicalDeviceSurfaceSupportKHR(&Icd.vkGetPhysicalDeviceSurfaceSupportKHR).Invoke);
        Add(t, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR", (nint)new VkGetPhysicalDeviceSurfaceCapabilitiesKHR(&Icd.vkGetPhysicalDeviceSurfaceCapabilitiesKHR).Invoke);
        Add(t, "vkGetPhysicalDeviceSurfaceFormatsKHR", (nint)new VkGetPhysicalDeviceSurfaceFormatsKHR(&Icd.vkGetPhysicalDeviceSurfaceFormatsKHR).Invoke);
        Add(t, "vkGetPhysicalDeviceSurfacePresentModesKHR", (nint)new VkGetPhysicalDeviceSurfacePresentModesKHR(&Icd.vkGetPhysicalDeviceSurfacePresentModesKHR).Invoke);
        Add(t, "vkCreateSwapchainKHR", (nint)new VkCreateSwapchainKHR(&Icd.vkCreateSwapchainKHR).Invoke);
        Add(t, "vkDestroySwapchainKHR", (nint)new VkDestroySwapchainKHR(&Icd.vkDestroySwapchainKHR).Invoke);
        Add(t, "vkGetSwapchainImagesKHR", (nint)new VkGetSwapchainImagesKHR(&Icd.vkGetSwapchainImagesKHR).Invoke);
        Add(t, "vkAcquireNextImageKHR", (nint)new VkAcquireNextImageKHR(&Icd.vkAcquireNextImageKHR).Invoke);
        Add(t, "vkQueuePresentKHR", (nint)new VkQueuePresentKHR(&Icd.vkQueuePresentKHR).Invoke);
        Add(t, "vkGetSwapchainStatusKHR", (nint)new VkGetSwapchainStatusKHR(&Icd.vkGetSwapchainStatusKHR).Invoke);
        // Icd.Transfers.cs
        Add(t, "vkCmdCopyBuffer", (nint)new VkCmdCopyBuffer(&Icd.vkCmdCopyBuffer).Invoke);
        Add(t, "vkCmdCopyBuffer2", (nint)new VkCmdCopyBuffer2(&Icd.vkCmdCopyBuffer2).Invoke);
        Add(t, "vkCmdCopyBuffer2KHR", (nint)new VkCmdCopyBuffer2KHR(&Icd.vkCmdCopyBuffer2).Invoke);
        Add(t, "vkCmdCopyBufferToImage", (nint)new VkCmdCopyBufferToImage(&Icd.vkCmdCopyBufferToImage).Invoke);
        Add(t, "vkCmdCopyBufferToImage2", (nint)new VkCmdCopyBufferToImage2(&Icd.vkCmdCopyBufferToImage2).Invoke);
        Add(t, "vkCmdCopyBufferToImage2KHR", (nint)new VkCmdCopyBufferToImage2KHR(&Icd.vkCmdCopyBufferToImage2).Invoke);
        Add(t, "vkCmdCopyImageToBuffer", (nint)new VkCmdCopyImageToBuffer(&Icd.vkCmdCopyImageToBuffer).Invoke);
        Add(t, "vkCmdCopyImageToBuffer2", (nint)new VkCmdCopyImageToBuffer2(&Icd.vkCmdCopyImageToBuffer2).Invoke);
        Add(t, "vkCmdCopyImageToBuffer2KHR", (nint)new VkCmdCopyImageToBuffer2KHR(&Icd.vkCmdCopyImageToBuffer2).Invoke);
        Add(t, "vkCmdCopyImage", (nint)new VkCmdCopyImage(&Icd.vkCmdCopyImage).Invoke);
        Add(t, "vkCmdCopyImage2", (nint)new VkCmdCopyImage2(&Icd.vkCmdCopyImage2).Invoke);
        Add(t, "vkCmdCopyImage2KHR", (nint)new VkCmdCopyImage2KHR(&Icd.vkCmdCopyImage2).Invoke);
        Add(t, "vkCmdUpdateBuffer", (nint)new VkCmdUpdateBuffer(&Icd.vkCmdUpdateBuffer).Invoke);
        Add(t, "vkCmdFillBuffer", (nint)new VkCmdFillBuffer(&Icd.vkCmdFillBuffer).Invoke);
    }
}
