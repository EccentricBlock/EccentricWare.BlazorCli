namespace EccentricWare.BlazorCli.TemplateApp.Bootstrap;

/// <summary>
/// Throw from a command handler for a failure the user can understand and fix (exit code 4).
/// The message is printed as-is, so never put secrets, paths or inner exception text in it.
/// </summary>
public sealed class ExpectedOperationException(string message) : Exception(message);
