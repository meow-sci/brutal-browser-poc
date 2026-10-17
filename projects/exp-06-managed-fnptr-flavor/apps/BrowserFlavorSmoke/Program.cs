// exp-06 browser entry point. main.js forwards the page query as arguments:
//   bench=0      skip benchmarks          iters=N   benchmark iterations (default 1,000,000)
//   callnull=1   call a NULL table entry (vkCmdDraw) at the end, inside try/catch
using Exp06;

var bench = !args.Contains("bench=0");
var iters = args.Where(a => a.StartsWith("iters=")).Select(a => int.Parse(a[6..])).FirstOrDefault(1_000_000);
var callNull = args.Contains("callnull=1");
Console.WriteLine($"EXP06 BrowserFlavorSmoke args=[{string.Join(",", args)}] bench={bench} iters={iters} callNull={callNull}");
var rc = 1;
try
{
    rc = FlavorScenario.Run(Console.WriteLine, new FlavorScenario.Options(bench, iters, callNull));
    Console.WriteLine("EXP06 instance-proc-names BEGIN");
    foreach (var l in ManagedIcd.InstanceProcNames) Console.WriteLine("EXP06I " + l);
    Console.WriteLine("EXP06 device-proc-names BEGIN");
    foreach (var l in ManagedIcd.DeviceProcNames) Console.WriteLine("EXP06D " + l);
}
catch (Exception e)
{
    Console.WriteLine($"EXP06 EXCEPTION {e.GetType().FullName}: {e.Message}");
    for (var inner = e.InnerException; inner != null; inner = inner.InnerException)
        Console.WriteLine($"EXP06   inner {inner.GetType().FullName}: {inner.Message}");
    Console.WriteLine(e.StackTrace);
}
Console.WriteLine($"EXP06 DONE rc={rc}");
return rc;
