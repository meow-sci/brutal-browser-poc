using Brutal;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;
using WebGpuIcdSmoke;

namespace WebGpuIcd.Tests;

/// <summary>Failure paths: once-only logging on per-frame commands, device loss, a push-constant ring that cannot grow.</summary>
public sealed unsafe class FailureTests
{
    private readonly RecordingBridge _bridge = TestIcd.Reset();
    private static readonly List<string> s_messages = new();

    private static TriangleScene NewScene() =>
        new(TestIcd.Resource("Shaders/tri.vert.spv"), TestIcd.Resource("Shaders/tri.frag.spv"));

    [Fact]
    public void IgnoredPerFrameCommandsWarnOnceAndDoNotAllocateAfterTheFirstCall()
    {
        for (var i = 0; i < 10; i++)
        {
            Icd.vkCmdSetDepthBias(default, 1f, 0f, 1f);
            Icd.vkCmdSetLineWidth(default, 2f);
            Icd.vkCmdSetStencilCompareMask(default, VkStencilFaceFlags.FrontAndBack, 0xff);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            Icd.vkCmdSetDepthBias(default, 1f, 0f, 1f);
            Icd.vkCmdSetLineWidth(default, 2f);
            Icd.vkCmdSetStencilCompareMask(default, VkStencilFaceFlags.FrontAndBack, 0xff);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024, $"{allocated} bytes allocated by 30000 ignored calls");
        lock (TestIcd.Log)
        {
            Assert.Single(TestIcd.Log, l => l.StartsWith("WARN [webgpu] vkCmdSetDepthBias:", StringComparison.Ordinal));
            Assert.Single(TestIcd.Log, l => l.StartsWith("WARN [webgpu] vkCmdSetLineWidth:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void DeviceLossFailsAcquireSubmitAndPresentAndReachesTheDebugMessenger()
    {
        using var scene = NewScene();
        Assert.True(scene.Frame());
        var messenger = AddMessenger();
        try
        {
            _bridge.DeviceLost = true;
            _bridge.PendingErrors = "device lost (unknown): simulated";
            var error = Assert.Throws<VkResultError>(() => scene.Frame()); // vkAcquireNextImageKHR
            Assert.Equal(VkResult.ErrorDeviceLost, error.Result);

            Assert.Equal(VkResult.ErrorDeviceLost, Icd.vkQueueSubmit(scene.Queue.Handle, 0, null, default));
            var info = new VkPresentInfoKHR();
            Assert.Equal(VkResult.ErrorDeviceLost, Icd.vkQueuePresentKHR(scene.Queue.Handle, &info));

            var errors = TestIcd.Errors();
            Assert.Contains(errors, l => l.Contains("device lost (unknown): simulated", StringComparison.Ordinal));
            Assert.Single(errors, l => l.Contains("VK_ERROR_DEVICE_LOST", StringComparison.Ordinal));
            lock (s_messages)
                Assert.Contains(s_messages, m => m.Contains("VK_ERROR_DEVICE_LOST", StringComparison.Ordinal));
        }
        finally
        {
            Icd.vkDestroyDebugUtilsMessengerEXT(default, messenger, null);
            _bridge.DeviceLost = false;
            TestIcd.Reset();
        }
    }

    [Fact]
    public void APushRingThatCannotGrowFailsTheSubmitWithOutOfDeviceMemory()
    {
        using var scene = NewScene();
        _bridge.ReplayFlags = BridgeStatus.ReplayOutOfMemory;
        var error = Assert.Throws<VkResultError>(() => scene.Frame()); // vkQueueSubmit
        Assert.Equal(VkResult.ErrorOutOfDeviceMemory, error.Result);
        Assert.Contains(TestIcd.Errors(), l => l.Contains("VK_ERROR_OUT_OF_DEVICE_MEMORY", StringComparison.Ordinal));
    }

    private static VkDebugUtilsMessengerEXT AddMessenger()
    {
        lock (s_messages)
            s_messages.Clear();
        var info = new VkDebugUtilsMessengerCreateInfoEXT
        {
            MessageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.ErrorBitEXT | VkDebugUtilsMessageSeverityFlagsEXT.WarningBitEXT,
            MessageType = VkDebugUtilsMessageTypeFlagsEXT.ValidationBitEXT | VkDebugUtilsMessageTypeFlagsEXT.GeneralBitEXT,
            PfnUserCallback = new PfnVkDebugUtilsMessengerCallbackEXT(&OnMessage),
        };
        VkDebugUtilsMessengerEXT messenger;
        Assert.Equal(VkResult.Success, Icd.vkCreateDebugUtilsMessengerEXT(default, &info, null, &messenger));
        return messenger;
    }

    private static Bool32 OnMessage(VkDebugUtilsMessageSeverityFlagsEXT severity, VkDebugUtilsMessageTypeFlagsEXT type,
        VkDebugUtilsMessengerCallbackDataEXT* data, void* userData)
    {
        lock (s_messages)
            s_messages.Add(new string((sbyte*)data->Message));
        return false;
    }
}
