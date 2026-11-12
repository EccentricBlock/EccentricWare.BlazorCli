using System.Globalization;
using EccentricWare.BlazorCli.TemplateApp.Application;

namespace EccentricWare.BlazorCli.TemplateApp.Bootstrap;

/// <summary>Pure, deliberately narrow parser. No host is constructed on validation failure.</summary>
public static class CommandLineParser
{
    public const int DefaultPort = 5080;

    /// <summary>Help text. It names the application by its assembly name, which is also the apphost file name.</summary>
    public static string Usage { get; } = BuildUsage(typeof(CommandLineParser).Assembly.GetName().Name ?? "app");

    public static string BuildUsage(string applicationName) => $"""
        Usage: {applicationName} [command]

        Commands:
          (none), --help, -h        Show this help
          --version                 Show the application version
          status [--json]           Show this process's local status
          run                       Run the worker without HTTP
          --web [--port PORT]       Run the worker and local web dashboard

        The web listener binds only to 127.0.0.1 (default port {DefaultPort}).
        Arguments are exact and case-sensitive. Options cannot be combined with help/version.
        """;

    public static ParseResult Parse(string[]? args)
    {
        if (args is null) return ParseResult.Failure("Arguments cannot be null.");
        if (args.Length > 16 || args.Any(arg => arg is null || arg.Length > 256))
            return ParseResult.Failure("Too many or excessively long arguments.");

        if (args.Length == 0 || (args.Length == 1 && args[0] is "--help" or "-h"))
            return ParseResult.Success(new(AppCommand.Help));
        if (args.Length == 1 && args[0] == "--version")
            return ParseResult.Success(new(AppCommand.Version));
        if (args[0] == "status")
        {
            if (args.Length == 1)
                return ParseResult.Success(new(AppCommand.Status));
            if (args.Length == 2 && args[1] == "--json")
                return ParseResult.Success(new(AppCommand.Status, Json: true));
            return ParseResult.Failure("status accepts only the optional --json flag.");
        }
        if (args[0] == "run")
            return args.Length == 1
                ? ParseResult.Success(new(AppCommand.Run))
                : ParseResult.Failure("run accepts no options.");
        if (args[0] == "--web")
        {
            if (args.Length == 1)
                return ParseResult.Success(new(AppCommand.Web));
            if (args.Length != 3 || args[1] != "--port")
                return ParseResult.Failure("--web accepts only --port followed by a number.");
            var value = args[2];
            if (value.Length == 0 || value.Any(c => c is < '0' or > '9') ||
                !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
                port is < 1 or > 65535)
                return ParseResult.Failure("Port must be a decimal integer between 1 and 65535.");
            return ParseResult.Success(new(AppCommand.Web, Port: port));
        }
        // Includes '--', --urls, ASP.NET Core switches, and positional '--web'.
        return ParseResult.Failure("Unrecognised or incompatible command-line arguments.");
    }
}
