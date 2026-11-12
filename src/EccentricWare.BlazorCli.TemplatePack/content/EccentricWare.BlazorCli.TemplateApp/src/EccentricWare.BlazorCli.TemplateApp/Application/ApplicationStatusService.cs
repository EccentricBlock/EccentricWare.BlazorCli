using System.Reflection;

namespace EccentricWare.BlazorCli.TemplateApp.Application;

public sealed class ApplicationStatusService : IApplicationStatusService
{
    private static readonly Assembly ApplicationAssembly = typeof(ApplicationStatusService).Assembly;
    private static readonly string ApplicationName = ApplicationAssembly.GetName().Name ?? "Application";
    private static readonly string ApplicationVersion = FormatVersion(
        ApplicationAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        ApplicationAssembly.GetName().Version);

    public ApplicationStatusDto GetStatus(AppMode mode)
    {
        var modeName = mode switch
        {
            AppMode.Command => "cli",
            AppMode.Worker => "run",
            AppMode.Web => "web",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        return new ApplicationStatusDto(ApplicationName, ApplicationVersion, modeName, "healthy");
    }

    /// <summary>
    /// Prefers the informational version so pre-release labels survive (1.2.0-beta.1), and drops the
    /// "+commit" build metadata the SDK may append. Falls back to the numeric assembly version.
    /// </summary>
    public static string FormatVersion(string? informationalVersion, Version? assemblyVersion)
    {
        var plus = informationalVersion?.IndexOf('+') ?? -1;
        var trimmed = (plus >= 0 ? informationalVersion![..plus] : informationalVersion)?.Trim();
        if (!string.IsNullOrEmpty(trimmed)) return trimmed;
        return assemblyVersion is null
            ? "0.0.0"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(assemblyVersion.Build, 0)}";
    }
}
