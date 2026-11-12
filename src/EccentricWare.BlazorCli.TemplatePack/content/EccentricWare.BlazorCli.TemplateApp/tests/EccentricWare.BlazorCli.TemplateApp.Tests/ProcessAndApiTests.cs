using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EccentricWare.BlazorCli.TemplateApp.Application;
using Xunit;

namespace EccentricWare.BlazorCli.TemplateApp.Tests;

/// <summary>Black-box smoke tests execute the generated assembly, not only parser methods.</summary>
public sealed class ProcessAndApiTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);

    /// <summary>A deadline that also fires when the test run is cancelled.</summary>
    private static CancellationTokenSource Within(TimeSpan limit)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        source.CancelAfter(limit);
        return source;
    }

    [Theory]
    [InlineData("--help", 0)]
    [InlineData("--version", 0)]
    [InlineData("status", 0)]
    [InlineData("status --json", 0)]
    [InlineData("--web status", 2)]
    [InlineData("--urls http://0.0.0.0:80", 2)]
    [InlineData("--port 5081", 2)]
    public async Task OneShotProcessesExitWithExpectedCode(string argumentLine, int code)
    {
        using var process = StartProcess(argumentLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        using var timeout = Within(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None); // reap the child even when the run is cancelled
            }
        }
        var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = await process.StandardError.ReadToEndAsync(timeout.Token);
        Assert.Equal(code, process.ExitCode);
        // Streams stay separate: results on stdout, diagnostics on stderr, never both for one outcome.
        if (code == 0) Assert.Empty(error);
        else
        {
            Assert.Empty(output);
            Assert.StartsWith("Usage error:", error);
            Assert.Contains("Usage: ", error);
        }
        if (argumentLine == "--help") Assert.StartsWith($"Usage: {typeof(ApplicationStatusService).Assembly.GetName().Name} ", output);
        if (argumentLine == "--version")
            Assert.Equal(new ApplicationStatusService().GetStatus(AppMode.Command).Version, output.Trim());
        if (argumentLine == "status --json")
        {
            using var json = JsonDocument.Parse(output);
            Assert.Equal("cli", json.RootElement.GetProperty("mode").GetString());
            Assert.Empty(error);
            Assert.Equal(4, json.RootElement.EnumerateObject().Count());
        }
    }

    [Fact]
    public async Task WebServesReadOnlyApiAndRejectsHostileHostHeaders()
    {
        var port = ReservePort();
        var webOutput = new StringBuilder();
        using var process = StartProcess(["--web", "--port", port.ToString()], hostileEnvironment: true, drainOutput: webOutput);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var uri = new Uri($"http://127.0.0.1:{port}/api/v1/status");
            await WaitForApiAsync(client, uri, process);
            using var success = await client.GetAsync(uri, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            Assert.Contains("no-store", success.Headers.CacheControl?.ToString() ?? "");
            Assert.Equal("nosniff", success.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", success.Headers.GetValues("X-Frame-Options").Single());
            using var json = JsonDocument.Parse(await success.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("web", json.RootElement.GetProperty("mode").GetString());
            Assert.Equal(4, json.RootElement.EnumerateObject().Count());

            using var missing = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/not-found", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var forbiddenMethod = await client.PostAsync(uri, null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, forbiddenMethod.StatusCode);
            using var hostileRequest = new HttpRequestMessage(HttpMethod.Get, uri);
            hostileRequest.Headers.Host = "rebind.attacker.example";
            using var rejected = await client.SendAsync(hostileRequest, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var wrongPortRequest = new HttpRequestMessage(HttpMethod.Get, uri);
            wrongPortRequest.Headers.Host = $"localhost:{port + 1}";
            using var wrongPortResponse = await client.SendAsync(wrongPortRequest, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, wrongPortResponse.StatusCode);
            using var html = await client.GetAsync($"http://127.0.0.1:{port}/", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, html.StatusCode);
            var document = await html.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("MudBlazor", document);
            Assert.Contains("blazor.web", document);
            Assert.DoesNotContain("fonts.googleapis.com", document);
            // Blazor's own default ("frame-ancestors 'self'") would contradict X-Frame-Options: DENY.
            Assert.Equal("frame-ancestors 'none'", html.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Equal("DENY", html.Headers.GetValues("X-Frame-Options").Single());

            await AssertCrossOriginPagesCannotReachTheApplicationAsync(client, port);
            Assert.Contains("Sample worker started", Snapshot(webOutput)); // --web runs the same worker as `run`

            // Explicit localhost-only bind: 127.0.0.2 must not accept TCP on Linux.
            if (OperatingSystem.IsLinux())
            {
                using var socket = new TcpClient();
                using var cancellation = Within(TimeSpan.FromMilliseconds(500));
                await Assert.ThrowsAnyAsync<Exception>(async () =>
                    await socket.ConnectAsync(IPAddress.Parse("127.0.0.2"), port, cancellation.Token));
            }

            if (!OperatingSystem.IsWindows())
            {
                using var terminate = Process.Start(new ProcessStartInfo("kill", $"-TERM {process.Id}") { UseShellExecute = false })!;
                using var shutdown = Within(Timeout);
                await terminate.WaitForExitAsync(shutdown.Token);
                await process.WaitForExitAsync(shutdown.Token);
                Assert.Equal(0, process.ExitCode);
                Assert.Contains("Sample worker stopped", Snapshot(webOutput));
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None); // reap the child even when the run is cancelled
        }
    }

    /// <summary>A page on another site can open http://127.0.0.1:port, so Origin must be checked as well as Host.</summary>
    private static async Task AssertCrossOriginPagesCannotReachTheApplicationAsync(HttpClient client, int port)
    {
        var negotiateUri = $"http://127.0.0.1:{port}/_blazor/negotiate?negotiateVersion=1";
        foreach (var hostile in new[] { "http://evil.example", "null", $"http://127.0.0.1:{port + 1}" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, negotiateUri);
            request.Headers.TryAddWithoutValidation("Origin", hostile);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var apiRequest = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/v1/status");
        apiRequest.Headers.TryAddWithoutValidation("Origin", "http://evil.example");
        using var apiResponse = await client.SendAsync(apiRequest);
        Assert.Equal(HttpStatusCode.Forbidden, apiResponse.StatusCode);

        // The page's own origin keeps working, including the WebSocket handshake that carries the circuit.
        using var sameOrigin = new HttpRequestMessage(HttpMethod.Post, negotiateUri);
        sameOrigin.Headers.TryAddWithoutValidation("Origin", $"http://localhost:{port}");
        using var negotiated = await client.SendAsync(sameOrigin);
        Assert.Equal(HttpStatusCode.OK, negotiated.StatusCode);
        using var negotiation = JsonDocument.Parse(await negotiated.Content.ReadAsStringAsync());
        var token = negotiation.RootElement.GetProperty("connectionToken").GetString();
        var socketUri = new Uri($"ws://127.0.0.1:{port}/_blazor?id={Uri.EscapeDataString(token!)}");

        using var hostileSocket = new ClientWebSocket();
        hostileSocket.Options.SetRequestHeader("Origin", "http://evil.example");
        using var hostileTimeout = Within(Timeout);
        await Assert.ThrowsAsync<WebSocketException>(() => hostileSocket.ConnectAsync(socketUri, hostileTimeout.Token));

        using var friendlySocket = new ClientWebSocket();
        friendlySocket.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{port}");
        using var friendlyTimeout = Within(Timeout);
        await friendlySocket.ConnectAsync(socketUri, friendlyTimeout.Token);
        Assert.Equal(WebSocketState.Open, friendlySocket.State);
    }

    [Fact]
    public async Task RunModeStartsWithoutHttpAndStopsCleanlyOnTerminate()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "No portable way to signal a child process here; smoke_publish.py covers Windows termination.");
        var output = new StringBuilder();
        using var process = StartProcess(["run"], hostileEnvironment: true, drainOutput: output);
        try
        {
            using var timeout = Within(Timeout);
            while (!Snapshot(output).Contains("Sample worker started"))
            {
                if (process.HasExited) Assert.Fail($"run exited early with {process.ExitCode}: {Snapshot(output)}");
                await Task.Delay(100, timeout.Token);
            }
            // SIGTERM is what systemd and Docker send. SIGINT is not used: a child of a background job
            // (`cmd &`, nohup, some CI wrappers) inherits an ignored SIGINT and would never stop.
            using var signal = Process.Start(new ProcessStartInfo("kill", $"-TERM {process.Id}") { UseShellExecute = false })!;
            await signal.WaitForExitAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("Sample worker stopped", Snapshot(output));
            Assert.DoesNotContain("Now listening", Snapshot(output)); // Kestrel's startup message
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None); // reap the child even when the run is cancelled
        }
    }

    private static string Snapshot(StringBuilder output)
    {
        lock (output) return output.ToString();
    }

    [Fact]
    public async Task PortConflictProducesStartupFailure()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var process = StartProcess(["--web", "--port", port.ToString()]);
        using var timeout = Within(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None); // reap the child even when the run is cancelled
            }
        }
        Assert.Equal(3, process.ExitCode);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForApiAsync(HttpClient client, Uri url, Process process)
    {
        using var timeout = Within(Timeout);
        while (!timeout.IsCancellationRequested)
        {
            if (process.HasExited) throw new InvalidOperationException($"Web process exited with code {process.ExitCode}");
            try
            {
                using var response = await client.GetAsync(url, timeout.Token);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!timeout.IsCancellationRequested) { }
            await Task.Delay(150, timeout.Token);
        }
        throw new TimeoutException("Web app did not become ready.");
    }

    private static Process StartProcess(string[] args, bool hostileEnvironment = false, StringBuilder? drainOutput = null)
    {
        var name = typeof(ApplicationStatusService).Assembly.GetName().Name!;
        var bin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = bin.Parent!.Name;
        var solutionRoot = Path.GetFullPath(Path.Combine(bin.FullName, "..", "..", "..", "..", ".."));
        var dll = Path.Combine(solutionRoot, "src", name, "bin", configuration, "net10.0", name + ".dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Build generated solution before executing process tests.", dll);
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath(), // Intentionally unrelated to project or published assets.
        };
        info.ArgumentList.Add(dll);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        if (hostileEnvironment)
        {
            info.Environment["ASPNETCORE_URLS"] = "http://0.0.0.0:56789";
            info.Environment["ASPNETCORE_HTTP_PORTS"] = "56800";
            info.Environment["Kestrel__Endpoints__Injected__Url"] = "http://0.0.0.0:56801";
        }
        var process = Process.Start(info) ?? throw new InvalidOperationException("Failed to start process.");
        if (drainOutput is not null)
        {
            // Long-running servers log continuously. An unread redirected pipe eventually blocks the child.
            DataReceivedEventHandler append = (_, e) => { lock (drainOutput) drainOutput.AppendLine(e.Data); };
            process.OutputDataReceived += append;
            process.ErrorDataReceived += append;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        return process;
    }
}
