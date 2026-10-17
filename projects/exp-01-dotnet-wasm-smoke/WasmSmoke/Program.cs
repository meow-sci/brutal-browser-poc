// exp-01-dotnet-wasm-smoke: feasibility smoke tests for running BRUTAL (open-source C# game framework)
// on .NET 10 browser-wasm (Mono interpreter, no wasm-tools workload / no native relink).
//
// Every test prints exactly one line:  "TEST <id> <name>: PASS <details>"  or  "TEST <id> <name>: FAIL <Type>: <Message> [| inner ...]"
// Output goes to stdout which the .NET wasm runtime forwards to the browser console (console.log).

using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading.Tasks;
using Brutal;
using Brutal.Collections;
using Brutal.Logging;
using Brutal.Maths;
using Brutal.Memory;
using Brutal.Numerics;
using Brutal.Pointers;
using Brutal.Pointers.Extensions;
using Brutal.VulkanApi;
using Microsoft.Extensions.Logging;

// Main lives in a non-unsafe part because C# forbids await in an unsafe context.
public static partial class SmokeTests
{
    // ------------------------------------------------------------------ Main

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("SMOKE START args=[" + string.Join(",", args) + "]");
        foreach (var a in args)
        {
            if (a.StartsWith("tests=") && a.Length > 6) s_only = new(a.Substring(6).Split(',', StringSplitOptions.RemoveEmptyEntries));
            if (a.StartsWith("skip=") && a.Length > 5) s_skip = new(a.Substring(5).Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        Run("T01", "env", T01_Env);
        Run("T02", "brutal-platform", T02_Platform);
        Run("T03", "brutal-numerics", T03_Numerics);
        Run("T03b", "brutal-collections-logging", T03b_CollectionsLogging);
        Run("T03c", "brutal-logging-console-provider", T03c_LoggingConsole);
        Run("T03d", "brutal-logging-custom-provider", T03d_LoggingCustom);
        Run("T04", "brutal-memory", T04_Memory);
        Run("T04b", "memory-growth-256MB", T04b_MemoryGrowth);
        Run("T05", "brutal-vulkan-static-ctor", T05_VulkanStaticCtor);
        Run("T05b", "brutal-vulkan-types-without-ctor", T05b_VulkanTypes);
        Run("T06", "nativelibrary", T06_NativeLibrary);
        Run("T06b", "dllimport-vulkan-local", T06b_DllImportVulkan);
        Run("T06c", "brutal-libraryimport-vkEnumerateInstanceVersion", T06c_BrutalPInvoke);
        Run("T06d", "dllimport-known-native-SystemNative_GetPid", T06d_DllImportKnown);
        Run("T07a", "fnptr-ldftn-value (no call)", T07a_FnPtrValue);
        Run("T07", "fnptr-basic", T07_FnPtrBasic);
        Run("T08", "fnptr-vulkan-shape (managed ICD, C# UCO)", T08_FnPtrVulkanShape);
        Run("T08b", "brutal-FunctionTable-from-managed-gipa", T08b_FunctionTableManaged);
        Run("T08c", "fnptr-js-addFunction", T08c_JsAddFunction);
        Run("T08d", "brutal-FunctionTable-from-JS-gipa (JS ICD)", T08d_FunctionTableJs);
        Run("T08e", "fnptr-js-trampoline-to-JSExport (managed ICD via JS hop)", T08e_JsTrampolineToManaged);
        Run("T09", "fnptr-mixed-args", T09_MixedArgs);
        Run("T09b", "fnptr-mixed-args-via-js-trampoline", T09b_MixedArgsJs);
        Run("T10", "delegate-marshal", T10_DelegateMarshal);
        Run("T11a", "jsimport-hasWebGpu", T11a_HasWebGpu);
        Run("T11b", "jsimport-span-memoryview", T11b_SpanMemoryView);
        Run("T11c", "jsimport-raw-pointer-heap", T11c_RawPointer);
        Run("T11d", "jsimport-heap-api-survey", T11d_HeapApiSurvey);
        if (Selected("T11e"))
        {
            Console.WriteLine("BEGIN T11e");
            try
            {
                var info = await WebGpuAdapterInfo();
                s_pass++;
                Console.WriteLine($"TEST T11e jsimport-async-webgpu-adapter: PASS {info}");
            }
            catch (Exception e)
            {
                s_fail++;
                Console.WriteLine($"TEST T11e jsimport-async-webgpu-adapter: FAIL {Describe(e)}");
            }
        }
        Run("T13a", "perf-direct-static-call", T13a_Direct);
        Run("T13b", "perf-fnptr-to-CSharp-UCO", T13b_FnPtrUco);
        Run("T13f", "perf-managed-fnptr (delegate*<int,int,int>)", T13f_ManagedFnPtr);
        Run("T13b2", "perf-fnptr-to-JS-addFunction", T13b2_FnPtrJs);
        Run("T13b3", "perf-fnptr-to-JS-trampoline-to-JSExport", T13b3_FnPtrJsTrampoline);
        Run("T13c", "perf-jsimport-int", T13c_JsImportInt);
        Run("T13d", "perf-jsimport-span64", T13d_JsImportSpan);
        Run("T13d2", "perf-jsimport-span64-copy", T13d2_JsImportSpanCopy);
        RunSigProbes();
        RunExtraProbes();

        Console.WriteLine($"SMOKE MAIN DONE pass={s_pass} fail={s_fail} (T12 runs from JS rAF after Main returns)");
        return 0;
    }

}

public static unsafe partial class SmokeTests
{
    // ------------------------------------------------------------------ harness

    static int s_pass, s_fail;
    static System.Collections.Generic.HashSet<string> s_only, s_skip;

    // Test selection (from application arguments, which main.js takes from the page query string):
    //   tests=T07,T08   run only these ids      skip=T07,T08   run everything except these ids
    static bool Selected(string id)
    {
        if (s_only != null && !s_only.Contains(id)) return false;
        if (s_skip != null && s_skip.Contains(id)) return false;
        return true;
    }

    static void Run(string id, string name, Func<string> body)
    {
        if (!Selected(id)) return;
        // BEGIN marker lets the driver attribute a fatal wasm trap (which kills the runtime) to a test id.
        Console.WriteLine($"BEGIN {id}");
        try
        {
            var details = body();
            s_pass++;
            Console.WriteLine($"TEST {id} {name}: PASS {details}");
        }
        catch (Exception e)
        {
            s_fail++;
            Console.WriteLine($"TEST {id} {name}: FAIL {Describe(e)}");
        }
    }

    // Signature probes (SigProbes.g.cs) only run when explicitly requested (?tests=P07), one per page load,
    // because a missing interp->native trampoline aborts the whole runtime.
    static void RunProbe(string id, string name, Func<string> body)
    {
        if (s_only == null || !s_only.Contains(id)) return;
        Run(id, name, body);
    }

    // Formats an exception including the whole InnerException chain (TypeInitializationException etc.)
    static string Describe(Exception e)
    {
        var sb = new StringBuilder();
        sb.Append(e.GetType().FullName).Append(": ").Append(e.Message);
        var inner = e.InnerException;
        while (inner != null)
        {
            sb.Append(" | inner ").Append(inner.GetType().FullName).Append(": ").Append(inner.Message);
            inner = inner.InnerException;
        }
        return sb.ToString();
    }

    // Runs one probe inside a larger test and returns "ok:<value>" or "EX <Type>: <msg>" so one throwing call
    // does not hide the remaining probes.
    static string Probe(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return "EX " + Describe(e); }
    }

    // ------------------------------------------------------------------ T01 env

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T01_Env()
    {
        return string.Join("; ", new[]
        {
            $"IsBrowser={OperatingSystem.IsBrowser()}",
            $"IsWasi={OperatingSystem.IsWasi()}",
            $"OSDescription='{RuntimeInformation.OSDescription}'",
            $"OSArchitecture={RuntimeInformation.OSArchitecture}",
            $"ProcessArchitecture={RuntimeInformation.ProcessArchitecture}",
            $"FrameworkDescription='{RuntimeInformation.FrameworkDescription}'",
            $"RuntimeIdentifier={RuntimeInformation.RuntimeIdentifier}",
            $"IntPtr.Size={IntPtr.Size}",
            $"ProcessorCount={Environment.ProcessorCount}",
            $"Is64BitProcess={Environment.Is64BitProcess}",
            $"Vector128.IsHardwareAccelerated={Vector128.IsHardwareAccelerated}",
            $"Vector.IsHardwareAccelerated={Vector.IsHardwareAccelerated}",
            $"Vector<float>.Count={Vector<float>.Count}",
            $"PackedSimd.IsSupported={System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported}",
            $"Stopwatch.Frequency={Stopwatch.Frequency}",
            $"Stopwatch.IsHighResolution={Stopwatch.IsHighResolution}",
            $"Environment.Version={Environment.Version}",
            $"IsDynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported}",
            $"IsDynamicCodeCompiled={RuntimeFeature.IsDynamicCodeCompiled}",
            $"ThreadId={Environment.CurrentManagedThreadId}",
            $"GC.TotalAvailableMemoryBytes={GC.GetGCMemoryInfo().TotalAvailableMemoryBytes}",
        });
    }

    // ------------------------------------------------------------------ T02 Brutal.Platform

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T02_Platform()
    {
        var fam = Platform.CurrentFamily; // static ctor throws PlatformNotSupportedException on browser
        return "CurrentFamily=" + fam;
    }

    // ------------------------------------------------------------------ T03 Brutal.Numerics / Maths

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T03_Numerics()
    {
        var rot = float4x4.CreateRotationY(MathF.PI / 2);
        var tr = float4x4.CreateTranslation(1, 2, 3);
        var m = rot * tr;
        var p = float3.Transform(new float3(1, 0, 0), m);
        var v4 = m * new float4(1, 0, 0, 1); // operator *(float4x4, float4)

        // System.Numerics reference for parity
        var sm = Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(1, 2, 3);
        var sp = Vector3.Transform(new Vector3(1, 0, 0), sm);

        bool ok = MathF.Abs(p.X - sp.X) < 1e-5f && MathF.Abs(p.Y - sp.Y) < 1e-5f && MathF.Abs(p.Z - sp.Z) < 1e-5f;
        var rnd = new Random(42).NextSinglePointInBox(2f); // Brutal.Maths extension
        var c = float3.Cross(new float3(1, 0, 0), new float3(0, 1, 0));
        if (!ok) throw new Exception($"mismatch brutal={p} sysnum={sp}");
        return $"float3.Transform((1,0,0), RotY(pi/2)*T(1,2,3)) = {p} (System.Numerics={sp}); float4x4*float4 = {v4}; cross(X,Y)={c}; sizeof(float4x4)={sizeof(float4x4)}; Random(42).NextSinglePointInBox(2)={rnd}";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T03b_CollectionsLogging()
    {
        var parts = new System.Collections.Generic.List<string>();
        parts.Add("NativeList: " + Probe(() =>
        {
            using var list = new NativeList();
            list.Add(42);
            list.Add(7);
            list.Add(1.5f);
            return "ok Size=" + list.Size + " Capacity=" + list.Capacity;
        }));
        parts.Add("LogCategory.Make: " + Probe(() =>
        {
            var cat = LogCategory.Make("Smoke");
            cat.Info($"info message before LogSystem.Initialize (expected to be dropped)");
            return "ok Name=" + cat.Name + " LogSystem.IsEnabled=" + LogSystem.IsEnabled;
        }));
        return string.Join("; ", parts);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T03c_LoggingConsole()
    {
        // Microsoft.Extensions.Logging.Console is annotated [UnsupportedOSPlatform("browser")]; observe what happens.
#pragma warning disable CA1416
        LogSystem.Initialize(b => b.AddConsole());
#pragma warning restore CA1416
        var cat = LogCategory.Make("SmokeConsole");
        cat.Info($"hello from Brutal.Logging via MEL console provider");
        return "LogSystem.Initialize(AddConsole) returned; IsEnabled=" + LogSystem.IsEnabled + " (check console for the log line)";
    }

    sealed class ConsoleWriteLineProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new L(categoryName);
        public void Dispose() { }
        sealed class L(string cat) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                => Console.WriteLine($"LOG [{logLevel}] {cat}: {formatter(state, exception)}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T03d_LoggingCustom()
    {
        LogSystem.Initialize(b => b.AddProvider(new ConsoleWriteLineProvider()));
        var cat = LogCategory.Make("SmokeCustom");
        cat.Info($"hello from Brutal.Logging via a custom ILoggerProvider (Console.WriteLine)");
        return "LogSystem.Initialize(custom provider) ok; IsEnabled=" + LogSystem.IsEnabled + " (expect a 'LOG [Information] SmokeCustom' line above)";
    }

    // ------------------------------------------------------------------ T04 Brutal.Memory

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T04_Memory()
    {
        const int N = 1 << 20;
        // Brutal.Memory: Span<T> Mem.AllocZeroed<T>(ElementCount) / Mem.Free<T>(Span<T>)
        Span<byte> s = Mem.AllocZeroed<byte>(N);
        byte* sp = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(s));
        bool zeroed = s.IndexOfAnyExcept((byte)0) < 0;
        for (int i = 0; i < N; i++) s[i] = (byte)(i * 31);
        long sum = 0;
        for (int i = 0; i < N; i++) sum += s[i];
        Mem.Free(s);

        // Brutal.Memory: Ptr Mem.Alloc(ByteSize) / Mem.Free(Ptr)
        Ptr p = Mem.Alloc((ByteSize)N);
        void* pv = p;
        new Span<byte>(pv, N).Fill(0xAB);
        byte last = ((byte*)pv)[N - 1];
        Mem.Free(p);

        // Ptr<T> AllocAndCopy
        var pt = Mem.AllocAndCopy(new float4(1, 2, 3, 4));
        float4* pf = pt; float w = pf->W;
        Mem.Free(pt);

        void* nm = NativeMemory.Alloc(N);
        ((byte*)nm)[N - 1] = 7;
        byte nmv = ((byte*)nm)[N - 1];
        NativeMemory.Free(nm);

        void* al = NativeMemory.AlignedAlloc(4096, 256);
        bool aligned = ((nint)al & 255) == 0;
        NativeMemory.AlignedFree(al);

        nint h = Marshal.AllocHGlobal(N);
        Marshal.WriteInt32(h, N - 4, 0x1234567);
        int hv = Marshal.ReadInt32(h, N - 4);
        Marshal.FreeHGlobal(h);

        return $"Mem.AllocZeroed<byte>(1MB) at 0x{(nint)sp:X} zeroed={zeroed} sum={sum}; Mem.Alloc(1MB) at 0x{(nint)pv:X} last=0x{last:X}; Mem.AllocAndCopy(float4).W={w}; NativeMemory.Alloc ok v={nmv}; AlignedAlloc(256) aligned={aligned}; AllocHGlobal ok v=0x{hv:X}";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T04b_MemoryGrowth()
    {
        // Linear memory growth: allocate 256 MB natively, touch every page, free.
        nuint n = 256u << 20;
        var sw = Stopwatch.StartNew();
        byte* p = (byte*)NativeMemory.Alloc(n);
        for (nuint i = 0; i < n; i += 65536) p[i] = 1;
        NativeMemory.Free(p);
        return $"256MB NativeMemory.Alloc at 0x{(nint)p:X} touched+freed in {sw.Elapsed.TotalMilliseconds:F1} ms";
    }

    // ------------------------------------------------------------------ T05 Brutal.Vulkan static class

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T05_VulkanStaticCtor()
    {
        try
        {
            var gipa = Vulkan.GetInstanceProcAddr; // triggers static Vulkan() -> NativeLibrary.Load ... -> throws
            return "Vulkan.GetInstanceProcAddr.Invoke=0x" + ((nint)gipa.Invoke).ToString("X");
        }
        finally
        {
            Console.WriteLine("INFO T05 second access to Vulkan.GetInstanceProcAddr: " + Probe(() => "ok 0x" + ((nint)Vulkan.GetInstanceProcAddr.Invoke).ToString("X")));
            Console.WriteLine("INFO T05 access to Vulkan.Log (static field of same class): " + Probe(() => "ok Log=" + Vulkan.Log?.Name));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T05b_VulkanTypes()
    {
        var inst = new VkInstance(0x1234);
        var bci = new VkBufferCreateInfo
        {
            Size = (ByteSize64)65536,
            Usage = VkBufferUsageFlags.VertexBufferBit,
            SharingMode = VkSharingMode.Exclusive,
        };
        int offQfi = (int)((byte*)&bci.QueueFamilyIndices - (byte*)&bci);
        int offNext = (int)((byte*)&bci.Next - (byte*)&bci);
        int sType = *(int*)&bci;
        var parts = new[]
        {
            $"VkInstance(0x1234).VkHandle=0x{inst.VkHandle:X} IsNull={inst.IsNull()} NullHandle.IsNull={VkInstance.NullHandle.IsNull()}",
            $"Unsafe.SizeOf<VkBufferCreateInfo>={Unsafe.SizeOf<VkBufferCreateInfo>()} (expect 56)",
            $"offsetof(Next)={offNext} offsetof(QueueFamilyIndices)={offQfi} (expect 8/48: explicit x64 layout kept on wasm32)",
            $"bci.sType={sType} (BufferCreateInfo=12) Size={bci.Size.Value} Usage={bci.Usage}",
            $"sizeof(VkBuffer)={sizeof(VkBuffer)} (expect 4 on wasm32; C ABI non-dispatchable handle is 8)",
            $"sizeof(VkInstance)={sizeof(VkInstance)} sizeof(PfnVkVoidFunction)={sizeof(PfnVkVoidFunction)}",
            $"Unsafe.SizeOf<VkInstanceCreateInfo>={Unsafe.SizeOf<VkInstanceCreateInfo>()} Unsafe.SizeOf<VkPhysicalDeviceProperties>={Unsafe.SizeOf<VkPhysicalDeviceProperties>()}",
            $"Marshal.SizeOf<VkBufferCreateInfo>=" + Probe(() => Marshal.SizeOf<VkBufferCreateInfo>().ToString()),
            $"VK.MAX_EXTENSION_NAME_SIZE={VK.MAX_EXTENSION_NAME_SIZE} VK.WHOLE_SIZE as ByteSize64=0x" + Probe(() => ((ByteSize64)VK.WHOLE_SIZE).Value.ToString("X")) + " (C VK_WHOLE_SIZE=~0ULL=0xFFFFFFFFFFFFFFFF)",
            $"VkResult.Success={(int)VkResult.Success} VkResult.ErrorDeviceLost={(int)VkResult.ErrorDeviceLost}",
        };
        return string.Join("; ", parts);
    }

    // ------------------------------------------------------------------ T06 NativeLibrary / DllImport

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T06_NativeLibrary()
    {
        var parts = new System.Collections.Generic.List<string>();
        parts.Add("TryLoad(vulkan)=" + Probe(() => NativeLibrary.TryLoad("vulkan", out var h) + " h=0x" + h.ToString("X")));
        parts.Add("TryLoad(libvulkan.so.1)=" + Probe(() => NativeLibrary.TryLoad("libvulkan.so.1", out var h) + " h=0x" + h.ToString("X")));
        parts.Add("TryLoad(__Internal)=" + Probe(() => NativeLibrary.TryLoad("__Internal", out var h) + " h=0x" + h.ToString("X")));
        parts.Add("TryLoad(libSystem.Native)=" + Probe(() => NativeLibrary.TryLoad("libSystem.Native", out var h) + " h=0x" + h.ToString("X")));
        parts.Add("Load(vulkan, asm, LegacyBehavior)=" + Probe(() => "0x" + NativeLibrary.Load("vulkan", typeof(VkInstance).Assembly, DllImportSearchPath.LegacyBehavior).ToString("X")));
        nint main = 0;
        parts.Add("GetMainProgramHandle()=" + Probe(() => { main = NativeLibrary.GetMainProgramHandle(); return "0x" + main.ToString("X"); }));
        parts.Add("TryGetExport(main,malloc)=" + Probe(() => NativeLibrary.TryGetExport(main, "malloc", out var a) + " a=0x" + a.ToString("X")));
        parts.Add("TryGetExport(main,mono_wasm_load_runtime)=" + Probe(() => NativeLibrary.TryGetExport(main, "mono_wasm_load_runtime", out var a) + " a=0x" + a.ToString("X")));
        parts.Add("TryGetExport(main,vkGetInstanceProcAddr)=" + Probe(() => NativeLibrary.TryGetExport(main, "vkGetInstanceProcAddr", out var a) + " a=0x" + a.ToString("X")));
        return string.Join("; ", parts);
    }

    [DllImport("vulkan")]
    static extern int vkCreateInstance(nint pCreateInfo, nint pAllocator, nint pInstance);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T06b_DllImportVulkan()
    {
        int r = vkCreateInstance(0, 0, 0);
        return "returned " + r;
    }

    static int s_resolverCalls;
    static string s_resolverLog = "";

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T06c_BrutalPInvoke()
    {
        // BRUTAL's 4 global commands are [LibraryImport("vulkan")] P/Invokes (Vulkan.PInvoke nested class).
        // Calling them does not run the outer Vulkan static ctor, so no DllImportResolver is registered yet.
        try
        {
            int v = 0;
            var r = Vulkan.PInvoke.vkEnumerateInstanceVersion(&v);
            return "ok r=" + r + " v=" + v;
        }
        finally
        {
            // Register our own resolver on Brutal.Vulkan (possible because Vulkan's ctor failed before its SetDllImportResolver call)
            string setRes = Probe(() =>
            {
                NativeLibrary.SetDllImportResolver(typeof(VkInstance).Assembly, (name, asm, path) =>
                {
                    s_resolverCalls++;
                    s_resolverLog += name + ";";
                    // return a handle that is valid on wasm: the static pinvoke table of libSystem.Native
                    return NativeLibrary.TryLoad("libSystem.Native", out var h) ? h : 0;
                });
                return "ok";
            });
            Console.WriteLine("INFO T06c SetDllImportResolver(Brutal.Vulkan, ...) -> " + setRes);
            string after = Probe(() => { int v = 0; var r = Vulkan.PInvoke.vkEnumerateInstanceVersion(&v); return "ok r=" + r + " v=" + v; });
            Console.WriteLine($"INFO T06c vkEnumerateInstanceVersion after resolver -> {after}; resolverCalls={s_resolverCalls} names=[{s_resolverLog}]");
        }
    }

    [DllImport("libSystem.Native", EntryPoint = "SystemNative_GetPid")]
    static extern int SystemNative_GetPid();

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T06d_DllImportKnown()
    {
        return "SystemNative_GetPid()=" + SystemNative_GetPid() + " Environment.ProcessId=" + Probe(() => Environment.ProcessId.ToString());
    }

    // ------------------------------------------------------------------ T07 basic UnmanagedCallersOnly fnptr

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static int AddUco(int a, int b) => a + b;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T07a_FnPtrValue()
    {
        delegate* unmanaged[Cdecl]<int, int, int> f = &AddUco;
        delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction> g = &MyGetInstanceProcAddr;
        delegate* unmanaged[Cdecl]<nint, int, long, float, double, int*, long> m = &Mixed;
        return $"&AddUco=0x{(nint)f:X} &MyGetInstanceProcAddr=0x{(nint)g:X} &Mixed=0x{(nint)m:X} (wasm function-table indices; 0 would mean no native-to-interp thunk)";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T07_FnPtrBasic()
    {
        delegate* unmanaged[Cdecl]<int, int, int> f = &AddUco;
        Console.WriteLine($"INFO T07 calling &AddUco=0x{(nint)f:X} ...");
        int r = f(2, 3);
        if (r != 5) throw new Exception("f(2,3) returned " + r);
        return $"&AddUco=0x{(nint)f:X} f(2,3)={r}";
    }

    // ------------------------------------------------------------------ T08 Vulkan-shaped managed ICD

    const nint FakeInstanceHandle = 0x00C0FFEE;
    const nint FakePhysicalDevice = 0x0000BEEF;
    static int s_gipaCalls;

    static bool NameIs(byte* name, ReadOnlySpan<byte> expected)
        => MemoryMarshal.CreateReadOnlySpanFromNullTerminated(name).SequenceEqual(expected);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static PfnVkVoidFunction MyGetInstanceProcAddr(VkInstance instance, byte* pName)
    {
        s_gipaCalls++;
        if (NameIs(pName, "vkCreateInstance"u8))
            return new PfnVkVoidFunction((delegate* unmanaged[Cdecl]<void>)(delegate* unmanaged[Cdecl]<VkInstanceCreateInfo*, VkAllocationCallbacks*, VkInstance*, VkResult>)&MyCreateInstance);
        if (NameIs(pName, "vkEnumeratePhysicalDevices"u8))
            return new PfnVkVoidFunction((delegate* unmanaged[Cdecl]<void>)(delegate* unmanaged[Cdecl]<VkInstance, int*, VkPhysicalDevice*, VkResult>)&MyEnumeratePhysicalDevices);
        if (NameIs(pName, "vkGetPhysicalDeviceProperties"u8))
            return new PfnVkVoidFunction((delegate* unmanaged[Cdecl]<void>)(delegate* unmanaged[Cdecl]<VkPhysicalDevice, VkPhysicalDeviceProperties*, void>)&MyGetPhysicalDeviceProperties);
        return default;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static VkResult MyCreateInstance(VkInstanceCreateInfo* ci, VkAllocationCallbacks* alloc, VkInstance* outInstance)
    {
        if (ci == null || outInstance == null) return VkResult.ErrorInitializationFailed;
        if (ci->EnabledExtensionCount != 2) return VkResult.ErrorExtensionNotPresent;
        *outInstance = new VkInstance(FakeInstanceHandle);
        return VkResult.Success;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static VkResult MyEnumeratePhysicalDevices(VkInstance instance, int* pCount, VkPhysicalDevice* pDevices)
    {
        if (instance.VkHandle != FakeInstanceHandle) return VkResult.ErrorInitializationFailed;
        if (pDevices == null) { *pCount = 1; return VkResult.Success; }
        pDevices[0] = new VkPhysicalDevice(FakePhysicalDevice);
        *pCount = 1;
        return VkResult.Success;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void MyGetPhysicalDeviceProperties(VkPhysicalDevice pd, VkPhysicalDeviceProperties* props)
    {
        props->ApiVersion = (1 << 22) | (3 << 12);
        props->VendorID = 0x10DE;
        props->DeviceType = VkPhysicalDeviceType.VirtualGpu;
        var name = "WebGPU (managed ICD)"u8;
        for (int i = 0; i < name.Length; i++) props->DeviceName[i] = name[i];
        props->DeviceName[name.Length] = 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T08_FnPtrVulkanShape()
    {
        // Wrap our C# UnmanagedCallersOnly gipa in BRUTAL's own function-pointer struct, WITHOUT touching static class Vulkan.
        var gipa = new VkGetInstanceProcAddr((delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction>)&MyGetInstanceProcAddr);
        Console.WriteLine($"INFO T08 calling gipa.Invoke=0x{(nint)gipa.Invoke:X} ...");
        PfnVkVoidFunction pfn = gipa.Invoke(default, "vkCreateInstance"u8.AsPointer());
        if (pfn.Invoke == null) throw new Exception("gipa returned null for vkCreateInstance");
        // BRUTAL has no VkCreateInstance fnptr struct (vkCreateInstance is a [LibraryImport]); use the raw fnptr type
        var createInstance = (delegate* unmanaged[Cdecl]<VkInstanceCreateInfo*, VkAllocationCallbacks*, VkInstance*, VkResult>)pfn.Invoke;

        byte** exts = stackalloc byte*[2];
        exts[0] = "VK_KHR_surface"u8.AsPointer();
        exts[1] = "VK_EXT_web_surface"u8.AsPointer();
        var ci = new VkInstanceCreateInfo { EnabledExtensionCount = 2, ppEnabledExtensionNames = exts };
        VkInstance outInst = default;
        VkResult res = createInstance(&ci, null, &outInst);
        if (res != VkResult.Success || outInst.VkHandle != FakeInstanceHandle)
            throw new Exception($"round trip failed res={res} handle=0x{outInst.VkHandle:X}");

        // Use a BRUTAL generated explicit operator (PfnVkVoidFunction -> VkEnumeratePhysicalDevices)
        var enumPd = (VkEnumeratePhysicalDevices)gipa.Invoke(outInst, "vkEnumeratePhysicalDevices"u8.AsPointer());
        int count = 0;
        var r1 = enumPd.Invoke(outInst, &count, null);
        VkPhysicalDevice* pds = stackalloc VkPhysicalDevice[count];
        var r2 = enumPd.Invoke(outInst, &count, pds);

        var getProps = (VkGetPhysicalDeviceProperties)gipa.Invoke(outInst, "vkGetPhysicalDeviceProperties"u8.AsPointer());
        VkPhysicalDeviceProperties props = default;
        getProps.Invoke(pds[0], &props);
        string devName = Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(props.DeviceName));

        return $"vkCreateInstance via gipa -> {res}, handle=0x{outInst.VkHandle:X}; vkEnumeratePhysicalDevices -> {r1}/{r2} count={count} pd=0x{pds[0].VkHandle:X}; vkGetPhysicalDeviceProperties -> name='{devName}' vendor=0x{props.VendorID:X} type={props.DeviceType} api={props.ApiVersion >> 22}.{(props.ApiVersion >> 12) & 0x3FF}; gipaCalls={s_gipaCalls}";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T08b_FunctionTableManaged()
    {
        // BRUTAL's generated VkInstance.FunctionTable(VkInstance, VkGetInstanceProcAddr) resolves every instance-level
        // entry point through the supplied gipa: feed it our managed ICD.
        var gipa = new VkGetInstanceProcAddr((delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction>)&MyGetInstanceProcAddr);
        int before = s_gipaCalls;
        var table = new VkInstance.FunctionTable(new VkInstance(FakeInstanceHandle), gipa);
        int lookups = s_gipaCalls - before;
        int count = 0;
        var r = table.vkEnumeratePhysicalDevices.Invoke(new VkInstance(FakeInstanceHandle), &count, null);
        return $"FunctionTable ctor made {lookups} gipa lookups; sizeof(FunctionTable)={sizeof(VkInstance.FunctionTable)}; table.vkEnumeratePhysicalDevices -> {r} count={count}; table.vkDestroyInstance null={table.vkDestroyInstance.Invoke == null}";
    }

    // ---- JS-implemented function pointers (Emscripten Module.addFunction) ----

    [JSImport("makeJsAdder", "main.js")]
    internal static partial int MakeJsAdder();

    [JSImport("makeJsGetProcAddr", "main.js")]
    internal static partial int MakeJsGetProcAddr();

    [JSImport("jsIcdStats", "main.js")]
    internal static partial string JsIcdStats();

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T08c_JsAddFunction()
    {
        int idx = MakeJsAdder(); // wasm table index of a JS function (a, b) => a + b * 100, signature 'iii'
        var f = (delegate* unmanaged[Cdecl]<int, int, int>)(nint)idx;
        int r = f(2, 3);
        if (r != 302) throw new Exception($"JS fnptr idx={idx} returned {r}, expected 302");
        return $"Module.addFunction -> table index {idx}; C# calli unmanaged[Cdecl] f(2,3)={r}";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T08d_FunctionTableJs()
    {
        int idx = MakeJsGetProcAddr();
        var gipa = new VkGetInstanceProcAddr((delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction>)(nint)idx);
        var inst = new VkInstance(FakeInstanceHandle);
        var table = new VkInstance.FunctionTable(inst, gipa);
        int count = 0;
        var r1 = table.vkEnumeratePhysicalDevices.Invoke(inst, &count, null);
        VkPhysicalDevice* pds = stackalloc VkPhysicalDevice[Math.Max(count, 1)];
        var r2 = table.vkEnumeratePhysicalDevices.Invoke(inst, &count, pds);
        return $"JS gipa idx={idx}; BRUTAL VkInstance.FunctionTable filled from JS; vkEnumeratePhysicalDevices (JS) -> {r1}/{r2} count={count} pd=0x{pds[0].VkHandle:X}; {JsIcdStats()}";
    }

    // ---- managed ICD reached through a JS trampoline: C# calli -> wasm table entry (JS, addFunction) -> [JSExport] C# ----

    [JSImport("makeTrampolineGipa", "main.js")]
    internal static partial int MakeTrampolineGipa();

    [JSImport("trampolineEnumPdIndex", "main.js")]
    internal static partial int TrampolineEnumPdIndex();

    [JSImport("makeTrampolineMixed", "main.js")]
    internal static partial int MakeTrampolineMixed();

    [JSImport("makeTrampolineAdd", "main.js")]
    internal static partial int MakeTrampolineAdd();

    static int s_jsTrampolineEnumPd; // table index of a JS trampoline for vkEnumeratePhysicalDevices

    // Called from the JS gipa trampoline. Pointers arrive as JS numbers (wasm32 addresses).
    [JSExport]
    internal static int ManagedGetInstanceProcAddr(int instance, int pName)
    {
        s_gipaCalls++;
        if (NameIs((byte*)(nint)pName, "vkEnumeratePhysicalDevices"u8)) return s_jsTrampolineEnumPd;
        return 0;
    }

    [JSExport]
    internal static int ManagedEnumeratePhysicalDevices(int instance, int pCount, int pDevices)
    {
        if (instance != (int)FakeInstanceHandle) return (int)VkResult.ErrorInitializationFailed;
        *(int*)(nint)pCount = 1;
        if (pDevices != 0) ((VkPhysicalDevice*)(nint)pDevices)[0] = new VkPhysicalDevice(FakePhysicalDevice);
        return (int)VkResult.Success;
    }

    [JSExport]
    internal static int ManagedAdd(int a, int b) => a + b;

    // (nint a, int b, long c, float d, double e, int* f) -> long ; i64 crosses JS as BigInt, so JS converts it
    [JSExport]
    [return: JSMarshalAs<JSType.Number>]
    internal static long ManagedMixed(int a, int b, [JSMarshalAs<JSType.Number>] long c, float d, double e, int f)
    {
        s_a = a; s_b = b; s_c = c; s_d = d; s_e = e; s_f = *(int*)(nint)f;
        return a + b + c + (long)(d * 10) + (long)(e * 100) + s_f;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T08e_JsTrampolineToManaged()
    {
        int idx = MakeTrampolineGipa();
        s_jsTrampolineEnumPd = TrampolineEnumPdIndex();
        var gipa = new VkGetInstanceProcAddr((delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction>)(nint)idx);
        var inst = new VkInstance(FakeInstanceHandle);
        int before = s_gipaCalls;
        var table = new VkInstance.FunctionTable(inst, gipa);
        int lookups = s_gipaCalls - before;
        int count = 0;
        var r1 = table.vkEnumeratePhysicalDevices.Invoke(inst, &count, null);
        VkPhysicalDevice* pds = stackalloc VkPhysicalDevice[Math.Max(count, 1)];
        var r2 = table.vkEnumeratePhysicalDevices.Invoke(inst, &count, pds);
        if (r2 != VkResult.Success || count != 1 || pds[0].VkHandle != FakePhysicalDevice) throw new Exception($"r1={r1} r2={r2} count={count} pd=0x{pds[0].VkHandle:X}");
        return $"gipa trampoline idx={idx}, enumPD trampoline idx={s_jsTrampolineEnumPd}; BRUTAL FunctionTable ctor -> {lookups} managed gipa lookups (re-entrant C#->JS->C#); vkEnumeratePhysicalDevices -> {r1}/{r2} count={count} pd=0x{pds[0].VkHandle:X}";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T09b_MixedArgsJs()
    {
        var fp = (delegate* unmanaged[Cdecl]<nint, int, long, float, double, int*, long>)(nint)MakeTrampolineMixed();
        int fortyTwo = 42;
        long r = fp((nint)0x12345678, -7, 0x1_0000_0001L, 1.5f, 2.25, &fortyTwo);
        long expected = 0x12345678 + -7 + 0x1_0000_0001L + 15 + 225 + 42;
        bool ok = r == expected && s_a == 0x12345678 && s_b == -7 && s_c == 0x1_0000_0001L && s_d == 1.5f && s_e == 2.25 && s_f == 42;
        if (!ok) throw new Exception($"r={r} expected={expected} a=0x{s_a:X} b={s_b} c=0x{s_c:X} d={s_d} e={s_e} f={s_f}");
        return $"C# calli (nint,int,long,float,double,int*)->long through JS trampoline (sig 'jiijfdi') into [JSExport]: r={r} (0x{r:X}); all 6 args round-tripped";
    }

    // ------------------------------------------------------------------ T09 mixed-arg fnptr

    static nint s_a; static int s_b; static long s_c; static float s_d; static double s_e; static int s_f;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static long Mixed(nint a, int b, long c, float d, double e, int* f)
    {
        s_a = a; s_b = b; s_c = c; s_d = d; s_e = e; s_f = *f;
        return a + b + c + (long)(d * 10) + (long)(e * 100) + *f;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T09_MixedArgs()
    {
        delegate* unmanaged[Cdecl]<nint, int, long, float, double, int*, long> fp = &Mixed;
        int fortyTwo = 42;
        long r = fp((nint)0x12345678, -7, 0x1_0000_0001L, 1.5f, 2.25, &fortyTwo);
        long expected = 0x12345678 + -7 + 0x1_0000_0001L + 15 + 225 + 42;
        bool ok = r == expected && s_a == 0x12345678 && s_b == -7 && s_c == 0x1_0000_0001L && s_d == 1.5f && s_e == 2.25 && s_f == 42;
        if (!ok) throw new Exception($"r={r} expected={expected} a=0x{s_a:X} b={s_b} c=0x{s_c:X} d={s_d} e={s_e} f={s_f}");
        return $"r={r} (0x{r:X}) all 6 args round-tripped (a=0x{s_a:X} b={s_b} c=0x{s_c:X} d={s_d} e={s_e} *f={s_f})";
    }

    // ------------------------------------------------------------------ T10 delegate marshal

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int BinOp(int a, int b);

    static BinOp s_keepAlive;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string T10_DelegateMarshal()
    {
        s_keepAlive = (a, b) => a * b;
        nint p = Marshal.GetFunctionPointerForDelegate(s_keepAlive);
        var f = (delegate* unmanaged[Cdecl]<int, int, int>)p;
        int r = f(6, 7);
        if (r != 42) throw new Exception("returned " + r);
        return $"GetFunctionPointerForDelegate -> 0x{p:X}; f(6,7)={r}";
    }

    // ------------------------------------------------------------------ T11 JSImport

    [JSImport("hasWebGpu", "main.js")]
    internal static partial bool HasWebGpu();

    [JSImport("sumBytes", "main.js")]
    internal static partial int SumBytes([JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    [JSImport("sumPtr", "main.js")]
    internal static partial int SumPtr([JSMarshalAs<JSType.Number>] nint ptr, int len);

    [JSImport("heapApiSurvey", "main.js")]
    internal static partial string HeapApiSurvey();

    [JSImport("webGpuAdapterInfo", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> WebGpuAdapterInfo();

    [JSImport("identity", "main.js")]
    internal static partial int JsIdentity(int x);

    [JSImport("spanLength", "main.js")]
    internal static partial int JsSpanLength([JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    [JSImport("spanSumCopy", "main.js")]
    internal static partial int JsSpanSumCopy([JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    static string T11a_HasWebGpu() => "navigator.gpu present=" + HasWebGpu();

    static string T11b_SpanMemoryView()
    {
        Span<byte> data = stackalloc byte[256];
        int expected = 0;
        for (int i = 0; i < data.Length; i++) { data[i] = (byte)i; expected += (byte)i; }
        int got = SumBytes(data);
        if (got != expected) throw new Exception($"got {got} expected {expected}");
        return $"SumBytes(Span<byte>[256]) = {got} (expected {expected})";
    }

    static string T11c_RawPointer()
    {
        const int len = 1000;
        byte* p = (byte*)NativeMemory.Alloc(len);
        int expected = 0;
        for (int i = 0; i < len; i++) { p[i] = (byte)(i * 7); expected += (byte)(i * 7); }
        int got = SumPtr((nint)p, len);
        NativeMemory.Free(p);
        if (got != expected) throw new Exception($"got {got} expected {expected}");
        return $"SumPtr(0x{(nint)p:X}, {len}) via runtime.localHeapViewU8() = {got} (expected {expected})";
    }

    static string T11d_HeapApiSurvey() => HeapApiSurvey();

    // ------------------------------------------------------------------ T12 JSExport tick (driven from JS)

    static int s_ticks;
    static double s_firstTs, s_lastTs;

    [JSExport]
    internal static void Tick(double timestampMs)
    {
        if (s_ticks == 0) s_firstTs = timestampMs;
        s_lastTs = timestampMs;
        s_ticks++;
    }

    [JSExport]
    internal static void ReportTicks()
    {
        bool ok = s_ticks == 10 && s_lastTs > s_firstTs;
        Console.WriteLine($"TEST T12 jsexport-tick: {(ok ? "PASS" : "FAIL")} ticks={s_ticks} first={s_firstTs:F1}ms last={s_lastTs:F1}ms span={(s_lastTs - s_firstTs):F1}ms (~{(s_ticks > 1 ? (s_lastTs - s_firstTs) / (s_ticks - 1) : 0):F2} ms/frame); runtime alive after Main returned");
    }

    [JSExport]
    internal static int Noop(int x) => x + 1;

    // ------------------------------------------------------------------ T13 perf

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int AddDirect(int a, int b) => a + b;

    static string Time(string label, int iters, Func<int, long> body)
    {
        body(Math.Min(iters, 20000)); // warm up (interpreter tiering)
        var sw = Stopwatch.StartNew();
        long acc = body(iters);
        sw.Stop();
        double ns = sw.Elapsed.TotalMilliseconds * 1e6 / iters;
        return $"{label}: {ns:F1} ns/call (total {sw.Elapsed.TotalMilliseconds:F0} ms, acc={acc})";
    }

    const int N = 1_000_000;

    static string T13a_Direct() => Time("direct static call", N, n => { long s = 0; for (int i = 0; i < n; i++) s += AddDirect(i, 1); return s; });

    static string T13b_FnPtrUco()
    {
        delegate* unmanaged[Cdecl]<int, int, int> f = &AddUco;
        return Time("delegate* unmanaged[Cdecl] -> C# UCO", N, n => { long s = 0; for (int i = 0; i < n; i++) s += f(i, 1); return s; });
    }

    // Managed function pointer (no unmanaged transition): stays inside the interpreter, needs no trampolines.
    static string T13f_ManagedFnPtr()
    {
        delegate*<int, int, int> f = &AddDirect;
        int check = f(2, 3);
        if (check != 5) throw new Exception("f(2,3)=" + check);
        return Time("delegate*<int,int,int> (managed calli) -> C# static", N, n => { long s = 0; for (int i = 0; i < n; i++) s += f(i, 1); return s; });
    }

    static string T13b2_FnPtrJs()
    {
        var f = (delegate* unmanaged[Cdecl]<int, int, int>)(nint)MakeJsAdder();
        return Time("delegate* unmanaged[Cdecl] -> JS addFunction (a+b*100)", N, n => { long s = 0; for (int i = 0; i < n; i++) s += f(i, 1); return s; });
    }

    static string T13b3_FnPtrJsTrampoline()
    {
        var f = (delegate* unmanaged[Cdecl]<int, int, int>)(nint)MakeTrampolineAdd();
        return Time("delegate* unmanaged[Cdecl] -> JS trampoline -> [JSExport] C#", N, n => { long s = 0; for (int i = 0; i < n; i++) s += f(i, 1); return s; });
    }

    static string T13c_JsImportInt() => Time("JSImport int->int", N, n => { long s = 0; for (int i = 0; i < n; i++) s += JsIdentity(i); return s; });

    static string T13d_JsImportSpan() => Time("JSImport Span<byte>[64] as MemoryView (JS reads .byteLength)", N, n =>
    {
        Span<byte> buf = stackalloc byte[64];
        long s = 0; for (int i = 0; i < n; i++) s += JsSpanLength(buf); return s;
    });

    static string T13d2_JsImportSpanCopy() => Time("JSImport Span<byte>[64] as MemoryView (JS .slice() copy + sum), 100k iters", N / 10, n =>
    {
        Span<byte> buf = stackalloc byte[64];
        buf.Fill(1);
        long s = 0; for (int i = 0; i < n; i++) s += JsSpanSumCopy(buf); return s;
    });
}
