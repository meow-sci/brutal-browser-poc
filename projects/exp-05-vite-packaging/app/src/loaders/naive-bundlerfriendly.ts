import type { DotnetHostBuilder } from '../vendor/dotnet';
// NEGATIVE CONTROL: statically import the bundler-friendly dotnet.js WITHOUT unplugin-dotnet-wasm.
// @ts-expect-error no types for the deep path
import { dotnet } from '../../../out/publish-bundler/wwwroot/_framework/dotnet.js';
export async function loadDotnet(): Promise<DotnetHostBuilder> { return dotnet as DotnetHostBuilder; }
