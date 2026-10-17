# Brutal.Vulkan.WebGpu.Smoke

Browser smoke test of the web ICD. `TriangleScene.cs` is a game-shaped Vulkan program written only against BRUTAL's API (instance
with `VK_BRUTAL_web_surface`, surface through the `BrowserSurfaces` hook, `DeviceEx` with dynamic rendering + synchronization2,
swapchain, depth image, vertex buffer through `BufferEx` + `StagingPool`, a 2x2 texture through staging + `vkCmdCopyBufferToImage`,
sampler, descriptor set with a dynamic-offset UBO + texture + sampler, push constants, depth-tested pipeline) that draws a front
triangle and a larger back triangle behind it every frame, and at frame 30 reads the centre pixel back with
`vkCmdCopyImageToBuffer`. `ComputeCheck.cs` runs one compute dispatch (`fill.comp`: storage buffer + push constant) at start-up
and reads the result back across frames (`COMPUTE ok=True`). Frame 60 draws the front triangle 5000 more times, each after a push-constant
update (`PUSHSTRESS`), so the ICD's push-constant ring has to grow past its initial 4096 slots without an error. The desktop tests
(`tests/Brutal.Vulkan.WebGpu.Tests`) run the same scene against a recording bridge.

`Shaders/tri.vert|frag`, `fill.comp` are compiled by `tools/Shaders.targets` (glslc → SPIR-V, `tools/build-shaders.mjs` → WGSL manifest);
`wwwroot/main.js` boots per docs/architecture.md section 5 and drives `SmokeApp.Tick` from `requestAnimationFrame`.

```sh
cd test && npm ci           # Playwright 1.64.1, project-local (uses the cached Chromium build; never `npx playwright install`)
node run.mjs                # dotnet publish -c Release (trimmed), headless Chromium, pixel + readback checks; exit 0 = pass
node run.mjs --chrome       # same against Google Chrome; --config Debug for an untrimmed build; --no-build to reuse the output
```

Expected pixels (640x480 canvas, B8G8R8A8_UNORM): centre (255,128,0) — texel (1,1) yellow x push tint (1,.5,1); lower left of the
front triangle (0,0,255); below the front triangle the back one (64,128,0); corner = clear colour (26,51,77).
