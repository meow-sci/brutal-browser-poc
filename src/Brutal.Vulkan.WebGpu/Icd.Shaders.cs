using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

internal static unsafe partial class Icd
{
    /// <summary>
    /// Looks the SPIR-V up in the shader manifest by the SHA-256 of its exact bytes and creates the GPUShaderModule from the
    /// pre-translated WGSL. A miss fails with VK_ERROR_INVALID_SHADER_NV (there is no runtime SPIR-V → WGSL translation).
    /// </summary>
    public static VkResult vkCreateShaderModule(VkDevice device, VkShaderModuleCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator,
        VkShaderModule* pShaderModule)
    {
        const string fn = "vkCreateShaderModule";
        var code = new ReadOnlySpan<byte>(pCreateInfo->Code, (int)pCreateInfo->CodeSize.Value);
        var hash = ShaderHash.Hex(code);
        if (!ShaderManifest.TryGet(hash, out var entry))
        {
            IcdLog.Error(ShaderManifest.Count == 0
                ? $"{fn}: no shader manifest is loaded (call WebIcd.LoadShaderManifest before creating shader modules); SPIR-V sha256 {hash}, {code.Length} bytes"
                : $"{fn}: SPIR-V module sha256 {hash} ({code.Length} bytes) is not in the shader manifest ({ShaderManifest.Count} entries); rebuild the shaders with tools/build-shaders.mjs");
            *pShaderModule = default;
            return VkResult.ErrorInvalidShaderNV;
        }
        if (entry.Push == PushMode.Unsupported)
        {
            IcdLog.Error($"{fn}: shader {entry.Name} has a push.mode other than none/uniform/storage; this ICD serves push constants from a uniform or storage buffer at group 3 (rebuild the manifest with tools/build-shaders.mjs)");
            return VkResult.ErrorInvalidShaderNV;
        }
        var gpu = WebIcd.Bridge.CreateShaderModule(entry.Wgsl, entry.Name);
        if (gpu == 0)
        {
            IcdLog.Error($"{fn}: createShaderModule failed for {entry.Name}");
            return VkResult.ErrorInvalidShaderNV;
        }
        var module = new ShaderModuleObject { Entry = entry, Gpu = gpu, Name = entry.Name };
        HandleTable.Add(module);
        *pShaderModule = new VkShaderModule(module.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyShaderModule(VkDevice device, VkShaderModule shaderModule, VkAllocationCallbacks* pAllocator)
    {
        var obj = HandleTable.Get<ShaderModuleObject>(shaderModule.VkHandle);
        if (obj is null)
            return;
        WebIcd.Bridge.Release(obj.Gpu); // pipelines created from it keep their own reference
        HandleTable.Remove(obj.Handle);
    }

    // ---- pipeline caches (no-ops: WebGPU caches compiled pipelines itself) --------------------------------------------

    public static VkResult vkCreatePipelineCache(VkDevice device, VkPipelineCacheCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator,
        VkPipelineCache* pPipelineCache)
    {
        var cache = new PipelineCacheObject();
        HandleTable.Add(cache);
        *pPipelineCache = new VkPipelineCache(cache.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyPipelineCache(VkDevice device, VkPipelineCache pipelineCache, VkAllocationCallbacks* pAllocator)
    {
        if (HandleTable.Get<PipelineCacheObject>(pipelineCache.VkHandle) is { } obj)
            HandleTable.Remove(obj.Handle);
    }

    /// <summary>Returns a valid 32-byte VkPipelineCacheHeaderVersionOne with no payload.</summary>
    public static VkResult vkGetPipelineCacheData(VkDevice device, VkPipelineCache pipelineCache, nuint* pDataSize, void* pData)
    {
        const int headerSize = 32;
        if (pData == null)
        {
            *pDataSize = headerSize;
            return VkResult.Success;
        }
        if (*pDataSize < headerSize)
        {
            *pDataSize = 0;
            return VkResult.Incomplete;
        }
        var h = (int*)pData;
        h[0] = headerSize;
        h[1] = 1; // VK_PIPELINE_CACHE_HEADER_VERSION_ONE
        h[2] = Profile.VendorId;
        h[3] = 0x0E06;
        var uuid = "BRUTALwebICD2026"u8;
        for (var i = 0; i < 16; i++)
            ((byte*)pData)[16 + i] = uuid[i];
        *pDataSize = headerSize;
        return VkResult.Success;
    }

    public static VkResult vkMergePipelineCaches(VkDevice device, VkPipelineCache dstCache, int srcCacheCount, VkPipelineCache* pSrcCaches) =>
        VkResult.Success;
}
