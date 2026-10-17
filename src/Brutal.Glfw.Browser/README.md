# Brutal.Glfw.Browser

Source-compatible subset of BRUTAL's `Brutal.GlfwApi` (namespace `Brutal.GlfwApi`, assembly name `Brutal.Glfw`) on top of `Brutal.Browser`.
Game code written against `Glfw` / `GlfwWindow` compiles unchanged; there is no native GLFW. Contract: `docs/architecture.md` section 7.

Enums, constants, plain structs, the unmanaged delegate types, the managed callback delegate types and `ExceptionAggregator` are copied
verbatim from the pinned BRUTAL tree (commit `a25cdf8`, `Brutal.Glfw/Generated/**`, `Glfw.cs` and the delegate declarations of
`Manual/Libraries/*.cs`); see `BRUTAL-NOTICE.txt`. Hand-written: `Glfw.Browser.cs`, `GlfwWindow.cs`, `GlfwMonitor.cs`, `GlfwCursor.cs`.
Add `ExcludeAssets="all"` for `Ahwoo.BRUTAL.Glfw` in browser projects so the NuGet assembly of the same name is not used.

## Semantics

- `Glfw.Init()` initialises `BrowserHost`; `Glfw.PollEvents()` drains the JS queue, raises window events in order and rethrows handler exceptions
  (aggregated like BRUTAL). `Glfw.Time` = seconds since `Init` (`performance.now()`).
- One window = the page canvas. `Size` = canvas CSS size, `FramebufferSize` = `BrowserCanvas.PixelSize` (what the swapchain must match),
  `ContentScale` = `devicePixelRatio`. `CreateInfo.Size` is ignored (the page's CSS decides); setting `Size` sets an explicit CSS size.
- `SetInputMode(Cursor, Disabled)` requests pointer lock (needs a user gesture; retried on the next click/keydown; Escape releases it and the
  cached mode becomes `Normal`). `Hidden` = CSS `cursor: none`. `RawMouseMotion` asks for unadjusted movement.
- `GetRequiredInstanceExtensions` returns `VK_KHR_surface`, `VK_BRUTAL_web_surface` (`byte**`, `out uint count`, like BRUTAL).
  `CreateWindowSurface` calls `BrowserSurfaces.CreateSurface(instance, BrowserCanvas.SurfaceHandle, out surface)` and throws
  `InvalidOperationException` if the web ICD has not set it.
- Joystick/gamepad queries answer "nothing connected" instead of throwing (so per-frame polling works); real gamepad support is future work.
- Everything else that is declared throws `NotSupportedException("<member> is not available in the browser")`.

## Members

| Status | Members |
| --- | --- |
| Implemented | `Glfw`: `Init`, `Terminate`, `SetInitHint` (ignored), `Version`, `VersionString`, `ErrorCallback`, `GetError`, `WindowHint` (x2, remembered), `DefaultWindowHints`, `CreateWindow`, `PollEvents`, `Time`, `SetTime`, `GetTimerValue/Frequency`, `Clipboard`, `SetClipboard`, `VulkanSupported`, `GetRequiredInstanceExtensions`, `GetPhysicalDevicePresentationSupport`, `CreateWindowSurface`, `RawMotionSupported`, `GetKeyName` (null), `CreateCursor(GlfwStandardCursor)`. `GlfwWindow`: `NativeHandle`, `Title`, `Size/Width/Height`, `FramebufferSize`, `AspectRatio` (get), `ContentScale`, `CursorPos/X/Y` (virtual set), `Opacity`/`Position`/`Monitor`/`Visible` (fixed values), `IsMaximised`, `IsIconified`, `IsFocused`, `IsFullscreen`, `IsWindowed`, `ShouldClose`, `UserPointer`, `Dispose`, `IsNull`, `GetKey`, `GetMouseButton`, `GetInputMode`, `SetInputMode`, `GetAttribute(GlfwWindowAttribute)` (Focused, Hovered, Visible, ...), `SetCursor`, `Focus`, `WaitForWindow`, `Refresh`; all 20 events (`OnKey`, `OnChar`, `OnCharMods`, `OnMouseButton`, `OnCursorPos`, `OnCursorEnter`, `OnScroll`, `OnScrollDetail`, `OnFramebufferSize`, `OnSize`, `OnContentScale`, `OnFocus`, `OnClose` are raised; `OnDrop`, `OnTrackpadZoom/Rotate`, `OnIconify`, `OnMaximize`, `OnPosition`, `OnRefresh` never fire) |
| "Nothing connected" | `JoystickPresent`, `GetJoystickAxes/Buttons/Hats/Name/Guid`, `JoystickIsGamepad`, `GetGamepadName`, `GetGamepadState`, `UpdateGamepadMappings`, `Set/GetJoystickUserPointer`, `JoystickCallback` |
| `NotSupportedException` | `Glfw`: `InitAllocator`, `WaitEvents`, `WaitEventsTimeout`, `PostEmptyEvent`, `PrimaryMonitor`, `GetMonitors`, `GetInstanceProcAddress`, `GetKeyScanCode`, `CreateCursor(in CreateInfo)`; `GlfwWindow`: `SetIcon`, `SetSizeLimits`, `SetFullscreen`, `SetWindowed`, `CenterToMonitor`, `GetWindowFrameSize`, `Iconify`, `Restore`, `Maximize`, `RequestAttention`, `MakeContextCurrent`, `SwapBuffers`, `SetAttribute`, `GetAttribute(GlfwContextAttribute)`, setters of `Position`/`AspectRatio`/`Opacity`/`Monitor`/`Visible` to other values, `StickyKeys`/`StickyMouseButtons`/`Captured` input modes; `GlfwMonitor` members except `Null`/`IsNull`/`NativeHandle` |
| Not declared (compile error) | OpenGL context API (`SwapInterval`, `ExtensionSupported`, `GetCurrentContext`, `GetProcAddress`), platform/native-window accessors, `GetPlatform`/`PlatformSupported`, `GlfwGammaRampView` |

## Build and test

`dotnet build src/Brutal.Glfw.Browser -c Release`; unit tests in `tests/Brutal.Browser.Tests` (`GlfwApiTests`); end-to-end in `tests/Brutal.Browser.Smoke`.
