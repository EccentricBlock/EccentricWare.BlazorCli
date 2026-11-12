using EccentricWare.BlazorCli.TemplateApp.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EccentricWare.BlazorCli.TemplateApp.Web;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/status", (HttpContext context, IApplicationStatusService status) =>
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            return Results.Json(status.GetStatus(AppMode.Web));
        });
        return endpoints;
    }
}
