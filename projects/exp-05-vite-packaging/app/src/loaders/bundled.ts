import type { DotnetHostBuilder } from '../vendor/dotnet';
// Approach B: unplugin-dotnet-wasm resolves `_framework/dotnet` to the bundler-friendly build output.
// @ts-expect-error virtual module provided by unplugin-dotnet-wasm
import { dotnet } from '_framework/dotnet';
export async function loadDotnet(): Promise<DotnetHostBuilder> {
  return dotnet as DotnetHostBuilder;
}
