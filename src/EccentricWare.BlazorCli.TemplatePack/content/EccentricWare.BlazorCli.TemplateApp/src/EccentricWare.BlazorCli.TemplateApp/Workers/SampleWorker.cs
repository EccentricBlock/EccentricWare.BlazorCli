using EccentricWare.BlazorCli.TemplateApp.Application;

namespace EccentricWare.BlazorCli.TemplateApp.Workers;

/// <summary>Non-privileged placeholder work loop; never binds a socket or executes commands.</summary>
public sealed class SampleWorker(
    ILogger<SampleWorker> logger,
    IApplicationStatusService statusService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var status = statusService.GetStatus(AppMode.Worker);
        logger.LogInformation("Sample worker started for {Application}", status.Application);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                logger.LogDebug("Sample worker heartbeat");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A normal, cooperative shutdown.
        }
        finally
        {
            logger.LogInformation("Sample worker stopped");
        }
    }
}
