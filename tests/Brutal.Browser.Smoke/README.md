# Brutal.Browser.Smoke

A `wasmbrowser` app (`Microsoft.NET.Sdk.WebAssembly`, no WebGPU) that exercises `Brutal.Browser` and `Brutal.Glfw.Browser` end to end:
it creates a `GlfwWindow`, prints one `EVT ...` line per input event, `FRAME <n>` every 60 frames and, after the page's start click,
decodes and plays the embedded Ogg Vorbis sound `Assets/874222__samrad20__generic-explosion.ogg` (stereo, 48 kHz, 1.54 s; licence: `samples/HelloMesh/Assets/LICENSES.md`) (`AUDIO loaded ...`, `AUDIO play`). `wwwroot/main.js` is the reference boot sequence (architecture section 5).

```
cd test && npm install          # playwright 1.64.1, project-local
node run.mjs                    # dotnet publish -c Release, serve bin/Release/net10.0/publish/wwwroot on localhost, run four passes
node run.mjs --no-build         # reuse the last publish
node run.mjs --strict-autoplay  # pin Chrome's desktop autoplay policy (document-user-activation-required); 40 checks either way
```

Passes: (1) default: keyboard, mouse press/drag, wheel, viewport resize, pointer lock, blur, audio `running` and a measured output level (`window.__brutal.audio.getOutputLevel() > 0.01` within 500 ms of `AUDIO play`), `FRAME 120`;
(2) `?managed=1`: NVorbis decodes the Ogg and its output level is checked the same way; (3) `?crash=1`: an exception in the frame prints `ERROR <message>` and stops the loop;
(4) `deviceScaleFactor: 2`: canvas pixel size and content scale. Exit code 0 = all checks passed; the console is written to `test/console.log`.
Uses Playwright's full Chromium (`channel: 'chromium'`); override with `--browser <exe>` or `CHROME_PATH`.
