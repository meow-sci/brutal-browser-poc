// [JSExport] wrappers around FakeIcd so that JS Module.addFunction trampolines can expose them as wasm function-table
// entries (callable through Brutal.Vulkan's delegate* unmanaged[Cdecl] tables) without a native relink.
using System.Runtime.InteropServices.JavaScript;
using Brutal;
using Brutal.VulkanApi;

namespace Exp04;

public static unsafe partial class BrowserIcd
{
    [JSExport] public static void Register(string name, int tableIndex) => FakeIcd.EntryPoints[name] = tableIndex;

    [JSExport] public static int GetInstanceProcAddr(int instance, int pName) => (int)(nint)FakeIcd.GetInstanceProcAddr(new VkInstance(instance), (byte*)pName).Invoke;
    [JSExport] public static int GetDeviceProcAddr(int device, int pName) => (int)(nint)FakeIcd.GetDeviceProcAddr(new VkDevice(device), (byte*)pName).Invoke;
    [JSExport] public static int EnumerateInstanceVersion(int p) => (int)FakeIcd.EnumerateInstanceVersion((int*)p);
    [JSExport] public static int EnumerateInstanceExtensionProperties(int l, int c, int p) => (int)FakeIcd.EnumerateInstanceExtensionProperties((byte*)l, (int*)c, (VkExtensionProperties*)p);
    [JSExport] public static int EnumerateInstanceLayerProperties(int c, int p) => (int)FakeIcd.EnumerateInstanceLayerProperties((int*)c, (VkLayerProperties*)p);
    [JSExport] public static int CreateInstance(int ci, int a, int i) => (int)FakeIcd.CreateInstance((VkInstanceCreateInfo*)ci, (VkAllocationCallbacks*)a, (VkInstance*)i);
    [JSExport] public static void DestroyInstance(int i, int a) => FakeIcd.DestroyInstance(new VkInstance(i), (VkAllocationCallbacks*)a);
    [JSExport] public static int EnumeratePhysicalDevices(int i, int c, int d) => (int)FakeIcd.EnumeratePhysicalDevices(new VkInstance(i), (int*)c, (VkPhysicalDevice*)d);
    [JSExport] public static void GetPhysicalDeviceProperties(int pd, int p) => FakeIcd.GetPhysicalDeviceProperties(new VkPhysicalDevice(pd), (VkPhysicalDeviceProperties*)p);
    [JSExport] public static void GetPhysicalDeviceQueueFamilyProperties(int pd, int c, int p) => FakeIcd.GetPhysicalDeviceQueueFamilyProperties(new VkPhysicalDevice(pd), (int*)c, (VkQueueFamilyProperties*)p);
    [JSExport] public static void GetPhysicalDeviceMemoryProperties(int pd, int p) => FakeIcd.GetPhysicalDeviceMemoryProperties(new VkPhysicalDevice(pd), (VkPhysicalDeviceMemoryProperties*)p);
    [JSExport] public static int EnumerateDeviceExtensionProperties(int pd, int l, int c, int p) => (int)FakeIcd.EnumerateDeviceExtensionProperties(new VkPhysicalDevice(pd), (byte*)l, (int*)c, (VkExtensionProperties*)p);
    [JSExport] public static int CreateDevice(int pd, int ci, int a, int d) => (int)FakeIcd.CreateDevice(new VkPhysicalDevice(pd), (VkDeviceCreateInfo*)ci, (VkAllocationCallbacks*)a, (VkDevice*)d);
    [JSExport] public static void DestroyDevice(int d, int a) => FakeIcd.DestroyDevice(new VkDevice(d), (VkAllocationCallbacks*)a);
    [JSExport] public static void GetDeviceQueue(int d, int f, int i, int q) => FakeIcd.GetDeviceQueue(new VkDevice(d), f, i, (VkQueue*)q);
    [JSExport] public static int CreateBuffer(int d, int ci, int a, int b) => (int)FakeIcd.CreateBuffer(new VkDevice(d), (VkBufferCreateInfo*)ci, (VkAllocationCallbacks*)a, (VkBuffer*)b);
    [JSExport] public static void DestroyBuffer(int d, int b, int a) => FakeIcd.DestroyBuffer(new VkDevice(d), new VkBuffer(b), (VkAllocationCallbacks*)a);
    [JSExport] public static void GetBufferMemoryRequirements(int d, int b, int r) => FakeIcd.GetBufferMemoryRequirements(new VkDevice(d), new VkBuffer(b), (VkMemoryRequirements*)r);
    [JSExport] public static int AllocateMemory(int d, int i, int a, int m) => (int)FakeIcd.AllocateMemory(new VkDevice(d), (VkMemoryAllocateInfo*)i, (VkAllocationCallbacks*)a, (VkDeviceMemory*)m);
    [JSExport] public static void FreeMemory(int d, int m, int a) => FakeIcd.FreeMemory(new VkDevice(d), new VkDeviceMemory(m), (VkAllocationCallbacks*)a);
    [JSExport] public static int BindBufferMemory(int d, int b, int m, [JSMarshalAs<JSType.BigInt>] long o) => (int)FakeIcd.BindBufferMemory(new VkDevice(d), new VkBuffer(b), new VkDeviceMemory(m), new ByteSize64((ulong)o));
}
