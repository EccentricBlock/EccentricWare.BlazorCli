using System.Net.Sockets;
using EccentricWare.BlazorCli.TemplateApp.Application;
using EccentricWare.BlazorCli.TemplateApp.Bootstrap;
using Xunit;

namespace EccentricWare.BlazorCli.TemplateApp.Tests;

public class ApplicationEntryPointTests
{
    [Theory]
    [InlineData(typeof(IOException), AppMode.Web, ApplicationEntryPoint.ExitStartup)]
    [InlineData(typeof(SocketException), AppMode.Web, ApplicationEntryPoint.ExitStartup)]
    [InlineData(typeof(UnauthorizedAccessException), AppMode.Worker, ApplicationEntryPoint.ExitStartup)]
    [InlineData(typeof(InvalidOperationException), AppMode.Web, ApplicationEntryPoint.ExitStartup)]
    [InlineData(typeof(IOException), AppMode.Command, ApplicationEntryPoint.ExitUnexpected)]
    [InlineData(typeof(UnauthorizedAccessException), AppMode.Command, ApplicationEntryPoint.ExitUnexpected)]
    [InlineData(typeof(InvalidOperationException), AppMode.Worker, ApplicationEntryPoint.ExitStartup)]
    // An InvalidOperationException inside a one-shot command is a bug, not a startup problem.
    [InlineData(typeof(InvalidOperationException), AppMode.Command, ApplicationEntryPoint.ExitUnexpected)]
    // Framework or programming errors must not be reported as an "expected" operation failure.
    [InlineData(typeof(ArgumentException), AppMode.Command, ApplicationEntryPoint.ExitUnexpected)]
    [InlineData(typeof(ArgumentOutOfRangeException), AppMode.Web, ApplicationEntryPoint.ExitUnexpected)]
    [InlineData(typeof(NullReferenceException), AppMode.Command, ApplicationEntryPoint.ExitUnexpected)]
    public void ClassifiesExceptionsToDocumentedExitCodes(Type type, AppMode mode, int expected)
    {
        var exception = (Exception)Activator.CreateInstance(type)!;
        Assert.Equal(expected, ApplicationEntryPoint.Classify(exception, mode).ExitCode);
    }

    [Fact]
    public void ExpectedOperationFailureExitsFourWithItsOwnMessage()
    {
        var (code, message) = ApplicationEntryPoint.Classify(new ExpectedOperationException("Input file is empty."), AppMode.Command);
        Assert.Equal(ApplicationEntryPoint.ExitOperation, code);
        Assert.Equal("Error: Input file is empty.", message);
    }

    [Fact]
    public void UnexpectedExceptionTextIsNeverEchoed()
    {
        var secret = "token=hunter2 /home/alice/.config";
        foreach (var exception in new Exception[] { new IOException(secret), new InvalidOperationException(secret), new ArgumentException(secret) })
            Assert.DoesNotContain("hunter2", ApplicationEntryPoint.Classify(exception, AppMode.Web).Message);
    }
}
