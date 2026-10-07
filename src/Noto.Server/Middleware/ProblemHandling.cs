using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Noto.Server.Middleware;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var problem = ex switch
        {
            ApiException a => Make(a.Status, a.Code, a.Title, a.Detail),
            BadHttpRequestException => Make(400, "VALIDATION_ERROR", "Invalid request"),
            System.Text.Json.JsonException => Make(400, "VALIDATION_ERROR", "Invalid JSON"),
            _ => null,
        };

        if (problem is null)
        {
            // Never echo exception text: it can contain request content.
            log.LogError("Unhandled {Type} on {Path}", ex.GetType().Name, ctx.Request.Path);
            problem = Make(500, "SERVER_ERROR", "Server error");
        }

        ctx.Response.StatusCode = problem.Status!.Value;
        await ctx.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            ct
        );
        return true;
    }

    public static ProblemDetails Make(int status, string code, string title, string? detail = null)
    {
        var p = new ProblemDetails
        {
            Type = $"https://noto.app/errors/{code.ToLowerInvariant().Replace('_', '-')}",
            Title = title,
            Status = status,
            Detail = detail,
        };
        p.Extensions["code"] = code;
        return p;
    }
}
