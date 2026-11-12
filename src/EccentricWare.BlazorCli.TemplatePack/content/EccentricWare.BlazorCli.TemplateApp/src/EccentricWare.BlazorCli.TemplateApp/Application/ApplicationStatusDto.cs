namespace EccentricWare.BlazorCli.TemplateApp.Application;

/// <summary>Explicitly allowlisted, non-sensitive status fields exposed to CLI and localhost API.</summary>
public sealed record ApplicationStatusDto(string Application, string Version, string Mode, string Status);
