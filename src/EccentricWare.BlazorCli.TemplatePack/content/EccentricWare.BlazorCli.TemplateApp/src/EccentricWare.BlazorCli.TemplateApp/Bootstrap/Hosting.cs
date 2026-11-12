using System.Net;
using EccentricWare.BlazorCli.TemplateApp.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor.Services;
// An alias keeps a project named like "Acme.App" from turning `App` into the namespace Acme.App.
using RootComponent = EccentricWare.BlazorCli.TemplateApp.Components.App;

namespace EccentricWare.BlazorCli.TemplateApp.Bootstrap;

public static class Hosting
{
    public static async Task<int> RunWorkerAsync()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Services.AddApplicationServices();
        builder.Services.AddApplicationWorkers();
        using var host = builder.Build();
        return await RunToCompletionAsync(host);
    }

    public static async Task<int> RunWebAsync(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = IsRunningFromBuildOutput() ? Environments.Development : Environments.Production,
        });

        // Remove ALL inherited configuration sources: ASPNETCORE_URLS, HTTP_PORTS, Kestrel:Endpoints,
        // Kestrel__Endpoints__*, launchSettings and arbitrary appsettings must never add endpoints.
        // There are intentionally no externally configurable web hosting settings in v0.2.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "127.0.0.1;localhost",
            // Clearing the sources also dropped the default logging section: without it every request
            // is logged at Information. Keep application and lifetime messages, mute per-request noise.
            ["Logging:LogLevel:Default"] = "Information",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
        });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, port);
        });

        builder.Services.AddApplicationServices();
        builder.Services.AddApplicationWorkers();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddMudServices();

        await using var app = builder.Build();
        // Even a build-output run must not emit a Developer Exception Page
        // with stack traces or environment details to unauthenticated local callers.
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("An internal error occurred.");
        }));

        // Host/Origin filtering and browser safety headers apply to all app, asset and API routes.
        app.UseLoopbackGuard(port);
        app.UseAntiforgery();
        app.MapStaticAssets();
        // Blazor's default is "frame-ancestors 'self'", which contradicts X-Frame-Options: DENY.
        app.MapRazorComponents<RootComponent>().AddInteractiveServerRenderMode(options =>
            options.ContentSecurityFrameAncestorsPolicy = "'none'");
        app.MapStatusEndpoints();
        return await RunToCompletionAsync(app);
    }

    /// <summary>
    /// Runs the host until shutdown. A faulted background worker stops the host, but the generic host
    /// would then return normally, so Main would exit 0 after a crash. Report that as unexpected (70).
    /// </summary>
    public static async Task<int> RunToCompletionAsync(IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        // Hosted services are singletons. Resolve them first: RunAsync disposes the host.
        var workers = host.Services.GetServices<IHostedService>().OfType<BackgroundService>().ToArray();
        await host.RunAsync();
        return workers.Any(worker => worker.ExecuteTask is { IsFaulted: true })
            ? ApplicationEntryPoint.ExitUnexpected
            : ApplicationEntryPoint.ExitSuccess;
    }

    /// <summary>
    /// True for a normal build output (bin/...), which ships *.staticwebassets.runtime.json beside the
    /// assembly. Publish output does not. Development is needed so the SDK resolves static assets from
    /// the project and NuGet cache. This holds for any output path, unlike a fixed relative directory.
    /// </summary>
    private static bool IsRunningFromBuildOutput()
    {
        var assemblyName = typeof(Hosting).Assembly.GetName().Name;
        return File.Exists(Path.Combine(AppContext.BaseDirectory, assemblyName + ".staticwebassets.runtime.json"));
    }
}
