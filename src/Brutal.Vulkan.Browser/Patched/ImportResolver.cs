// brutal-browser: pluggable loader for Brutal.Vulkan (patches/brutal/brutal-vulkan-pluggable-loader.patch), one source for both flavors.
//   default              : Brutal.Vulkan's generated tables hold delegate* unmanaged[Cdecl]<...> (upstream).
//   BRUTAL_MANAGED_FNPTR : patches/brutal/transform.py rewrote them to managed delegate*<...>; the supplied
//                          vkGetInstanceProcAddr and every pointer it returns are managed function pointers
//                          (&StaticMethod), called with a managed calli (no native transition).
#if BRUTAL_MANAGED_FNPTR
using unsafe GetInstanceProcAddrFn = delegate*<Brutal.VulkanApi.VkInstance, byte*, Brutal.VulkanApi.PfnVkVoidFunction>;
using unsafe CreateInstanceFnPtr = delegate*<Brutal.VulkanApi.VkInstanceCreateInfo*, Brutal.VulkanApi.VkAllocationCallbacks*, Brutal.VulkanApi.VkInstance*, Brutal.VulkanApi.VkResult>;
using unsafe EnumerateInstanceExtensionPropertiesFnPtr = delegate*<byte*, int*, Brutal.VulkanApi.VkExtensionProperties*, Brutal.VulkanApi.VkResult>;
using unsafe EnumerateInstanceLayerPropertiesFnPtr = delegate*<int*, Brutal.VulkanApi.VkLayerProperties*, Brutal.VulkanApi.VkResult>;
using unsafe EnumerateInstanceVersionFnPtr = delegate*<int*, Brutal.VulkanApi.VkResult>;
using unsafe VoidFnPtr = delegate*<void>;
#else
using unsafe GetInstanceProcAddrFn = delegate* unmanaged[Cdecl]<Brutal.VulkanApi.VkInstance, byte*, Brutal.VulkanApi.PfnVkVoidFunction>;
using unsafe CreateInstanceFnPtr = delegate* unmanaged[Cdecl]<Brutal.VulkanApi.VkInstanceCreateInfo*, Brutal.VulkanApi.VkAllocationCallbacks*, Brutal.VulkanApi.VkInstance*, Brutal.VulkanApi.VkResult>;
using unsafe EnumerateInstanceExtensionPropertiesFnPtr = delegate* unmanaged[Cdecl]<byte*, int*, Brutal.VulkanApi.VkExtensionProperties*, Brutal.VulkanApi.VkResult>;
using unsafe EnumerateInstanceLayerPropertiesFnPtr = delegate* unmanaged[Cdecl]<int*, Brutal.VulkanApi.VkLayerProperties*, Brutal.VulkanApi.VkResult>;
using unsafe EnumerateInstanceVersionFnPtr = delegate* unmanaged[Cdecl]<int*, Brutal.VulkanApi.VkResult>;
using unsafe VoidFnPtr = delegate* unmanaged[Cdecl]<void>;
#endif

namespace Brutal.VulkanApi;

public partial class Vulkan
{
    // The dll name we expect LibraryImport functions calling Vulkan to use
    private const string PInvokeDllName = "vulkan";

    // AppContext key that lets the application supply vkGetInstanceProcAddr itself instead of having a native
    // Vulkan library loaded. Set it with AppContext.SetData before the first use of Vulkan; the value is a boxed
    // nint (or long/ulong) holding a delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction>, or, in the
    // managed flavor, a managed delegate*<VkInstance, byte*, PfnVkVoidFunction> (e.g. &MyIcd.GetInstanceProcAddr).
    // Required on browser-wasm, where there is no native loader; honoured on every platform (software ICDs,
    // layering, tests). When it is absent the native loading below is unchanged.
    private const string GetInstanceProcAddrKey = "BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR";

#if !BRUTAL_MANAGED_FNPTR
    // Cached reference to the loaded Vulkan dll
    private static readonly nint Dll;
#endif

    /// <summary>
    /// The function pointer to vkGetInstanceProcAddr, loaded from the Vulkan library.
    /// </summary>
    public static VkGetInstanceProcAddr GetInstanceProcAddr { get; }

    // Global commands resolved through vkGetInstanceProcAddr(NULL, ...) when the loader was supplied through
    // AppContext; unused (null) when the native library and its LibraryImport exports are used.
#if !BRUTAL_MANAGED_FNPTR
    private static readonly bool UseProcAddrForGlobalCommands;
#endif
    private static readonly PfnVkVoidFunction CreateInstanceFn;
    private static readonly PfnVkVoidFunction EnumerateInstanceExtensionPropertiesFn;
    private static readonly PfnVkVoidFunction EnumerateInstanceLayerPropertiesFn;
    private static readonly PfnVkVoidFunction EnumerateInstanceVersionFn;

    static Vulkan()
    {
        // Use an application supplied vkGetInstanceProcAddr when one is configured
        var suppliedGetInstanceProcAddr = AppContext.GetData(GetInstanceProcAddrKey) switch
        {
            nint value => value,
            long value => (nint)value,
            ulong value => (nint)value,
            _ => nint.Zero,
        };

        if (suppliedGetInstanceProcAddr != nint.Zero)
        {
            unsafe
            {
                GetInstanceProcAddr = new VkGetInstanceProcAddr((GetInstanceProcAddrFn)suppliedGetInstanceProcAddr);
                CreateInstanceFn = GetInstanceProcAddr.Invoke(default, "vkCreateInstance"u8.AsPointer());
                EnumerateInstanceExtensionPropertiesFn = GetInstanceProcAddr.Invoke(
                    default,
                    "vkEnumerateInstanceExtensionProperties"u8.AsPointer()
                );
                EnumerateInstanceLayerPropertiesFn = GetInstanceProcAddr.Invoke(
                    default,
                    "vkEnumerateInstanceLayerProperties"u8.AsPointer()
                );
                EnumerateInstanceVersionFn = GetInstanceProcAddr.Invoke(
                    default,
                    "vkEnumerateInstanceVersion"u8.AsPointer()
                );
            }
#if !BRUTAL_MANAGED_FNPTR
            UseProcAddrForGlobalCommands = true;
#endif
            return;
        }

#if BRUTAL_MANAGED_FNPTR
        // A managed function-pointer build can only talk to a managed ICD: native exports are not managed methods.
        throw new PlatformNotSupportedException(
            $"This Brutal.Vulkan build uses managed function pointers (BRUTAL_MANAGED_FNPTR) and has no native Vulkan "
                + $"loader. Set AppContext data '{GetInstanceProcAddrKey}' to a managed vkGetInstanceProcAddr "
                + "(delegate*<VkInstance, byte*, PfnVkVoidFunction>) before first using Vulkan."
        );
#else
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException(
                $"There is no native Vulkan library on this platform. Set AppContext data '{GetInstanceProcAddrKey}' "
                    + "to a vkGetInstanceProcAddr function pointer before first using Vulkan."
            );

        // Load the correct Vulkan library for the platform

        if (OperatingSystem.IsWindows())
            Dll = NativeLibrary.Load("vulkan-1", typeof(Vulkan).Assembly, DllImportSearchPath.System32);

        // TODO: This is a hack which only works if SDK is installed, we need to package the Vulkan binary on macOS
        if (OperatingSystem.IsMacOS())
            Dll = NativeLibrary.Load("VulkanEx", typeof(Vulkan).Assembly, DllImportSearchPath.LegacyBehavior);

        if (OperatingSystem.IsLinux())
            Dll = NativeLibrary.Load("vulkan", typeof(Vulkan).Assembly, DllImportSearchPath.LegacyBehavior);

        if (Dll == nint.Zero)
            throw new NullReferenceException("Failed to load Vulkan library");

        // Assign the native library to the assembly resolver
        NativeLibrary.SetDllImportResolver(
            typeof(Vulkan).Assembly,
            (libraryName, _, _) => libraryName == PInvokeDllName ? Dll : nint.Zero
        );

        // Load the vkGetInstanceProcAddr function
        unsafe
        {
            GetInstanceProcAddr = new VkGetInstanceProcAddr(
                (delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction>)
                    NativeLibrary.GetExport(Dll, "vkGetInstanceProcAddr")
            );
            if (GetInstanceProcAddr.Invoke == null)
                throw new NullReferenceException("Failed to load vkGetInstanceProcAddr from Vulkan library");
        }
#endif
    }

    /// <summary>
    /// The four global (instance-less) commands. They go to the LibraryImport exports of the native library, or
    /// through vkGetInstanceProcAddr(NULL, name) when the application supplied vkGetInstanceProcAddr.
    /// The OperatingSystem.IsBrowser() test is constant-folded by the trimmer, so browser builds drop the P/Invokes.
    /// </summary>
    internal static unsafe class GlobalCommands
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VkResult vkCreateInstance(
            VkInstanceCreateInfo* pCreateInfo,
            VkAllocationCallbacks* pAllocator,
            VkInstance* pInstance
        )
        {
#if !BRUTAL_MANAGED_FNPTR
            if (!OperatingSystem.IsBrowser() && !UseProcAddrForGlobalCommands)
                return PInvoke.vkCreateInstance(pCreateInfo, pAllocator, pInstance);
#endif
            return ((CreateInstanceFnPtr)
                Resolved(CreateInstanceFn, "vkCreateInstance"))(pCreateInfo, pAllocator, pInstance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VkResult vkEnumerateInstanceExtensionProperties(
            byte* pLayerName,
            int* pPropertyCount,
            VkExtensionProperties* pProperties
        )
        {
#if !BRUTAL_MANAGED_FNPTR
            if (!OperatingSystem.IsBrowser() && !UseProcAddrForGlobalCommands)
                return PInvoke.vkEnumerateInstanceExtensionProperties(pLayerName, pPropertyCount, pProperties);
#endif
            return ((EnumerateInstanceExtensionPropertiesFnPtr)
                Resolved(EnumerateInstanceExtensionPropertiesFn, "vkEnumerateInstanceExtensionProperties"))(
                pLayerName,
                pPropertyCount,
                pProperties
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VkResult vkEnumerateInstanceLayerProperties(int* pPropertyCount, VkLayerProperties* pProperties)
        {
#if !BRUTAL_MANAGED_FNPTR
            if (!OperatingSystem.IsBrowser() && !UseProcAddrForGlobalCommands)
                return PInvoke.vkEnumerateInstanceLayerProperties(pPropertyCount, pProperties);
#endif
            return ((EnumerateInstanceLayerPropertiesFnPtr)
                Resolved(EnumerateInstanceLayerPropertiesFn, "vkEnumerateInstanceLayerProperties"))(
                pPropertyCount,
                pProperties
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VkResult vkEnumerateInstanceVersion(int* pApiVersion)
        {
#if !BRUTAL_MANAGED_FNPTR
            if (!OperatingSystem.IsBrowser() && !UseProcAddrForGlobalCommands)
                return PInvoke.vkEnumerateInstanceVersion(pApiVersion);
#endif
            return ((EnumerateInstanceVersionFnPtr)
                Resolved(EnumerateInstanceVersionFn, "vkEnumerateInstanceVersion"))(pApiVersion);
        }

        private static VoidFnPtr Resolved(PfnVkVoidFunction function, string name)
        {
            if (function.Invoke == null)
                throw new EntryPointNotFoundException($"vkGetInstanceProcAddr(NULL, \"{name}\") returned NULL");
            return function.Invoke;
        }
    }
}
