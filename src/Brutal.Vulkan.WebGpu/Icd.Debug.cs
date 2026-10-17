using System.Text;
using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>The active <c>VkDebugUtilsMessengerEXT</c>s; the ICD reports its own findings and WebGPU errors through them.</summary>
internal static unsafe class DebugMessengers
{
    private static readonly List<DebugMessengerObject> s_active = new();
    private static bool s_emitting;

    public static DebugMessengerObject Create(VkDebugUtilsMessengerCreateInfoEXT* info) => new()
    {
        Severities = info->MessageSeverity,
        Types = info->MessageType,
        Callback = info->PfnUserCallback,
        UserData = info->UserData,
    };

    public static void Add(DebugMessengerObject m) => s_active.Add(m);

    public static void Remove(DebugMessengerObject m) => s_active.Remove(m);

    public static void Emit(VkDebugUtilsMessageSeverityFlagsEXT severity, VkDebugUtilsMessageTypeFlagsEXT type, string message)
    {
        if (s_active.Count == 0 || s_emitting)
            return;
        s_emitting = true; // a callback that logs through the ICD must not recurse
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message + "\0");
            fixed (byte* pMessage = bytes)
            fixed (byte* pId = "BRUTAL-webgpu\0"u8)
            {
                var data = new VkDebugUtilsMessengerCallbackDataEXT { MessageIdName = pId, Message = pMessage };
                foreach (var m in s_active.ToArray())
                    if ((m.Severities & severity) != 0 && (m.Types & type) != 0 && m.Callback.Invoke != null)
                        m.Callback.Invoke(severity, type, &data, m.UserData);
            }
        }
        finally
        {
            s_emitting = false;
        }
    }
}

internal static unsafe partial class Icd
{
    public static VkResult vkCreateDebugUtilsMessengerEXT(VkInstance instance, VkDebugUtilsMessengerCreateInfoEXT* pCreateInfo,
        VkAllocationCallbacks* pAllocator, VkDebugUtilsMessengerEXT* pMessenger)
    {
        var m = DebugMessengers.Create(pCreateInfo);
        HandleTable.Add(m);
        DebugMessengers.Add(m);
        *pMessenger = new VkDebugUtilsMessengerEXT(m.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyDebugUtilsMessengerEXT(VkInstance instance, VkDebugUtilsMessengerEXT messenger, VkAllocationCallbacks* pAllocator)
    {
        var m = HandleTable.Get<DebugMessengerObject>(messenger.VkHandle);
        if (m is null)
            return;
        DebugMessengers.Remove(m);
        HandleTable.Remove(m.Handle);
    }

    public static void vkSubmitDebugUtilsMessageEXT(VkInstance instance, VkDebugUtilsMessageSeverityFlagsEXT severity,
        VkDebugUtilsMessageTypeFlagsEXT types, VkDebugUtilsMessengerCallbackDataEXT* pCallbackData) =>
        DebugMessengers.Emit(severity, types, Str(pCallbackData->Message));

    public static VkResult vkSetDebugUtilsObjectNameEXT(VkDevice device, VkDebugUtilsObjectNameInfoEXT* pNameInfo)
    {
        var obj = HandleTable.Get<IcdObject>(pNameInfo->ObjectHandle);
        if (obj is null)
            return VkResult.Success;
        obj.Name = pNameInfo->ObjectName == null ? null : Str(pNameInfo->ObjectName);
        if (obj.Name is not null)
        {
            if (obj.GpuHandle > ReservedHandles.FirstDynamic - 1)
                WebIcd.Bridge.SetLabel(obj.GpuHandle, obj.Name);
            if (obj is ImageViewObject { AttachmentGpu: var a, Gpu: var g } && a != g && a != 0)
                WebIcd.Bridge.SetLabel(a, obj.Name);
        }
        return VkResult.Success;
    }

    public static VkResult vkSetDebugUtilsObjectTagEXT(VkDevice device, VkDebugUtilsObjectTagInfoEXT* pTagInfo) => VkResult.Success;

    public static void vkQueueBeginDebugUtilsLabelEXT(VkQueue queue, VkDebugUtilsLabelEXT* pLabelInfo) { }

    public static void vkQueueEndDebugUtilsLabelEXT(VkQueue queue) { }

    public static void vkQueueInsertDebugUtilsLabelEXT(VkQueue queue, VkDebugUtilsLabelEXT* pLabelInfo) { }

    public static void vkCmdBeginDebugUtilsLabelEXT(VkCommandBuffer commandBuffer, VkDebugUtilsLabelEXT* pLabelInfo)
    {
        var cb = Recording(commandBuffer, "vkCmdBeginDebugUtilsLabelEXT");
        if (cb is null)
            return;
        RecordLabel(cb, Op.PushDebugGroup, pLabelInfo->LabelName);
    }

    public static void vkCmdEndDebugUtilsLabelEXT(VkCommandBuffer commandBuffer) =>
        Recording(commandBuffer, "vkCmdEndDebugUtilsLabelEXT")?.Stream.Op0(Op.PopDebugGroup);

    public static void vkCmdInsertDebugUtilsLabelEXT(VkCommandBuffer commandBuffer, VkDebugUtilsLabelEXT* pLabelInfo)
    {
        var cb = Recording(commandBuffer, "vkCmdInsertDebugUtilsLabelEXT");
        if (cb is null)
            return;
        RecordLabel(cb, Op.InsertDebugMarker, pLabelInfo->LabelName);
    }

    private static void RecordLabel(CommandBufferObject cb, Op op, byte* label)
    {
        var length = 0;
        if (label != null)
            while (length < 1024 && label[length] != 0)
                length++;
        var p = cb.Stream.RecordWithData(op, 1, label, length);
        p[0] = (uint)length;
    }
}
