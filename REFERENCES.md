# Reference material

The BRUTAL framework which is a framework level set of C# bindings to vulkan and supporting libraries is open source

BRUTAL NuGet packages which are pre-built are available on NuGet, see:

- https://www.nuget.org/profiles/Ahwoo?page=1
- https://www.nuget.org/profiles/Ahwoo?page=2
- https://www.nuget.org/profiles/Ahwoo?page=3

The sources are checked out at /Users/asherwin/repos/meow-sci/BRUTAL

A game kitten space agency (KSA) is fully built on BRUTAL, it is not open source but the decompiled sources are available at /Users/asherwin/repos/meow-sci/ksa-game-assemblies

A copy of the KSA linux game is at /Users/asherwin/repos/meow-sci/ksa-linux which contains a "Core" mod at /Users/asherwin/repos/meow-sci/ksa-linux/Content/Core which contains KSA specific game assets (meshes, textures, audio, etc) and shaders

A library /Users/asherwin/repos/meow-sci/ImCode was created which is an early attempt at linking to the BRUTAL NuGet packages to make a game-agnostic open source re-usable coding environment for use in-game using the BRUTAL ImGui framework

And /Users/asherwin/repos/meow-sci/brutal-imgui-standalone is a library I created which is an attempt to make a reusable wrapper around the BRUTAL ImGui packages BEFORE it went open source, so this was crudely using the black box BURTAL.ImGui and transitive dependencies from a user-supplied KSA game dir (this project can now be updated to use the open source BURTAL.ImGui package(s), but that is not your task right now)

# Your task

Now given this contextual information, I am interested in doing an initial PoC which will get BRUTAL running in a browser, this MUST use only the open source BRUTAL NuGet packages, never linking the KSA DLLs, the KSA DLLs and decompiled sources are just reference material

Ideally, this would:

- use open source BRUTAL
- compile down to WASM (maybe using the C# pattern for compiling to blazor wasm?  i am not sure, this requires research)
- create our own "bindings" or some kind of translation layer that targets a browser compatible rendering framework.  Ideally we would target WebGPU and try to.  I dont know the right terminology for this or how to describe it other then this, but I want to leverage the BRUTAL vulkan bindings and translating that to something that is WebGPU compatible

The PoC must

- Render a 3d model on screen
- Support i/o via keyboard and mouse (just something simple to prove they work, maybe spin the model with mouse and pan/zoom the viewport with arrow keys)
- Play audio, there was a sound file in the project root that could be used (it was proprietary and has been removed from the repository; the sample now plays `samples/HelloMesh/Assets/874222__samrad20__generic-explosion.ogg`)
- use latest vite 8+ to bundle the final webapp and to run the dev server

I want a minimal amount of work done to accomplish this but be a viable path for a full game to be built down this technical path, not a complete hack.

And it must be a pattern that actually leverages BRUTAL meaningfully, in the sense that BRUTAL is providing substantial benefit such that the end-user that would be building a game using this pattern would be writing C# code against BRUTAL code and have it work in-browser in the end.

I realize some comrpomises may be necessary in terms of shader languages, but if it's possible to write in a BRUTAL compatible shader language and use an automatic cross compiler to a WebGPU compatible format (which I think may exist) would be ideal

I understand that WebGPU is some kind of vulkan-like layer in browsers so if we can somehow have a thin bridge to make this mostly work via BRUTAL that would be ideal

Do some research, make notes and research in OKF format into a bundle under the docs/research folder using the okf skill. This should be useful research material that can be referenced later from the hard fought research and analysis you do so that future agentic sessions can leverage it without needing to re analyze the input corpus of information you have availbale.  Be detailed and exhaustive.

Then make a proposal, or more then one proposal if there are multiple viable paths, into detailed implementation plan with phases and tasks into a markdown file in the plans/ folder for how to build the PoC

You can also build and test actual test projects as needed under the projects/ folder for test and validation of ideas

If you need more tools or things installed ask me and I can provide that, but most things you need should be available to you already like dotnet and this macOS machine has MoltenVK available for vulkan support at /Users/asherwin/VulkanSDK/1.4.328.1

Use subagents with opus-5.5 / xhigh for coding tasks that need some reasoning and use sonnet-5.5 / xhigh subagents for lesser scripting and validation tasks and general work and use haiku-5.5 / xhigh for trivial tasks like test running and simple scripting etc that dont require deep reasoning, just be highly specific with the instructions to the sonnet and haiku subagents

Be thorough, do deep research, weigh the options, and come up with something novel that will put BRUTAL front and center around being the centerpeice of a new way to deliver a game that can render in browser (where BRUTAL as-is right now really only targets desktop platform OS's macOS, Linux and Windows)

