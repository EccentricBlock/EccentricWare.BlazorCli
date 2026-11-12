using EccentricWare.BlazorCli.TemplateApp.Application;

namespace EccentricWare.BlazorCli.TemplateApp.Bootstrap;

public enum AppCommand { Help, Version, Status, Run, Web }

/// <summary>A validated command line. <see cref="Mode"/> follows from <see cref="Command"/>, so the two cannot disagree.</summary>
public sealed record AppInvocation(AppCommand Command, bool Json = false, int Port = CommandLineParser.DefaultPort)
{
    public AppMode Mode => Command switch
    {
        AppCommand.Help or AppCommand.Version or AppCommand.Status => AppMode.Command,
        AppCommand.Run => AppMode.Worker,
        AppCommand.Web => AppMode.Web,
        // A new command must pick its mode here: the mode decides how startup errors are classified.
        _ => throw new ArgumentOutOfRangeException(nameof(Command), Command, "No AppMode is defined for this command."),
    };
}

public sealed record ParseResult(AppInvocation? Invocation, string? Error)
{
    public bool IsValid => Invocation is not null && Error is null;
    public static ParseResult Success(AppInvocation invocation) => new(invocation, null);
    public static ParseResult Failure(string message) => new(null, message);
}
