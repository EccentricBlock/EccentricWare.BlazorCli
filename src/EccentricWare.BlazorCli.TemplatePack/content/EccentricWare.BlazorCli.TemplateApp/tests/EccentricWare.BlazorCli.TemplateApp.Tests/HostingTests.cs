using EccentricWare.BlazorCli.TemplateApp.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EccentricWare.BlazorCli.TemplateApp.Tests;

public sealed class HostingTests
{
    private sealed class FaultingWorker : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            throw new InvalidOperationException("worker crashed");
    }

    /// <summary>Stops the host once every other service has started, simulating Ctrl+C.</summary>
    private sealed class StopWhenStarted(IHostApplicationLifetime lifetime) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            lifetime.StopApplication();
            return Task.CompletedTask;
        }
    }

    private static IHost BuildHost(Action<IServiceCollection> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        configure(builder.Services);
        return builder.Build();
    }

    [Fact]
    public async Task FaultedWorkerMakesTheProcessExitUnexpected()
    {
        // Regression: the generic host stops after a worker crash and RunAsync returns normally,
        // so `run` and `--web` used to exit 0 after a failure.
        using var host = BuildHost(services => services.AddHostedService<FaultingWorker>());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var exitCode = await Hosting.RunToCompletionAsync(host).WaitAsync(timeout.Token);
        Assert.Equal(ApplicationEntryPoint.ExitUnexpected, exitCode);
    }

    [Fact]
    public async Task CleanShutdownExitsSuccessfully()
    {
        using var host = BuildHost(services =>
        {
            services.AddApplicationServices();
            services.AddApplicationWorkers();
            services.AddHostedService<StopWhenStarted>();
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var exitCode = await Hosting.RunToCompletionAsync(host).WaitAsync(timeout.Token);
        Assert.Equal(ApplicationEntryPoint.ExitSuccess, exitCode);
    }

    [Fact]
    public async Task RejectsNullHost() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Hosting.RunToCompletionAsync(null!));

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public async Task WebHostRejectsInvalidPortBeforeBuildingAnything(int port) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Hosting.RunWebAsync(port));

    [Fact]
    public void WorkersAreRegisteredOnceThroughTheSharedExtension()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService)); // one-shot path starts nothing
        services.AddApplicationWorkers();
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
    }
}
