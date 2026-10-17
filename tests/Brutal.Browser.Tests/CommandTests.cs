namespace Brutal.Browser.Tests;

/// <summary>Page to game commands: <see cref="BrowserHost.RegisterCommand"/> / <see cref="BrowserHost.Dispatch"/> (docs/architecture.md section 5).</summary>
[Collection(BrowserCollection.Name)] // Dispatch writes to Console.Out, which these tests redirect
public sealed class CommandTests : IDisposable
{
    private const string Name = "test-command";

    private readonly TextWriter _originalOut = Console.Out;
    private readonly TextWriter _originalError = Console.Error;
    private readonly StringWriter _out = new();

    public CommandTests()
    {
        Console.SetOut(_out);
        Console.SetError(new StringWriter());
    }

    public void Dispose()
    {
        BrowserHost.UnregisterCommand(Name);
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
    }

    private string[] Lines() => _out.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Dispatch_runs_the_registered_handler_and_prints_nothing()
    {
        int calls = 0;
        BrowserHost.RegisterCommand(Name, () => calls++);

        BrowserHost.Dispatch(Name);
        BrowserHost.Dispatch(Name);

        Assert.Equal(2, calls);
        Assert.Empty(Lines());
    }

    [Fact]
    public void Registering_again_replaces_the_handler()
    {
        string last = "";
        BrowserHost.RegisterCommand(Name, () => last = "first");
        BrowserHost.RegisterCommand(Name, () => last = "second");

        BrowserHost.Dispatch(Name);

        Assert.Equal("second", last);
    }

    [Fact]
    public void Unknown_command_prints_a_warning()
    {
        BrowserHost.Dispatch("no-such-command");

        Assert.Equal(["WARN unknown command no-such-command"], Lines());
    }

    [Fact]
    public void Names_are_case_sensitive_and_unregistered_commands_are_unknown()
    {
        int calls = 0;
        BrowserHost.RegisterCommand(Name, () => calls++);

        BrowserHost.Dispatch(Name.ToUpperInvariant());
        Assert.True(BrowserHost.UnregisterCommand(Name));
        Assert.False(BrowserHost.UnregisterCommand(Name));
        BrowserHost.Dispatch(Name);

        Assert.Equal(0, calls);
        Assert.Equal([$"WARN unknown command {Name.ToUpperInvariant()}", $"WARN unknown command {Name}"], Lines());
    }

    [Fact]
    public void A_throwing_handler_prints_ERROR_and_does_not_propagate()
    {
        BrowserHost.RegisterCommand(Name, () => throw new InvalidOperationException("boom"));

        BrowserHost.Dispatch(Name);

        Assert.Equal(["ERROR boom"], Lines());
    }

    [Fact]
    public void Register_validates_its_arguments()
    {
        Assert.Throws<ArgumentException>(() => BrowserHost.RegisterCommand("", () => { }));
        Assert.Throws<ArgumentNullException>(() => BrowserHost.RegisterCommand(Name, null!));
    }
}
