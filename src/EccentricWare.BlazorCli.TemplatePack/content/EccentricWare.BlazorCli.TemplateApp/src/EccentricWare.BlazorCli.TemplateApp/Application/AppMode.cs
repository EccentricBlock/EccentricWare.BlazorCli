namespace EccentricWare.BlazorCli.TemplateApp.Application;

/// <summary>How the current process is running. Lives here so the application layer never depends on Bootstrap.</summary>
public enum AppMode { Command, Worker, Web }
