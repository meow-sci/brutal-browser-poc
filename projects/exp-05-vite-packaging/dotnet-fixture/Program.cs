using System;
using System.Runtime.InteropServices.JavaScript;

public static partial class Program
{
    public static void Main() => Console.WriteLine("Fixture: Main() ran");

    [JSImport("host.log", "main.js")]
    internal static partial void HostLog(string s);

    [JSExport]
    internal static int Tick(double dt)
    {
        HostLog($"tick dt={dt:F2}");
        return 42;
    }
}
