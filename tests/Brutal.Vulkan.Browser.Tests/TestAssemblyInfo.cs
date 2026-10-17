// Brutal.VulkanApi.Vulkan has a static constructor and the fake ICD / AppContext key are process-wide: run tests serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
