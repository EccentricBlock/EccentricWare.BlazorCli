using EccentricWare.BlazorCli.TemplateApp.Application;
using EccentricWare.BlazorCli.TemplateApp.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace EccentricWare.BlazorCli.TemplateApp.Bootstrap;

public static class ServiceRegistration
{
    /// <summary>Services shared by every mode, including one-shot commands. Registers no hosted service.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IApplicationStatusService, ApplicationStatusService>();
        return services;
    }

    /// <summary>Background workers for the long-running modes (<c>run</c> and <c>--web</c>). Add new workers here.</summary>
    public static IServiceCollection AddApplicationWorkers(this IServiceCollection services)
    {
        services.AddHostedService<SampleWorker>();
        return services;
    }
}
