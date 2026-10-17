import type { DotnetHostBuilder } from '../vendor/dotnet';
// NEGATIVE CONTROL: statically import the *plain* dotnet.js (default publish output) into the module graph.
// @ts-expect-error no types for the deep path
import { dotnet } from '../../../out/publish-plain/wwwroot/_framework/dotnet.js';
export async function loadDotnet(): Promise<DotnetHostBuilder> { return dotnet as DotnetHostBuilder; }
