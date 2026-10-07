namespace Noto.Server.Middleware;

// Strict defaults for the web client we serve (docs/05 › Security: browser tokens depend on a strict CSP).
public static class SecurityHeaders
{
    public const string Csp =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; font-src 'self'; connect-src 'self'; worker-src 'self'; " +
        "object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseNotoSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h.TryAdd("Content-Security-Policy", Csp);
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            // Keeps window.opener for the OAuth popup while isolating everything else.
            h["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            if (ctx.Request.Path.StartsWithSegments("/v1")) h["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });
        await next();
    });
}
