using Exp04;

var outDir = Environment.GetEnvironmentVariable("EXP04_OUT");
var callNull = args.Contains("--call-null");
var rc = ManagedLoaderScenario.Run(Console.WriteLine, UcoEntryPoints.Register(), callNull);
if (outDir is not null && !callNull)
{
    Directory.CreateDirectory(outDir);
    File.WriteAllLines(Path.Combine(outDir, "instance-proc-names.txt"), FakeIcd.InstanceProcNames.Select((l, i) => $"{i + 1}\t{l}"));
    File.WriteAllLines(Path.Combine(outDir, "device-proc-names.txt"), FakeIcd.DeviceProcNames.Select((l, i) => $"{i + 1}\t{l}"));
    Console.WriteLine($"Wrote {FakeIcd.InstanceProcNames.Count} instance-level and {FakeIcd.DeviceProcNames.Count} device-level names to {outDir}");
}
return rc;
