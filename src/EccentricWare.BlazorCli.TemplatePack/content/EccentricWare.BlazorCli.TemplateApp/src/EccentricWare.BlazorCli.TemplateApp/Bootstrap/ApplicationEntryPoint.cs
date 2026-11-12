using System.Net.Sockets;
using EccentricWare.BlazorCli.TemplateApp.Cli;
using EccentricWare.BlazorCli.TemplateApp.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EccentricWare.BlazorCli.TemplateApp.Bootstrap;

public static class ApplicationEntryPoint
{
    public const int ExitSuccess = 0;
    public const int ExitUsage = 2;
    public const int ExitStartup = 3;
    public const int ExitOperation = 4;
    public const int ExitUnexpected = 70;

    public static async Task<int> RunAsync(string[] args)
    {
        var parse = CommandLineParser.Parse(args);
        if (!parse.IsValid)
        {
            Console.Error.WriteLine($"Usage error: {parse.Error}");
            Console.Error.WriteLine(CommandLineParser.Usage);
            return ExitUsage;
        }

        var invocation = parse.Invocation!;
        try
        {
            switch (invocation.Command)
            {
                case AppCommand.Help:
                    Console.Out.WriteLine(CommandLineParser.Usage);
                    return ExitSuccess;
                case AppCommand.Version:
                    Console.Out.WriteLine(new ApplicationStatusService().GetStatus(AppMode.Command).Version);
                    return ExitSuccess;
                case AppCommand.Status:
                    // Build the common DI container, but never StartAsync: no hosted services or HTTP.
                    var settings = new HostApplicationBuilderSettings
                    {
                        Args = Array.Empty<string>(),
                        ContentRootPath = AppContext.BaseDirectory,
                    };
                    var cliBuilder = Host.CreateApplicationBuilder(settings);
                    cliBuilder.Services.AddApplicationServices();
                    using (var host = cliBuilder.Build())
                    using (var scope = host.Services.CreateScope())
                        StatusCommand.Execute(scope.ServiceProvider.GetRequiredService<IApplicationStatusService>(), invocation.Json, Console.Out);
                    return ExitSuccess;
                case AppCommand.Run:
                    return await Hosting.RunWorkerAsync();
                case AppCommand.Web:
                    return await Hosting.RunWebAsync(invocation.Port);
                default:
                    return ExitUnexpected;
            }
        }
        catch (OperationCanceledException)
        {
            return ExitSuccess;
        }
        catch (Exception exception)
        {
            var (code, message) = Classify(exception, invocation.Mode);
            Console.Error.WriteLine(message);
            return code;
        }
    }

    /// <summary>
    /// Maps an exception to an exit code and a user-safe message. Messages never include exception
    /// text, paths or environment values; only <see cref="ExpectedOperationException"/> carries a
    /// message that its author wrote for the user.
    /// </summary>
    public static (int ExitCode, string Message) Classify(Exception exception, AppMode mode) => exception switch
    {
        ExpectedOperationException expected => (ExitOperation, $"Error: {expected.Message}"),
        // Startup advice is wrong for a one-shot command (for example a closed stdout pipe), so only the
        // long-running modes map these to 3.
        IOException or SocketException or UnauthorizedAccessException or InvalidOperationException
            when mode != AppMode.Command => (ExitStartup, StartupFailure),
        _ => (ExitUnexpected, "An unexpected application error occurred."),
    };

    private const string StartupFailure =
        "Startup failed. Check port availability, permissions and application configuration.";
}
