using EccentricWare.BlazorCli.TemplateApp.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EccentricWare.BlazorCli.TemplateApp.Tests;

public class LoopbackGuardTests
{
    [Theory]
    [InlineData("http://127.0.0.1", 80, true)]        // default port is omitted from Origin
    [InlineData("http://localhost", 80, true)]
    [InlineData("http://127.0.0.1:80", 80, false)]    // browsers never send an explicit default port
    [InlineData("http://127.0.0.1", 5080, false)]
    [InlineData("http://127.0.0.1:5080", 80, false)]
    public void OriginCheckHandlesTheDefaultHttpPort(string origin, int port, bool allowed) =>
        Assert.Equal(allowed, LoopbackGuard.IsAllowedOrigin(origin, port));

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.1:5080", true)]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST:5080", true)]
    [InlineData("127.0.0.1:5081", false)]      // wrong explicit port
    [InlineData("localhost:1", false)]
    [InlineData("rebind.attacker.example", false)]
    [InlineData("rebind.attacker.example:5080", false)]
    [InlineData("127.0.0.1.attacker.example", false)]
    [InlineData("localhost.attacker.example", false)]
    [InlineData("[::1]", false)]               // the listener is IPv4 loopback only
    [InlineData("0.0.0.0", false)]
    [InlineData("127.0.0.2", false)]
    [InlineData("", false)]
    public void HostCheckAcceptsOnlyTheBoundLoopbackNames(string host, bool allowed) =>
        Assert.Equal(allowed, LoopbackGuard.IsAllowedHost(new HostString(host), 5080));

    [Theory]
    [InlineData(null, true)]                      // same-origin navigation and non-browser clients
    [InlineData("http://127.0.0.1:5080", true)]
    [InlineData("http://localhost:5080", true)]
    [InlineData("HTTP://LOCALHOST:5080", true)]
    [InlineData("http://evil.example", false)]
    [InlineData("https://127.0.0.1:5080", false)] // wrong scheme
    [InlineData("http://127.0.0.1:5081", false)]  // another local service
    [InlineData("http://127.0.0.1", false)]       // implicit port 80
    [InlineData("http://127.0.0.1:5080.evil.example", false)]
    [InlineData("http://127.0.0.1:5080, http://evil.example", false)] // joined duplicate headers
    [InlineData("null", false)]                   // sandboxed iframes and file:// pages
    [InlineData("", false)]
    public void OriginCheckRejectsEverythingExceptThisListener(string? origin, bool allowed) =>
        Assert.Equal(allowed, LoopbackGuard.IsAllowedOrigin(origin, 5080));
}
