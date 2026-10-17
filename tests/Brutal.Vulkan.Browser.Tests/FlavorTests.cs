using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace Brutal.Vulkan.Browser.Tests;

/// <summary>The built assemblies really are the managed function-pointer flavor of BRUTAL's bindings (metadata checks, no Vulkan call).</summary>
public class FlavorTests
{
    private static readonly Assembly VulkanAssembly = typeof(VkCmdDraw).Assembly;
    private static readonly Assembly AbstractionsAssembly = typeof(DeviceEx).Assembly;

    private static FieldInfo InvokeField(Type wrapper) =>
        wrapper.GetField("Invoke", BindingFlags.Public | BindingFlags.Instance) ?? throw new InvalidOperationException($"{wrapper} has no public Invoke field");

    // Global commands (vkCreateInstance, ...) have no Pfn wrapper: they are resolved by Vulkan.GlobalCommands through
    // vkGetInstanceProcAddr(NULL, name). VkGetInstanceProcAddr, VkCmdDraw and PfnVkVoidFunction are representative wrappers.
    [Theory]
    [InlineData(typeof(VkGetInstanceProcAddr))]
    [InlineData(typeof(PfnVkVoidFunction))]
    [InlineData(typeof(VkCmdDraw))]
    [InlineData(typeof(VkCmdPipelineBarrier))]
    public void WrapperInvokeField_IsManagedFunctionPointer(Type wrapper)
    {
        var type = InvokeField(wrapper).FieldType;
        Assert.True(type.IsFunctionPointer, $"{wrapper.Name}.Invoke should be a function pointer, was {type}");
        Assert.False(type.IsUnmanagedFunctionPointer, $"{wrapper.Name}.Invoke must not be an unmanaged function pointer (calling convention)");
        Assert.Empty(type.GetFunctionPointerCallingConventions());
    }

    [Fact]
    public void EveryWrapperInvokeField_IsManaged()
    {
        var wrappers = VulkanAssembly.GetTypes()
            .Select(t => (Type: t, Field: t.GetField("Invoke", BindingFlags.Public | BindingFlags.Instance)))
            .Where(x => x.Type.IsValueType && x.Field is { FieldType.IsFunctionPointer: true })
            .ToList();
        Assert.True(wrappers.Count > 800, $"expected hundreds of Pfn/Vk wrapper structs, found {wrappers.Count}");
        var unmanaged = wrappers.Where(x => x.Field!.FieldType.IsUnmanagedFunctionPointer).Select(x => x.Type.Name).ToList();
        Assert.True(unmanaged.Count == 0, $"{unmanaged.Count} of {wrappers.Count} wrappers still hold unmanaged function pointers, e.g. {string.Join(", ", unmanaged.Take(5))}");
    }

    [Fact]
    public void VulkanClass_GetInstanceProcAddrProperty_IsManagedWrapper()
    {
        // Reading the property would run the static constructor (and need an ICD): inspect the type only.
        var property = typeof(Brutal.VulkanApi.Vulkan).GetProperty(nameof(Brutal.VulkanApi.Vulkan.GetInstanceProcAddr))!;
        Assert.Equal(typeof(VkGetInstanceProcAddr), property.PropertyType);
    }

    [Fact]
    public void AbstractionsDebugCallbacks_AreNotUnmanagedCallersOnly()
    {
        // Transform rule R5: with managed pointer types `&Callbacks.Quiet` must be a plain static method.
        var callbacks = typeof(VulkanHelpers.DebugUtilsMessengerCallbacks);
        foreach (var name in new[] { "Quiet", "Default", "Verbose" })
        {
            var method = callbacks.GetMethod(name, BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException(name);
            Assert.Null(method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>());
        }
    }

    [Theory]
    [InlineData("Brutal.Vulkan")]
    [InlineData("Brutal.Vulkan.Abstractions")]
    public void Assembly_HasPublishedIdentity(string name)
    {
        var assembly = name == "Brutal.Vulkan" ? VulkanAssembly : AbstractionsAssembly;
        var identity = assembly.GetName();
        Assert.Equal(name, identity.Name);
        Assert.Equal(new Version(1, 0, 0, 0), identity.Version);
        Assert.Empty(identity.GetPublicKeyToken() ?? []);
        Assert.NotNull(assembly.GetCustomAttribute<DisableRuntimeMarshallingAttribute>());
        Assert.EndsWith(".managed-fnptr", assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
    }

    [Fact]
    public void Vulkan_KeepsInternalsVisibleToAbstractions_AndAbstractionsBindToIt()
    {
        Assert.Contains(VulkanAssembly.GetCustomAttributes<InternalsVisibleToAttribute>(), a => a.AssemblyName == "Brutal.Vulkan.Abstractions");
        var reference = AbstractionsAssembly.GetReferencedAssemblies().Single(a => a.Name == "Brutal.Vulkan");
        Assert.Equal(new Version(1, 0, 0, 0), reference.Version);
    }

    [Fact]
    public void LoadedAssemblies_AreTheProjectReferenceOutput_NotTheNuGetPackage()
    {
        // A NuGet package (Ahwoo.BRUTAL.Vulkan) would resolve from the package cache, not from the test output folder.
        var outDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        foreach (var assembly in new[] { VulkanAssembly, AbstractionsAssembly })
            Assert.Equal(outDir, Path.GetDirectoryName(assembly.Location));
    }
}
