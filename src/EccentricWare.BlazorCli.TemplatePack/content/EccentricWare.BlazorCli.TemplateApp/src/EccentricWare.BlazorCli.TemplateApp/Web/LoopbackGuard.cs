using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace EccentricWare.BlazorCli.TemplateApp.Web;

/// <summary>
/// Request filtering for the loopback-only listener. This is defence in depth, not authentication:
/// any local process can still send a request with a correct Host and no Origin.
/// </summary>
public static class LoopbackGuard
{
    /// <summary>Accepts only 127.0.0.1 or localhost, and rejects an explicit port that differs from the bound one.</summary>
    public static bool IsAllowedHost(HostString host, int port) =>
        (string.Equals(host.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(host.Host, "localhost", StringComparison.OrdinalIgnoreCase)) &&
        (host.Port is not int explicitPort || explicitPort == port);

    /// <summary>
    /// A browser attaches Origin to cross-origin requests and WebSocket handshakes. A page from another
    /// site can reach 127.0.0.1 with a valid Host (and open a Blazor circuit), so the Origin must match
    /// this listener. A missing Origin (same-origin navigation, curl, non-browser clients) is allowed.
    /// </summary>
    public static bool IsAllowedOrigin(string? origin, int port)
    {
        if (origin is null) return true;
        // Browsers omit the default port from an Origin, so --port 80 sends "http://127.0.0.1".
        var authority = port == 80 ? "" : $":{port}";
        return string.Equals(origin, $"http://127.0.0.1{authority}", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(origin, $"http://localhost{authority}", StringComparison.OrdinalIgnoreCase);
    }

    public static IApplicationBuilder UseLoopbackGuard(this IApplicationBuilder app, int port) =>
        app.Use(async (context, next) =>
        {
            if (!IsAllowedHost(context.Request.Host, port))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            // Several Origin headers join into one comma-separated string, which never matches.
            string? origin = context.Request.Headers.TryGetValue(HeaderNames.Origin, out var values)
                ? values.ToString()
                : null;
            if (!IsAllowedOrigin(origin, port))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            await next(context);
        });
}
