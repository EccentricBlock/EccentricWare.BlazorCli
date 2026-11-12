using EccentricWare.BlazorCli.TemplateApp.Application;
using EccentricWare.BlazorCli.TemplateApp.Bootstrap;
using Xunit;

namespace EccentricWare.BlazorCli.TemplateApp.Tests;

public class CommandLineParserTests
{
    [Theory]
    [InlineData("", AppCommand.Help, AppMode.Command)]
    [InlineData("-h", AppCommand.Help, AppMode.Command)]
    [InlineData("--help", AppCommand.Help, AppMode.Command)]
    [InlineData("--version", AppCommand.Version, AppMode.Command)]
    [InlineData("status", AppCommand.Status, AppMode.Command)]
    [InlineData("run", AppCommand.Run, AppMode.Worker)]
    [InlineData("--web", AppCommand.Web, AppMode.Web)]
    public void RecognisesValidModes(string commandLine, AppCommand command, AppMode mode)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = CommandLineParser.Parse(args);
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(command, result.Invocation!.Command);
        Assert.Equal(mode, result.Invocation.Mode);
    }

    [Fact]
    public void JsonStatusIsSeparateFromPlainText()
    {
        var result = CommandLineParser.Parse(["status", "--json"]);
        Assert.True(result.IsValid);
        Assert.True(result.Invocation!.Json);
        Assert.False(CommandLineParser.Parse(["status"]).Invocation!.Json);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("5081", 5081)]
    [InlineData("65535", 65535)]
    [InlineData("00080", 80)]
    public void AcceptsDecimalPortInRange(string input, int expected)
    {
        var result = CommandLineParser.Parse(["--web", "--port", input]);
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Invocation!.Port);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 80")]
    [InlineData("80 ")]
    [InlineData("1.2")]
    [InlineData("0x10")]
    [InlineData("999999999999999999999999999")]
    [InlineData("")]
    [InlineData("１２３")]
    public void RejectsMalformedOrOutOfRangePort(string port) =>
        Assert.False(CommandLineParser.Parse(["--web", "--port", port]).IsValid);

    [Theory]
    [InlineData("--port 8080")]
    [InlineData("--urls http://0.0.0.0:80")]
    [InlineData("--web status")]
    [InlineData("--web --help")]
    [InlineData("--web --port")]
    [InlineData("run --web")]
    [InlineData("status --web")]
    [InlineData("status --json --json")]
    [InlineData("--web --port 80 --port 90")]
    [InlineData("-- --web")]
    [InlineData("--web -- --port 80")]
    [InlineData("--version --web")]
    [InlineData("--help --version")]
    [InlineData("STATUS")]
    [InlineData("--json")]
    public void RejectsUnexpectedCombinations(string commandLine) =>
        Assert.False(CommandLineParser.Parse(commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)).IsValid);

    [Fact]
    public void FailsClosedOnNullOrOversizedInput()
    {
        Assert.False(CommandLineParser.Parse(null).IsValid);
        Assert.False(CommandLineParser.Parse(Enumerable.Repeat("status", 17).ToArray()).IsValid);
        Assert.False(CommandLineParser.Parse([new string('X', 257)]).IsValid);
        Assert.False(CommandLineParser.Parse([null!]).IsValid);
    }

    [Theory]
    [InlineData(AppCommand.Help, AppMode.Command)]
    [InlineData(AppCommand.Version, AppMode.Command)]
    [InlineData(AppCommand.Status, AppMode.Command)]
    [InlineData(AppCommand.Run, AppMode.Worker)]
    [InlineData(AppCommand.Web, AppMode.Web)]
    public void ModeFollowsFromTheCommand(AppCommand command, AppMode mode) =>
        Assert.Equal(mode, new AppInvocation(command).Mode);

    [Fact]
    public void EveryCommandHasAMode()
    {
        // Fails when a command is added to AppCommand without a mode in AppInvocation.Mode.
        foreach (var command in Enum.GetValues<AppCommand>())
            Assert.True(Enum.IsDefined(new AppInvocation(command).Mode), command.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppInvocation((AppCommand)999).Mode);
    }

    [Fact]
    public void WebInvocationDefaultsToTheDocumentedPort()
    {
        Assert.Equal(CommandLineParser.DefaultPort, CommandLineParser.Parse(["--web"]).Invocation!.Port);
        Assert.Contains($"default port {CommandLineParser.DefaultPort}", CommandLineParser.Usage);
    }

    [Fact]
    public void UsageNamesTheApplicationInsteadOfAPlaceholder()
    {
        var name = typeof(CommandLineParser).Assembly.GetName().Name!;
        Assert.StartsWith($"Usage: {name} [command]", CommandLineParser.Usage);
        Assert.StartsWith("Usage: Tool ", CommandLineParser.BuildUsage("Tool"));
        // Every documented command is listed.
        foreach (var command in new[] { "--help", "--version", "status [--json]", "run", "--web [--port PORT]" })
            Assert.Contains(command, CommandLineParser.Usage);
    }
}
