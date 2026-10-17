import type { DotnetHostBuilder } from '../vendor/dotnet';
// Approach A: dotnet.js is NOT part of the module graph; it is fetched at runtime from <base>_framework/dotnet.js.
export async function loadDotnet(): Promise<DotnetHostBuilder> {
  const url = `${import.meta.env.BASE_URL}_framework/dotnet.js`;
  return (await import(/* @vite-ignore */ url)).dotnet;
}
