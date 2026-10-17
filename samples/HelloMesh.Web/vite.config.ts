import { defineConfig } from 'vite';
import { dotnetWwwroot } from './plugins/dotnet-wwwroot.ts';

// The .NET output location comes from the environment (see README):
//   DOTNET_APP_DIR        default ../HelloMesh/HelloMesh.Browser   (the .NET project directory)
//   DOTNET_CONFIGURATION  default Release
export default defineConfig({
  plugins: [dotnetWwwroot()],
  build: { target: 'es2023' },
  server: { watch: { ignored: ['**/test/out/**'] } },
});
