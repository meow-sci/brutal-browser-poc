using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Transfer commands. Buffer→image copies whose source is host-visible become <c>queue.writeTexture</c> from the shadow at
/// replay (no 256-byte <c>bytesPerRow</c> rule; the replayer submits the commands recorded before it first, so ordering is
/// kept); other buffer↔image copies need 256-byte rows. Copies into host-visible buffers are read back into the shadow after
/// the submit (the fence signals when the data has landed, a frame or more later).
/// </summary>
internal static unsafe partial class Icd
{
    public static void vkCmdCopyBuffer(VkCommandBuffer commandBuffer, VkBuffer srcBuffer, VkBuffer dstBuffer, int regionCount, VkBufferCopy* pRegions)
    {
        const string fn = "vkCmdCopyBuffer";
        var cb = Recording(commandBuffer, fn);
        var src = HandleTable.Lookup<BufferObject>(srcBuffer.VkHandle, fn);
        var dst = HandleTable.Lookup<BufferObject>(dstBuffer.VkHandle, fn);
        if (cb is null || src is null || dst is null)
            return;
        for (var i = 0; i < regionCount; i++)
            CopyBufferRegion(cb, src, dst, pRegions[i].SrcOffset.Value, pRegions[i].DstOffset.Value, pRegions[i].Size.Value, fn);
    }

    public static void vkCmdCopyBuffer2(VkCommandBuffer commandBuffer, VkCopyBufferInfo2* pCopyBufferInfo)
    {
        const string fn = "vkCmdCopyBuffer2";
        var cb = Recording(commandBuffer, fn);
        var src = HandleTable.Lookup<BufferObject>(pCopyBufferInfo->SrcBuffer.VkHandle, fn);
        var dst = HandleTable.Lookup<BufferObject>(pCopyBufferInfo->DstBuffer.VkHandle, fn);
        if (cb is null || src is null || dst is null)
            return;
        for (var i = 0; i < pCopyBufferInfo->RegionCount; i++)
        {
            ref var r = ref pCopyBufferInfo->Regions[i];
            CopyBufferRegion(cb, src, dst, r.SrcOffset.Value, r.DstOffset.Value, r.Size.Value, fn);
        }
    }

    private static void CopyBufferRegion(CommandBufferObject cb, BufferObject src, BufferObject dst, ulong srcOffset, ulong dstOffset, ulong size, string fn)
    {
        // a copy reaching the end of both buffers may be padded to 4 bytes (GPU buffers are rounded up to 4); anywhere else the
        // padding would overwrite neighbouring bytes, so unaligned sizes are rejected
        var padded = size;
        if ((size & 3) != 0 && srcOffset + size == src.Size && dstOffset + size == dst.Size)
            padded = (size + 3) & ~3UL;
        if (!WebProfileLint.CheckAligned4(fn, "srcOffset", srcOffset) || !WebProfileLint.CheckAligned4(fn, "dstOffset", dstOffset)
            || !WebProfileLint.CheckAligned4(fn, "size", padded))
            return;
        cb.Stream.Op5(Op.CopyBuffer, (uint)src.Gpu, (uint)srcOffset, (uint)dst.Gpu, (uint)dstOffset, (uint)padded);
        RequestReadback(cb, dst, dstOffset, padded);
    }

    /// <summary>GPU writes into a host-visible buffer: copy the range back into the shadow after the submit.</summary>
    internal static void RequestReadback(CommandBufferObject cb, BufferObject dst, ulong offset, ulong size)
    {
        if (!dst.HostVisible || size == 0)
            return;
        foreach (var r in cb.Readbacks)
            if (r.Buffer == dst && r.Offset <= offset && offset + size <= r.Offset + r.Size)
                return;
        cb.Readbacks.Add(new ReadbackRequest { Buffer = dst, Offset = offset, Size = size });
    }

    private struct TextureRegion
    {
        public uint MipLevel, X, Y, Z, Aspect, Width, Height, DepthOrLayers;
    }

    /// <summary>
    /// The texture side of a copy. Compressed extents are rounded up to whole blocks (Vulkan allows edge-reaching texel extents,
    /// WebGPU wants the physical, block-aligned size). Buffer copies of the depth aspect of depth24plus formats do not exist in
    /// WebGPU; texture-to-texture copies of depth/stencil formats use all aspects.
    /// </summary>
    private static bool ResolveRegion(ImageObject image, in VkImageSubresourceLayers sub, in VkOffset3D offset, in VkExtent3D extent, string fn,
        bool bufferCopy, out TextureRegion region)
    {
        region = default;
        var layers = sub.LayerCount == Mappings.Remaining ? image.ArrayLayers - sub.BaseArrayLayer : sub.LayerCount;
        var is3D = image.Type == VkImageType._3D;
        region.MipLevel = (uint)sub.MipLevel;
        region.X = (uint)offset.X;
        region.Y = (uint)offset.Y;
        region.Z = (uint)(is3D ? offset.Z : sub.BaseArrayLayer);
        region.Width = (uint)extent.Width;
        region.Height = (uint)Math.Max(1, extent.Height);
        region.DepthOrLayers = (uint)(is3D ? Math.Max(1, extent.Depth) : Math.Max(1, layers));
        if (Formats.IsCompressed(image.Format))
        {
            var (_, bw, bh) = Formats.BlockInfo(image.Format);
            region.Width = (region.Width + (uint)bw - 1) / (uint)bw * (uint)bw;
            region.Height = (region.Height + (uint)bh - 1) / (uint)bh * (uint)bh;
        }
        var depth = (sub.AspectMask & VkImageAspectFlags.DepthBit) != 0;
        var stencil = (sub.AspectMask & VkImageAspectFlags.StencilBit) != 0;
        region.Aspect = depth && stencil ? StreamCodes.AspectAll
            : depth && Formats.HasStencil(image.Format) ? StreamCodes.AspectDepthOnly
            : stencil && Formats.HasDepth(image.Format) ? StreamCodes.AspectStencilOnly
            : StreamCodes.AspectAll;
        if (!bufferCopy && Formats.IsDepthOrStencil(image.Format))
            region.Aspect = StreamCodes.AspectAll; // WebGPU copies depth/stencil textures whole
        if (bufferCopy && depth && image.Format is VkFormat.D24UNormS8UInt or VkFormat.X8D24UNormPack32)
        {
            IcdLog.Unsupported(fn, $"buffer copies of the depth aspect of {image.Format} (WebGPU depth24plus is not copyable to or from buffers)");
            return false;
        }
        if (bufferCopy && depth && stencil)
        {
            IcdLog.Unsupported(fn, "buffer copies of both depth and stencil aspects at once (copy one aspect per region)");
            return false;
        }
        return true;
    }

    /// <summary>Bytes per row / rows per image of a buffer side of a copy, and the bytes the copy touches.</summary>
    private static (uint BytesPerRow, uint RowsPerImage, ulong Required, uint HeightBlocks) BufferLayout(ImageObject image, uint aspect, int rowLength, int imageHeight,
        in TextureRegion region)
    {
        var (blockBytes, bw, bh) = Formats.BlockInfo(image.Format);
        if (aspect == StreamCodes.AspectStencilOnly)
            blockBytes = 1;
        var rowTexels = rowLength == 0 ? (int)region.Width : rowLength;
        var heightTexels = imageHeight == 0 ? (int)region.Height : imageHeight;
        var bytesPerRow = (uint)((rowTexels + bw - 1) / bw * blockBytes);
        var rowsPerImage = (uint)((heightTexels + bh - 1) / bh);
        var heightBlocks = (region.Height + (uint)bh - 1) / (uint)bh;
        var widthBytes = (region.Width + (uint)bw - 1) / (uint)bw * (uint)blockBytes;
        var required = (ulong)bytesPerRow * (rowsPerImage * (region.DepthOrLayers - 1) + heightBlocks - 1) + widthBytes;
        return (bytesPerRow, rowsPerImage, required, heightBlocks);
    }

    private static void WriteRegion(uint* p, in TextureRegion r)
    {
        p[0] = r.MipLevel; p[1] = r.X; p[2] = r.Y; p[3] = r.Z; p[4] = r.Aspect;
    }

    public static void vkCmdCopyBufferToImage(VkCommandBuffer commandBuffer, VkBuffer srcBuffer, VkImage dstImage, VkImageLayout dstImageLayout,
        int regionCount, VkBufferImageCopy* pRegions)
    {
        const string fn = "vkCmdCopyBufferToImage";
        var cb = Recording(commandBuffer, fn);
        var src = HandleTable.Lookup<BufferObject>(srcBuffer.VkHandle, fn);
        var dst = HandleTable.Lookup<ImageObject>(dstImage.VkHandle, fn);
        if (cb is null || src is null || dst is null)
            return;
        for (var i = 0; i < regionCount; i++)
        {
            ref var r = ref pRegions[i];
            CopyBufferToImageRegion(cb, src, dst, r.BufferOffset.Value, r.BufferRowLength, r.BufferImageHeight, r.ImageSubresource, r.ImageOffset, r.ImageExtent, fn);
        }
    }

    public static void vkCmdCopyBufferToImage2(VkCommandBuffer commandBuffer, VkCopyBufferToImageInfo2* pInfo)
    {
        const string fn = "vkCmdCopyBufferToImage2";
        var cb = Recording(commandBuffer, fn);
        var src = HandleTable.Lookup<BufferObject>(pInfo->SrcBuffer.VkHandle, fn);
        var dst = HandleTable.Lookup<ImageObject>(pInfo->DstImage.VkHandle, fn);
        if (cb is null || src is null || dst is null)
            return;
        for (var i = 0; i < pInfo->RegionCount; i++)
        {
            ref var r = ref pInfo->Regions[i];
            CopyBufferToImageRegion(cb, src, dst, r.BufferOffset.Value, r.BufferRowLength, r.BufferImageHeight, r.ImageSubresource, r.ImageOffset, r.ImageExtent, fn);
        }
    }

    private static void CopyBufferToImageRegion(CommandBufferObject cb, BufferObject src, ImageObject dst, ulong bufferOffset, int rowLength, int imageHeight,
        in VkImageSubresourceLayers sub, in VkOffset3D offset, in VkExtent3D extent, string fn)
    {
        if (!ResolveRegion(dst, sub, offset, extent, fn, bufferCopy: true, out var region))
            return;
        var (bytesPerRow, rowsPerImage, required, heightBlocks) = BufferLayout(dst, region.Aspect, rowLength, imageHeight, region);
        if (src.HostVisible)
        {
            // queue.writeTexture straight from the shadow: any bytesPerRow works
            if (src.MemoryOffset + bufferOffset + required > src.Memory!.Size)
            {
                IcdLog.Error($"{fn}: the copy reads {required} bytes at offset {bufferOffset} of {src}, past its memory");
                return;
            }
            var p = cb.Stream.Record(Op.WriteTexture, 13);
            p[0] = (uint)(nint)src.ShadowAddress(bufferOffset);
            p[1] = (uint)required;
            p[2] = bytesPerRow;
            p[3] = rowsPerImage;
            p[4] = (uint)dst.CopyHandle;
            WriteRegion(p + 5, region);
            p[10] = region.Width; p[11] = region.Height; p[12] = region.DepthOrLayers;
            return;
        }
        if ((heightBlocks > 1 || region.DepthOrLayers > 1) && bytesPerRow % 256 != 0)
        {
            WebProfileLint.Fail(fn, "buffer-to-image copies from device-local buffers use 256-byte rows",
                $"bytesPerRow {bytesPerRow} (stage the data in host-visible memory, or pad rows to 256 bytes)");
            return;
        }
        var q = cb.Stream.Record(Op.CopyBufferToTexture, 13);
        q[0] = (uint)src.Gpu;
        q[1] = (uint)bufferOffset;
        q[2] = bytesPerRow;
        q[3] = rowsPerImage;
        q[4] = (uint)dst.CopyHandle;
        WriteRegion(q + 5, region);
        q[10] = region.Width; q[11] = region.Height; q[12] = region.DepthOrLayers;
    }

    public static void vkCmdCopyImageToBuffer(VkCommandBuffer commandBuffer, VkImage srcImage, VkImageLayout srcImageLayout, VkBuffer dstBuffer,
        int regionCount, VkBufferImageCopy* pRegions)
    {
        const string fn = "vkCmdCopyImageToBuffer";
        var cb = Recording(commandBuffer, fn);
        var src = HandleTable.Lookup<ImageObject>(srcImage.VkHandle, fn);
        var dst = HandleTable.Lookup<BufferObject>(dstBuffer.VkHandle, fn);
        if (cb is null || src is null || dst is null)
            return;
        for (var i = 0; i < regionCount; i++)
        {
            ref var r = ref pRegions[i];
            CopyImageToBufferRegion(cb, src, dst, r.BufferOffset.Value, r.BufferRowLength, r.BufferImageHeight, r.ImageSubresource, r.ImageOffset, r.ImageExtent, fn);
        }
    }

    public static void vkCmdCopyImageToBuffer2(VkCommandBuffer commandBuffer, VkCopyImageToBufferInfo2* pInfo)
    {
        const string fn = "vkCmdCopyImageToBuffer2";
        var cb = Recording(commandBuffer, fn);
        var src = HandleTable.Lookup<ImageObject>(pInfo->SrcImage.VkHandle, fn);
        var dst = HandleTable.Lookup<BufferObject>(pInfo->DstBuffer.VkHandle, fn);
        if (cb is null || src is null || dst is null)
            return;
        for (var i = 0; i < pInfo->RegionCount; i++)
        {
            ref var r = ref pInfo->Regions[i];
            CopyImageToBufferRegion(cb, src, dst, r.BufferOffset.Value, r.BufferRowLength, r.BufferImageHeight, r.ImageSubresource, r.ImageOffset, r.ImageExtent, fn);
        }
    }

    private static void CopyImageToBufferRegion(CommandBufferObject cb, ImageObject src, BufferObject dst, ulong bufferOffset, int rowLength, int imageHeight,
        in VkImageSubresourceLayers sub, in VkOffset3D offset, in VkExtent3D extent, string fn)
    {
        if (!ResolveRegion(src, sub, offset, extent, fn, bufferCopy: true, out var region))
            return;
        var (bytesPerRow, rowsPerImage, required, heightBlocks) = BufferLayout(src, region.Aspect, rowLength, imageHeight, region);
        if ((heightBlocks > 1 || region.DepthOrLayers > 1) && bytesPerRow % 256 != 0)
        {
            WebProfileLint.Fail(fn, "image-to-buffer copies use 256-byte rows", $"bytesPerRow {bytesPerRow} (set bufferRowLength to a multiple of 256 bytes)");
            return;
        }
        var p = cb.Stream.Record(Op.CopyTextureToBuffer, 13);
        p[0] = (uint)src.CopyHandle;
        WriteRegion(p + 1, region);
        p[6] = (uint)dst.Gpu;
        p[7] = (uint)bufferOffset;
        p[8] = bytesPerRow;
        p[9] = rowsPerImage;
        p[10] = region.Width; p[11] = region.Height; p[12] = region.DepthOrLayers;
        RequestReadback(cb, dst, bufferOffset & ~3UL, Math.Min((required + (bufferOffset & 3) + 3) & ~3UL, dst.GpuSize - (bufferOffset & ~3UL)));
    }

    public static void vkCmdCopyImage(VkCommandBuffer commandBuffer, VkImage srcImage, VkImageLayout srcImageLayout, VkImage dstImage,
        VkImageLayout dstImageLayout, int regionCount, VkImageCopy* pRegions)
    {
        const string fn = "vkCmdCopyImage";
        var cb = Recording(commandBuffer, fn);
        var src = HandleTable.Lookup<ImageObject>(srcImage.VkHandle, fn);
        var dst = HandleTable.Lookup<ImageObject>(dstImage.VkHandle, fn);
        if (cb is null || src is null || dst is null)
            return;
        for (var i = 0; i < regionCount; i++)
        {
            ref var r = ref pRegions[i];
            CopyImageRegion(cb, src, dst, r.SrcSubresource, r.SrcOffset, r.DstSubresource, r.DstOffset, r.Extent, fn);
        }
    }

    public static void vkCmdCopyImage2(VkCommandBuffer commandBuffer, VkCopyImageInfo2* pInfo)
    {
        const string fn = "vkCmdCopyImage2";
        var cb = Recording(commandBuffer, fn);
        var src = HandleTable.Lookup<ImageObject>(pInfo->SrcImage.VkHandle, fn);
        var dst = HandleTable.Lookup<ImageObject>(pInfo->DstImage.VkHandle, fn);
        if (cb is null || src is null || dst is null)
            return;
        for (var i = 0; i < pInfo->RegionCount; i++)
        {
            ref var r = ref pInfo->Regions[i];
            CopyImageRegion(cb, src, dst, r.SrcSubresource, r.SrcOffset, r.DstSubresource, r.DstOffset, r.Extent, fn);
        }
    }

    private static void CopyImageRegion(CommandBufferObject cb, ImageObject src, ImageObject dst, in VkImageSubresourceLayers srcSub, in VkOffset3D srcOffset,
        in VkImageSubresourceLayers dstSub, in VkOffset3D dstOffset, in VkExtent3D extent, string fn)
    {
        if (src.Format != dst.Format && Formats.SrgbTwin(src.Format) != dst.Format)
        {
            IcdLog.Unsupported(fn, $"copies between {src.Format} and {dst.Format} (formats must match up to sRGB)");
            return;
        }
        if (!ResolveRegion(src, srcSub, srcOffset, extent, fn, bufferCopy: false, out var s)
            || !ResolveRegion(dst, dstSub, dstOffset, extent, fn, bufferCopy: false, out var d))
            return;
        var p = cb.Stream.Record(Op.CopyTextureToTexture, 15);
        p[0] = (uint)src.CopyHandle;
        WriteRegion(p + 1, s);
        p[6] = (uint)dst.CopyHandle;
        WriteRegion(p + 7, d);
        p[12] = s.Width; p[13] = s.Height; p[14] = Math.Min(s.DepthOrLayers, d.DepthOrLayers);
    }

    public static void vkCmdUpdateBuffer(VkCommandBuffer commandBuffer, VkBuffer dstBuffer, ByteSize64 dstOffset, ByteSize64 dataSize, void* pData)
    {
        const string fn = "vkCmdUpdateBuffer";
        var cb = Recording(commandBuffer, fn);
        var dst = HandleTable.Lookup<BufferObject>(dstBuffer.VkHandle, fn);
        if (cb is null || dst is null)
            return;
        var size = dataSize.Value;
        if (!WebProfileLint.CheckAligned4(fn, "dstOffset", dstOffset.Value) || !WebProfileLint.CheckAligned4(fn, "dataSize", size))
            return;
        if (size > 65536)
        {
            IcdLog.Error($"{fn}: dataSize {size} exceeds 65536");
            return;
        }
        var p = cb.Stream.RecordWithData(Op.UpdateBuffer, 3, pData, (int)size);
        p[0] = (uint)dst.Gpu;
        p[1] = (uint)dstOffset.Value;
        p[2] = (uint)size;
        RequestReadback(cb, dst, dstOffset.Value, size);
    }

    private static readonly uint[] s_fillPattern = new uint[16384];

    public static void vkCmdFillBuffer(VkCommandBuffer commandBuffer, VkBuffer dstBuffer, ByteSize64 dstOffset, ByteSize64 size, int data)
    {
        const string fn = "vkCmdFillBuffer";
        var cb = Recording(commandBuffer, fn);
        var dst = HandleTable.Lookup<BufferObject>(dstBuffer.VkHandle, fn);
        if (cb is null || dst is null)
            return;
        var offset = dstOffset.Value;
        var bytes = Mappings.IsWholeSize(size.Value) ? (dst.GpuSize - Math.Min(offset, dst.GpuSize)) & ~3UL : size.Value;
        if (!WebProfileLint.CheckAligned4(fn, "dstOffset", offset) || !WebProfileLint.CheckAligned4(fn, "size", bytes))
            return;
        if (data == 0)
        {
            cb.Stream.Op3(Op.ClearBuffer, (uint)dst.Gpu, (uint)offset, (uint)bytes);
        }
        else
        {
            // non-zero fills are written as inline update records (64 KiB each)
            if (bytes > 1 << 20)
            {
                IcdLog.Unsupported(fn, $"non-zero fills larger than 1 MiB ({bytes} bytes)");
                return;
            }
            Array.Fill(s_fillPattern, (uint)data);
            fixed (uint* pattern = s_fillPattern)
                for (ulong done = 0; done < bytes; done += 65536)
                {
                    var chunk = (int)Math.Min(65536UL, bytes - done);
                    var p = cb.Stream.RecordWithData(Op.UpdateBuffer, 3, pattern, chunk);
                    p[0] = (uint)dst.Gpu;
                    p[1] = (uint)(offset + done);
                    p[2] = (uint)chunk;
                }
        }
        RequestReadback(cb, dst, offset, bytes);
    }
}
