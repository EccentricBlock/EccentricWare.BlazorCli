using System.Text.Json;
using EccentricWare.BlazorCli.TemplateApp.Application;

namespace EccentricWare.BlazorCli.TemplateApp.Cli;

public static class StatusCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Execute(IApplicationStatusService statusService, bool json, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(statusService);
        ArgumentNullException.ThrowIfNull(output);
        var status = statusService.GetStatus(AppMode.Command);
        if (json)
        {
            // Exactly one JSON object and a trailing newline; no host is started.
            output.WriteLine(JsonSerializer.Serialize(status, JsonOptions));
            return;
        }
        output.WriteLine($"{status.Application} {status.Version} ({status.Mode}): {status.Status}");
    }
}
