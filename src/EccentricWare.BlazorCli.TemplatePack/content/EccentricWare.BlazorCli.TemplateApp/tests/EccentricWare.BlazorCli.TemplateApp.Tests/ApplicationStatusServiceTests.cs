using System.Text.Json;
using EccentricWare.BlazorCli.TemplateApp.Application;
using EccentricWare.BlazorCli.TemplateApp.Cli;
using Xunit;

namespace EccentricWare.BlazorCli.TemplateApp.Tests;

public sealed class ApplicationStatusServiceTests
{
    private readonly ApplicationStatusService _service = new();

    // Derived from the assembly (not a literal) so a consumer can change <Version> without breaking tests.
    private static string ExpectedVersion => typeof(ApplicationStatusService).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single()
        .InformationalVersion.Split('+')[0];

    [Theory]
    [InlineData(AppMode.Command, "cli")]
    [InlineData(AppMode.Worker, "run")]
    [InlineData(AppMode.Web, "web")]
    public void ReturnsKnownPublicFields(AppMode mode, string expectedMode)
    {
        var dto = _service.GetStatus(mode);
        Assert.Equal(typeof(ApplicationStatusService).Assembly.GetName().Name, dto.Application);
        Assert.Equal(ExpectedVersion, dto.Version);
        Assert.Equal(expectedMode, dto.Mode);
        Assert.Equal("healthy", dto.Status);
        Assert.Equal(4, typeof(ApplicationStatusDto).GetProperties().Length);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+abcdef0", "1.2.3")]
    [InlineData("1.2.3-beta.1+abcdef0", "1.2.3-beta.1")]
    [InlineData("  2.0.0  ", "2.0.0")]
    [InlineData(null, "1.2.0")]
    [InlineData("", "1.2.0")]
    [InlineData("+onlymetadata", "1.2.0")]
    public void FormatVersionPrefersInformationalVersionWithoutBuildMetadata(string? informational, string expected) =>
        Assert.Equal(expected, ApplicationStatusService.FormatVersion(informational, new Version(1, 2, 0, 0)));

    [Fact]
    public void FormatVersionHandlesMissingAndPartialAssemblyVersions()
    {
        Assert.Equal("0.0.0", ApplicationStatusService.FormatVersion(null, null));
        Assert.Equal("3.1.0", ApplicationStatusService.FormatVersion(null, new Version(3, 1))); // Build is -1
    }

    [Fact]
    public void ThrowsForUnknownMode() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetStatus((AppMode)999));

    [Fact]
    public void ConcurrentAccessIsConsistent()
    {
        var statuses = Enumerable.Range(0, 256).AsParallel()
            .Select(_ => _service.GetStatus(AppMode.Web)).ToArray();
        Assert.Single(statuses.Distinct());
    }

    [Fact]
    public void JsonCommandWritesExactlyOneSanitisedObject()
    {
        using var writer = new StringWriter();
        StatusCommand.Execute(_service, true, writer);
        var text = writer.ToString();
        Assert.Equal(1, text.Count(c => c == '\n'));
        using var json = JsonDocument.Parse(text);
        Assert.Equal(4, json.RootElement.EnumerateObject().Count());
        Assert.Equal("cli", json.RootElement.GetProperty("mode").GetString());
        Assert.False(text.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TextCommandProducesHumanReadableOutput()
    {
        using var writer = new StringWriter();
        StatusCommand.Execute(_service, false, writer);
        Assert.Contains("(cli): healthy", writer.ToString());
    }

    [Fact]
    public void CommandRejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => StatusCommand.Execute(null!, false, TextWriter.Null));
        Assert.Throws<ArgumentNullException>(() => StatusCommand.Execute(_service, false, null!));
    }
}
