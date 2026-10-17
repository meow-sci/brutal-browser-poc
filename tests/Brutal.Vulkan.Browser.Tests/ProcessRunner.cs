using System.ComponentModel;
using System.Diagnostics;

namespace Brutal.Vulkan.Browser.Tests;

internal static class ProcessRunner
{
    /// <summary>Runs a tool and returns (exit code, stdout + stderr); null when the executable is not installed.</summary>
    public static (int ExitCode, string Output)? Run(string fileName, IEnumerable<string> args, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(info)!;
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout + stderr.Result);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
