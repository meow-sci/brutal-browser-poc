namespace Brutal.Browser.Tests;

/// <summary>BrowserInput, BrowserCanvas and Glfw hold process-wide state: tests that touch them run serially.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BrowserCollection
{
    public const string Name = "Browser global state";
}
