using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace HelloMesh.Rendering;

/// <summary>Layout transitions recorded with <c>vkCmdPipelineBarrier2</c> (synchronization2).</summary>
internal static class Barriers
{
    /// <summary>Transitions the single mip level and array layer of <paramref name="image"/>.</summary>
    public static unsafe void Image(
        CommandBuffer cmd,
        VkImage image,
        VkImageAspectFlags aspect,
        VkImageLayout oldLayout,
        VkImageLayout newLayout,
        VkPipelineStageFlags2 srcStage,
        VkAccessFlags2 srcAccess,
        VkPipelineStageFlags2 dstStage,
        VkAccessFlags2 dstAccess)
    {
        var barrier = new VkImageMemoryBarrier2
        {
            SrcStageMask = srcStage,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStage,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
            DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
            Image = image,
            SubresourceRange = Subresources.Single(aspect),
        };
        var dependency = new VkDependencyInfo { ImageMemoryBarrierCount = 1, ImageMemoryBarriers = &barrier };
        cmd.PipelineBarrier2(dependency);
    }
}
