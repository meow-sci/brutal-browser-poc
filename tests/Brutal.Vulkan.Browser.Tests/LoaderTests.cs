using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Brutal.VulkanApi;
using Xunit.Abstractions;
using VulkanLoader = Brutal.VulkanApi.Vulkan; // `Vulkan` alone would bind to the Brutal.Vulkan namespace here

namespace Brutal.Vulkan.Browser.Tests;

/// <summary>
/// The pluggable loader in <c>Vulkan</c>'s static constructor (patches/brutal/0001-pluggable-loader-import-resolver.patch).
///
/// The static constructor runs once per loaded copy of Brutal.Vulkan, so the two orders (AppContext key absent / present)
/// cannot both be tested against the copy the test assembly references. Instead each test loads a fresh copy of the assembly
/// into its own collectible <see cref="AssemblyLoadContext"/> (dependencies resolve from the default context) and runs its
/// static constructor with the key cleared or set:
///   * managed flavor  = the referenced assembly's file (the flavor the browser uses);
///   * unmanaged flavor = unmanaged-flavor/Brutal.Vulkan.dll, built by the test project with BrutalManagedFnptr=false.
/// The copy the test assembly references (default context) is exercised end to end by <see cref="ManagedIcdTests"/>, where
/// the key is set by <c>ManagedIcd.Install()</c> before the first touch; its loader assertions are at the end of this class.
/// The AppContext key is process-wide: the tests run serially and restore it.
/// </summary>
public unsafe class LoaderTests(ITestOutputHelper output)
{
    private const string Key = "BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR";
    private static readonly string[] GlobalNames =
        ["vkCreateInstance", "vkEnumerateInstanceExtensionProperties", "vkEnumerateInstanceLayerProperties", "vkEnumerateInstanceVersion"];

    private static string ManagedAssemblyPath => typeof(VkCmdDraw).Assembly.Location;
    private static string UnmanagedAssemblyPath => Path.Combine(AppContext.BaseDirectory, "unmanaged-flavor", "Brutal.Vulkan.dll");

    // ---- a probe "ICD": records the (instance, name) pairs it is asked for and answers NULL ------------------------------
    private static readonly List<(nint Instance, string Name)> Requests = [];

    private static nint Record(nint instance, byte* name)
    {
        Requests.Add((instance, Marshal.PtrToStringUTF8((nint)name)!));
        return 0;
    }

    private static nint ManagedGetInstanceProcAddr(nint instance, byte* name) => Record(instance, name);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint UnmanagedGetInstanceProcAddr(nint instance, byte* name) => Record(instance, name);

    // The signature is ABI-compatible with VkInstance (one nint) and PfnVkVoidFunction (one pointer) of the isolated copies.
    private static nint ManagedPointer => (nint)(delegate*<nint, byte*, nint>)&ManagedGetInstanceProcAddr;
    private static nint UnmanagedPointer => (nint)(delegate* unmanaged[Cdecl]<nint, byte*, nint>)&UnmanagedGetInstanceProcAddr;

    // ---- isolated copy of Brutal.Vulkan ----------------------------------------------------------------------------------
    private sealed class IsolatedContext(string path) : AssemblyLoadContext("isolated-brutal-vulkan", isCollectible: true)
    {
        public Type VulkanType => LoadFromAssemblyPath(path).GetType("Brutal.VulkanApi.Vulkan", throwOnError: true)!;
        protected override Assembly? Load(AssemblyName name) => null; // everything else comes from the default context
    }

    /// <summary>Runs the static constructor of an isolated copy with the AppContext key set to <paramref name="key"/> (null = absent).</summary>
    private static (Type Vulkan, Exception? Failure, IsolatedContext Context) LoadWithKey(string path, object? key)
    {
        var previous = AppContext.GetData(Key);
        Requests.Clear();
        AppContext.SetData(Key, key);
        var context = new IsolatedContext(path);
        try
        {
            var vulkan = context.VulkanType;
            try
            {
                RuntimeHelpers.RunClassConstructor(vulkan.TypeHandle);
                return (vulkan, null, context);
            }
            catch (TypeInitializationException e)
            {
                return (vulkan, e.InnerException, context);
            }
        }
        finally
        {
            AppContext.SetData(Key, previous);
        }
    }

    /// <summary>Vulkan.GetInstanceProcAddr.Invoke of an isolated copy, read by reflection (the copy's types are not ours).</summary>
    private static nint GetInstanceProcAddrOf(Type vulkan)
    {
        var wrapper = vulkan.GetProperty("GetInstanceProcAddr")!.GetValue(null)!;
        var invoke = wrapper.GetType().GetField("Invoke")!.GetValue(wrapper)!;
        return (nint)invoke;
    }

    private static void AssertSuppliedProcAddrUsed(Type vulkan, Exception? failure, nint expected)
    {
        Assert.Null(failure);
        Assert.Equal(expected, GetInstanceProcAddrOf(vulkan));
        // The four global commands are resolved eagerly with vkGetInstanceProcAddr(NULL, name), in this order.
        Assert.Equal(GlobalNames, Requests.Select(r => r.Name));
        Assert.All(Requests, r => Assert.Equal(0, r.Instance));
    }

    // ---- managed flavor (the browser flavor) -----------------------------------------------------------------------------

    [Fact]
    public void ManagedFlavor_KeyAbsent_ThrowsPlatformNotSupported_WithGuidance()
    {
        var (_, failure, _) = LoadWithKey(ManagedAssemblyPath, null);
        var pns = Assert.IsType<PlatformNotSupportedException>(failure);
        Assert.Contains(Key, pns.Message);
        Assert.Contains("managed function pointers", pns.Message);
        Assert.Empty(Requests);
    }

    [Fact]
    public void ManagedFlavor_KeyPresent_UsesTheSuppliedProcAddr()
    {
        var pointer = ManagedPointer;
        var (vulkan, failure, _) = LoadWithKey(ManagedAssemblyPath, pointer);
        AssertSuppliedProcAddrUsed(vulkan, failure, pointer);
    }

    [Fact]
    public void ManagedFlavor_KeyAcceptsBoxedLongAndUlong()
    {
        foreach (object boxed in new object[] { (long)ManagedPointer, (ulong)ManagedPointer })
        {
            var (vulkan, failure, _) = LoadWithKey(ManagedAssemblyPath, boxed);
            AssertSuppliedProcAddrUsed(vulkan, failure, ManagedPointer);
        }
    }

    [Fact]
    public void ManagedFlavor_KeyOfWrongType_IsTreatedAsAbsent()
    {
        var (_, failure, _) = LoadWithKey(ManagedAssemblyPath, "not a pointer");
        Assert.IsType<PlatformNotSupportedException>(failure);
    }

    // ---- unmanaged flavor (-p:BrutalManagedFnptr=false): upstream behavior plus the key -----------------------------------

    [Fact]
    public void UnmanagedFlavor_KeyPresent_UsesTheSuppliedProcAddr()
    {
        var pointer = UnmanagedPointer;
        var (vulkan, failure, _) = LoadWithKey(UnmanagedAssemblyPath, pointer);
        AssertSuppliedProcAddrUsed(vulkan, failure, pointer);
    }

    [Fact]
    public void UnmanagedFlavor_KeyAbsent_OnDesktop_LoadsTheNativeLibraryLikeUpstream()
    {
        Assert.False(OperatingSystem.IsBrowser());
        var (vulkan, failure, _) = LoadWithKey(UnmanagedAssemblyPath, null);
        // Never the browser guidance: without the key a desktop process takes the unchanged upstream path.
        Assert.IsNotType<PlatformNotSupportedException>(failure);
        Assert.Empty(Requests);
        if (failure is null)
        {
            Assert.NotEqual(0, GetInstanceProcAddrOf(vulkan));
            output.WriteLine("native Vulkan library found: vkGetInstanceProcAddr resolved through the upstream loader");
        }
        else
        {
            // No Vulkan library on this machine (xunit v2 has no dynamic skip): only the negative assertion above applies.
            output.WriteLine($"skipped the native-load assertion, no Vulkan library: {failure.GetType().Name}: {failure.Message}");
        }
    }

    // ---- the referenced (default-context) managed copy, initialized by the ManagedIcd scenario -----------------------------

    [Fact]
    public void DefaultContext_ManagedCopy_UsesTheKeySetBeforeFirstUse()
    {
        var report = Scenario.Report;
        Assert.NotEqual(0, report.SuppliedGetInstanceProcAddr);
        Assert.Equal(report.SuppliedGetInstanceProcAddr, report.LoadedGetInstanceProcAddr);
        Assert.Equal(report.SuppliedGetInstanceProcAddr, (nint)VulkanLoader.GetInstanceProcAddr.Invoke);
    }
}
