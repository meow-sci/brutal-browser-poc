using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using WebGpuTriangle;

Console.WriteLine($"dotnet Main: {RuntimeInformation.FrameworkDescription} {RuntimeInformation.RuntimeIdentifier} " +
                  $"IsDynamicCodeCompiled={RuntimeFeature.IsDynamicCodeCompiled} IsDynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported} " +
                  $"Stopwatch.Frequency={Stopwatch.Frequency} IsHighResolution={Stopwatch.IsHighResolution}");
// Main returns; main.js drives App.Init / App.Frame via [JSExport].

public static unsafe partial class App
{
    const string Wgsl = """
        struct U { m: mat4x4f };
        @group(0) @binding(0) var<uniform> u: U;
        struct VOut { @builtin(position) pos: vec4f, @location(0) color: vec3f };
        @vertex fn vs(@location(0) p: vec2f, @location(1) c: vec3f) -> VOut {
            var o: VOut;
            o.pos = u.m * vec4f(p, 0.0, 1.0);
            o.color = c;
            return o;
        }
        @fragment fn fs(i: VOut) -> @location(0) vec4f { return vec4f(i.color * K, 1.0); }
        """;

    // vertex data: position xy + color rgb, 3 vertices (centroid at origin -> center pixel = avg colour)
    static readonly float[] Vertices =
    {
         0.0f,       0.8f,  1f, 0f, 0f,
        -0.69282f,  -0.4f,  0f, 1f, 0f,
         0.69282f,  -0.4f,  0f, 0f, 1f,
    };

    public const float ClearR = 0.1f, ClearG = 0.2f, ClearB = 0.3f, ClearA = 1f;
    const int ManyDrawsMax = 1000;
    const int DynStride = 256; // minUniformBufferOffsetAlignment

    static int s_format, s_vbo, s_ubo, s_uboMany, s_bglStatic, s_bglDyn, s_bg, s_bgMany, s_shader, s_pipeline, s_pipelineDyn;
    static float* s_matrix;       // 64 bytes, NativeMemory (stable address for writeBuffer)
    static float* s_manyMatrices; // ManyDrawsMax * 256 bytes
    static CommandStream s_stream;
    static float s_aspect;

    // K is an integer-valued f32 (exact up to 2^24). Each distinct K yields distinct generated MSL, which defeats
    // Dawn's object dedup, Chrome's in-session caches and (apparently) the cross-process macOS Metal shader cache.
    static string ShaderSource(int k) => "const K: f32 = " + k.ToString(CultureInfo.InvariantCulture) + ".0;\n" + Wgsl;

    static string PipelineJson(int shader, int bgl, int format) =>
        "{\"layout\":[" + bgl + "]," +
        "\"vertex\":{\"module\":" + shader + ",\"entryPoint\":\"vs\",\"buffers\":[{\"arrayStride\":20,\"stepMode\":\"vertex\"," +
        "\"attributes\":[{\"format\":\"float32x2\",\"offset\":0,\"shaderLocation\":0},{\"format\":\"float32x3\",\"offset\":8,\"shaderLocation\":1}]}]}," +
        "\"fragment\":{\"module\":" + shader + ",\"entryPoint\":\"fs\",\"targets\":[{\"format\":" + format + "}]}," +
        "\"primitive\":{\"topology\":\"triangle-list\",\"cullMode\":\"none\"}}";

    [JSExport]
    public static string Init()
    {
        var sw = Stopwatch.StartNew();
        s_format = WebGpu.GetPreferredFormat();
        s_aspect = (float)WebGpu.GetCanvasHeight() / WebGpu.GetCanvasWidth();
        int device = WebGpu.GetDevice(); // handles exist mostly to show the table; not used further
        int queue = WebGpu.GetQueue();

        // vertex buffer: mappedAtCreation + copy from unmanaged memory + unmap
        int vbBytes = Vertices.Length * sizeof(float);
        s_vbo = WebGpu.CreateBuffer(vbBytes, (int)(BufferUsage.Vertex | BufferUsage.CopyDst), mappedAtCreation: true);
        fixed (float* pv = Vertices) WebGpu.CopyToMappedRange(s_vbo, 0, (nint)pv, vbBytes);
        WebGpu.UnmapBuffer(s_vbo);

        // uniform buffers
        s_ubo = WebGpu.CreateBuffer(64, (int)(BufferUsage.Uniform | BufferUsage.CopyDst), false);
        s_matrix = (float*)NativeMemory.AlignedAlloc(64, 16);
        WriteRotation(s_matrix, 0f, 1f, 0f, 0f);
        WebGpu.WriteBuffer(s_ubo, 0, (nint)s_matrix, 64);

        s_uboMany = WebGpu.CreateBuffer(ManyDrawsMax * DynStride, (int)(BufferUsage.Uniform | BufferUsage.CopyDst), false);
        s_manyMatrices = (float*)NativeMemory.AlignedAlloc((nuint)(ManyDrawsMax * DynStride), 16);
        for (int i = 0; i < ManyDrawsMax; i++)
        {
            int col = i % 40, row = i / 40;
            float cx = -1f + (2f / 40f) * (col + 0.5f);
            float cy = -1f + (2f / 25f) * (row + 0.5f);
            WriteRotation((float*)((byte*)s_manyMatrices + i * DynStride), i * 0.1f, 0.045f, cx, cy);
        }
        WebGpu.WriteBuffer(s_uboMany, 0, (nint)s_manyMatrices, ManyDrawsMax * DynStride);

        // shader, layouts, bind groups, pipelines
        s_shader = WebGpu.CreateShaderModule(ShaderSource(1));
        s_bglStatic = WebGpu.CreateBindGroupLayoutUniform((int)ShaderStage.Vertex, false, 64);
        s_bglDyn = WebGpu.CreateBindGroupLayoutUniform((int)ShaderStage.Vertex, true, 64);
        s_bg = WebGpu.CreateBindGroupUniform(s_bglStatic, s_ubo, 0, 64);
        s_bgMany = WebGpu.CreateBindGroupUniform(s_bglDyn, s_uboMany, 0, 64);
        s_pipeline = WebGpu.CreateRenderPipelineJson(PipelineJson(s_shader, s_bglStatic, s_format));
        s_pipelineDyn = WebGpu.CreateRenderPipelineJson(PipelineJson(s_shader, s_bglDyn, s_format));

        s_stream = new CommandStream(64 * 1024);
        return $"init ok in {sw.Elapsed.TotalMilliseconds:0.00} ms: format={(TextureFormat)s_format} device={device} queue={queue} " +
               $"vbo={s_vbo} ubo={s_ubo} uboMany={s_uboMany} shader={s_shader} pipeline={s_pipeline} pipelineDyn={s_pipelineDyn} " +
               $"bg={s_bg} bgMany={s_bgMany} matrixPtr=0x{(nint)s_matrix:x} streamPtr=0x{s_stream.Pointer:x}";
    }

    /// <summary>"vbo,ubo,pipeline,pipelineDyn,bg,bgMany,bglStatic" for JS-side baselines.</summary>
    [JSExport]
    public static string GetHandles() => $"{s_vbo},{s_ubo},{s_pipeline},{s_pipelineDyn},{s_bg},{s_bgMany},{s_bglStatic}";

    /// <summary>Column-major mat4: M = T(tx,ty) * S(scale*aspect, scale) * Rz(angle).</summary>
    static void WriteRotation(float* m, float angle, float scale, float tx, float ty)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        float ax = scale * s_aspect, ay = scale;
        m[0] = ax * c; m[1] = ay * s; m[2] = 0; m[3] = 0;
        m[4] = -ax * s; m[5] = ay * c; m[6] = 0; m[7] = 0;
        m[8] = 0; m[9] = 0; m[10] = 1; m[11] = 0;
        m[12] = tx; m[13] = ty; m[14] = 0; m[15] = 1;
    }

    // ------------------------------------------------------------------ frames

    /// <summary>One frame via individual [JSImport] calls (11 interop calls).</summary>
    [JSExport]
    public static void Frame(double tMs)
    {
        WriteRotation(s_matrix, (float)(tMs * 0.001), 1f, 0f, 0f);
        WebGpu.WriteBuffer(s_ubo, 0, (nint)s_matrix, 64);
        int enc = WebGpu.CreateCommandEncoder();
        int view = WebGpu.GetCurrentTextureView();
        int pass = WebGpu.BeginRenderPass(enc, view, ClearR, ClearG, ClearB, ClearA, (int)LoadOp.Clear, (int)StoreOp.Store);
        WebGpu.SetPipeline(pass, s_pipeline);
        WebGpu.SetVertexBuffer(pass, 0, s_vbo, 0);
        WebGpu.SetBindGroup(pass, 0, s_bg);
        WebGpu.Draw(pass, 3, 1, 0, 0);
        WebGpu.EndPass(pass);
        WebGpu.Submit(WebGpu.Finish(enc));
    }

    /// <summary>Same frame via the binary command stream (1 interop call). Returns command count.</summary>
    [JSExport]
    public static int FrameStream(double tMs)
    {
        WriteRotation(s_matrix, (float)(tMs * 0.001), 1f, 0f, 0f);
        var cs = s_stream;
        cs.WriteBuffer(s_ubo, 0, s_matrix, 64);
        cs.BeginPass(0, LoadOp.Clear, StoreOp.Store, ClearR, ClearG, ClearB, ClearA);
        cs.SetPipeline(s_pipeline);
        cs.SetVertexBuffer(0, s_vbo, 0);
        cs.SetBindGroup(0, s_bg);
        cs.Draw(3, 1, 0, 0);
        cs.EndPass();
        cs.Submit();
        return cs.Flush();
    }

    /// <summary>Frame with N draws (setBindGroup(dyn offset)+draw pairs). Returns C#-side elapsed ms.</summary>
    [JSExport]
    public static double FrameManyDraws(int draws, bool useStream)
    {
        long t0 = Stopwatch.GetTimestamp();
        if (!useStream)
        {
            int enc = WebGpu.CreateCommandEncoder();
            int view = WebGpu.GetCurrentTextureView();
            int pass = WebGpu.BeginRenderPass(enc, view, ClearR, ClearG, ClearB, ClearA, (int)LoadOp.Clear, (int)StoreOp.Store);
            WebGpu.SetPipeline(pass, s_pipelineDyn);
            WebGpu.SetVertexBuffer(pass, 0, s_vbo, 0);
            for (int i = 0; i < draws; i++)
            {
                WebGpu.SetBindGroupDyn(pass, 0, s_bgMany, i * DynStride);
                WebGpu.Draw(pass, 3, 1, 0, 0);
            }
            WebGpu.EndPass(pass);
            WebGpu.Submit(WebGpu.Finish(enc));
        }
        else
        {
            var cs = s_stream;
            cs.BeginPass(0, LoadOp.Clear, StoreOp.Store, ClearR, ClearG, ClearB, ClearA);
            cs.SetPipeline(s_pipelineDyn);
            cs.SetVertexBuffer(0, s_vbo, 0);
            for (int i = 0; i < draws; i++)
            {
                cs.SetBindGroup(0, s_bgMany, (uint)(i * DynStride));
                cs.Draw(3, 1, 0, 0);
            }
            cs.EndPass();
            cs.Submit();
            s_lastEncodeMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            cs.Flush();
        }
        return Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    static double s_lastEncodeMs;
    /// <summary>C#-side stream encode time of the last FrameManyDraws(useStream: true) (excludes replay).</summary>
    [JSExport] public static double LastStreamEncodeMs() => s_lastEncodeMs;

    // ------------------------------------------------------------------ benchmarks

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int ManagedNoop(int i) => i + 1;

    /// <summary>Baseline: N calls to a non-inlined managed method (interpreter cost only).</summary>
    [JSExport]
    public static double BenchManagedCall(int n)
    {
        long t0 = Stopwatch.GetTimestamp();
        int acc = 0;
        for (int i = 0; i < n; i++) acc += ManagedNoop(i);
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (acc == 42) Console.WriteLine("unlikely");
        return ms;
    }

    [JSExport]
    public static double BenchNoop(int n)
    {
        long t0 = Stopwatch.GetTimestamp();
        int acc = 0;
        for (int i = 0; i < n; i++) acc += WebGpu.Noop(i);
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (acc == 42) Console.WriteLine("unlikely");
        return ms;
    }

    [JSExport]
    public static double BenchNoop5(int n)
    {
        long t0 = Stopwatch.GetTimestamp();
        int acc = 0;
        for (int i = 0; i < n; i++) acc += WebGpu.Noop5(i, 1, 2, 3, 4);
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (acc == 42) Console.WriteLine("unlikely");
        return ms;
    }

    static JSObject s_deviceObj;
    [JSExport]
    public static double BenchNoopObj(int n)
    {
        s_deviceObj ??= WebGpu.GetDeviceObject();
        long t0 = Stopwatch.GetTimestamp();
        int acc = 0;
        for (int i = 0; i < n; i++) acc += WebGpu.NoopObj(s_deviceObj);
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (acc == 42) Console.WriteLine("unlikely");
        return ms;
    }

    /// <summary>N calls returning a JSObject: fresh=false returns the same (already proxied) GPUDevice;
    /// fresh=true returns a new JS object each call (new proxy + JSHandle, disposed immediately).</summary>
    [JSExport]
    public static double BenchReturnObj(int n, bool fresh)
    {
        long t0 = Stopwatch.GetTimestamp();
        int acc = 0;
        if (fresh)
        {
            for (int i = 0; i < n; i++) { using var o = WebGpu.NewObject(); acc += o is null ? 0 : 1; }
        }
        else
        {
            for (int i = 0; i < n; i++) { var o = WebGpu.GetDeviceObject(); acc += o is null ? 0 : 1; }
        }
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (acc == 42) Console.WriteLine("unlikely");
        return ms;
    }

    /// <summary>JS->C# direction: trivial export used by JS-side loop.</summary>
    [JSExport] public static int Echo(int i) => i + 1;

    [JSExport]
    public static double BenchWriteBuffer(int n)
    {
        long t0 = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++) WebGpu.WriteBuffer(s_ubo, 0, (nint)s_matrix, 64);
        return Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    [JSExport]
    public static double BenchWriteBufferSpan(int n)
    {
        long t0 = Stopwatch.GetTimestamp();
        var span = new Span<byte>(s_matrix, 64);
        for (int i = 0; i < n; i++) WebGpu.WriteBufferSpan(s_ubo, 0, span);
        return Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    /// <summary>
    /// Creates n render pipelines (sync createRenderPipeline via JSON). distinctShaders => a new WGSL
    /// module per pipeline with K = kBase + i (defeats dedup and shader caches). Returns "shaderMs;pipelineMs;jsonMs".
    /// </summary>
    [JSExport]
    public static string BenchPipelines(int n, bool distinctShaders, int kBase)
    {
        var shaders = new int[n];
        var pipes = new int[n];
        long t0 = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++)
            shaders[i] = distinctShaders ? WebGpu.CreateShaderModule(ShaderSource(kBase + i)) : s_shader;
        double shaderMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        var json = new string[n];
        long t1 = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++) json[i] = PipelineJson(shaders[i], s_bglStatic, s_format);
        double jsonMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
        long t2 = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++) pipes[i] = WebGpu.CreateRenderPipelineJson(json[i]);
        double pipeMs = Stopwatch.GetElapsedTime(t2).TotalMilliseconds;
        for (int i = 0; i < n; i++)
        {
            WebGpu.Release(pipes[i]);
            if (distinctShaders) WebGpu.Release(shaders[i]);
        }
        return $"{shaderMs:0.###};{pipeMs:0.###};{jsonMs:0.###}";
    }

    /// <summary>Allocates (and touches) unmanaged memory to force wasm memory.grow (heap-view hazard demo).</summary>
    [JSExport]
    public static double GrowHeap(int megabytes)
    {
        nuint size = (nuint)megabytes * 1024 * 1024;
        byte* p = (byte*)NativeMemory.Alloc(size);
        for (nuint i = 0; i < size; i += 4096) p[i] = 1;
        s_keepAlive = p;
        return (double)(nint)p;
    }
    static byte* s_keepAlive;
}

// Separate (non-unsafe) partial declaration: `await` is not allowed in an unsafe context (CS4004).
public static partial class App
{
    /// <summary>
    /// n pipelines with distinct shaders via createRenderPipelineAsync (Task&lt;int&gt; per JSImport, awaited with
    /// Task.WhenAll). Returns "shaderMs;issueMs;allResolvedMs" (C# Stopwatch).
    /// </summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static async System.Threading.Tasks.Task<string> BenchPipelinesAsync(int n, int kBase)
    {
        long t0 = Stopwatch.GetTimestamp();
        var shaders = new int[n];
        for (int i = 0; i < n; i++) shaders[i] = WebGpu.CreateShaderModule(ShaderSource(kBase + i));
        double shaderMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        var tasks = new System.Threading.Tasks.Task<int>[n];
        for (int i = 0; i < n; i++) tasks[i] = WebGpu.CreateRenderPipelineJsonAsync(PipelineJson(shaders[i], s_bglStatic, s_format));
        double issueMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        int[] pipes = await System.Threading.Tasks.Task.WhenAll(tasks);
        double doneMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        for (int i = 0; i < n; i++) { WebGpu.Release(pipes[i]); WebGpu.Release(shaders[i]); }
        return $"{shaderMs:0.###};{issueMs:0.###};{doneMs:0.###}";
    }
}
