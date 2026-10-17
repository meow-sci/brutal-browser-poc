// exp-06 DesktopFlavorSmoke: runs shared/FlavorScenario.cs on CoreCLR (JIT).
//   --no-bench       skip the micro-benchmarks
//   --iters N        benchmark iterations (default 1,000,000)
//   --call-null      also call a NULL table entry (vkCmdDraw) at the end
// EXP06_OUT=<dir>    write the ordered proc-name requests to <dir>/desktop-{instance,device}-proc-names.txt
using Exp06;

var iters = args.SkipWhile(a => a != "--iters").Skip(1).Select(int.Parse).FirstOrDefault(1_000_000);
var rc = FlavorScenario.Run(Console.WriteLine, new FlavorScenario.Options(
    Bench: !args.Contains("--no-bench"), BenchIterations: iters, CallNull: args.Contains("--call-null")));
var outDir = Environment.GetEnvironmentVariable("EXP06_OUT");
if (outDir is not null)
{
    Directory.CreateDirectory(outDir);
    File.WriteAllLines(Path.Combine(outDir, "desktop-instance-proc-names.txt"), ManagedIcd.InstanceProcNames.Select((l, i) => $"{i + 1}\t{l}"));
    File.WriteAllLines(Path.Combine(outDir, "desktop-device-proc-names.txt"), ManagedIcd.DeviceProcNames.Select((l, i) => $"{i + 1}\t{l}"));
}
return rc;
