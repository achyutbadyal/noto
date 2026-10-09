using System.Net;
using System.Security.Cryptography;
using System.Text;
using Noto.Server.Auth;
using Noto.Server.Config;
using Noto.Server.Gateway;
using Noto.Server.Middleware;

namespace Noto.Server.Endpoints;

public static class GatewayEndpoints
{
    public static void MapGateway(this RouteGroupBuilder v1)
    {
        var gw = v1.MapGroup("/gateway");

        var oauth = gw.MapGroup("/oauth/{provider}").RequireRateLimiting("gateway-oauth");

        oauth
            .MapPost(
                "/start",
                (string provider, OAuthStartRequest req, HttpContext http, OAuthBroker broker) =>
                    Results.Ok(broker.Start(http.User.UserId(), provider, req))
            )
            .RequireAuthorization();

        // Browser redirect from the provider: carries no bearer token; the single-use state binds it to the user.
        oauth.MapGet(
            "/callback",
            async (
                string provider,
                string? code,
                string? state,
                string? error,
                HttpContext http,
                OAuthBroker broker,
                ServerConfig config,
                CancellationToken ct
            ) =>
            {
                var result = await broker.CompleteAsync(
                    provider,
                    error is null ? code : null,
                    state,
                    ct
                );
                return result.ReturnTo is null
                    ? PopupResult(http, config, provider, result.OneTimeCode!)
                    : Results.Redirect(LoopbackReturn.Build(result));
            }
        );

        // Which providers this server can sign people in with. The desktop shows only these.
        gw.MapGet("/oauth", (OAuthBroker broker) => Results.Ok(broker.Available()))
            .RequireAuthorization();

        oauth
            .MapPost(
                "/redeem",
                (string provider, RedeemRequest req, HttpContext http, OAuthBroker broker) =>
                    Results.Ok(broker.Redeem(http.User.UserId(), provider, req))
            )
            .RequireAuthorization();

        oauth
            .MapPost(
                "/refresh",
                async (
                    string provider,
                    RefreshRequest req,
                    OAuthBroker broker,
                    CancellationToken ct
                ) => Results.Ok(await broker.RefreshAsync(provider, req, ct))
            )
            .RequireAuthorization();

        gw.MapPost(
                "/fetch",
                async (
                    FetchRequest req,
                    HttpContext http,
                    FetchProxy proxy,
                    RateGate gate,
                    ServerConfig config,
                    CancellationToken ct
                ) =>
                {
                    var userId = http.User.UserId();
                    var limits = config.Limits;
                    // Per-provider cap on top of the per-user policy.
                    if (
                        !gate.TryAcquire(
                            $"fetch:{userId}:{req.ProviderId}",
                            limits.GatewayFetchPerProviderPerMinute,
                            TimeSpan.FromMinutes(1)
                        )
                    )
                        throw new ApiException(
                            429,
                            "RATE_LIMITED",
                            "Too many requests for this provider"
                        );

                    var response = await proxy.FetchAsync(
                        req,
                        http.Request.Headers["X-Provider-Authorization"].FirstOrDefault(),
                        ct
                    );
                    return Results.Ok(response);
                }
            )
            .RequireAuthorization()
            .RequireRateLimiting("gateway-fetch");

        gw.MapPost(
                "/opengraph",
                async (OpenGraphRequest req, OpenGraphFetcher og, CancellationToken ct) =>
                    Results.Ok(await og.FetchAsync(req.Url, ct))
            )
            .RequireAuthorization()
            .RequireRateLimiting("gateway-opengraph");
    }

    public sealed record OpenGraphRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("url")] string? Url
    );

    // Hands the one-time code to the opener window. A per-response nonce keeps the page compatible with the strict CSP.
    static IResult PopupResult(
        HttpContext http,
        ServerConfig config,
        string provider,
        string oneTimeCode
    )
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var origin = config.PublicUrl.GetLeftPart(UriPartial.Authority);
        http.Response.Headers["Content-Security-Policy"] =
            $"default-src 'none'; script-src 'nonce-{nonce}'; frame-ancestors 'none'";

        var message = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                type = "noto.oauth",
                provider,
                code = oneTimeCode,
            }
        );
        var html = $$"""
            <!doctype html><meta charset="utf-8"><title>Connected</title>
            <p>Connected. You can close this window.</p>
            <script nonce="{{nonce}}">
              if (window.opener) { window.opener.postMessage({{message}}, {{System.Text.Json.JsonSerializer.Serialize(
                origin
            )}}); window.close(); }
            </script>
            """;
        return Results.Content(html, "text/html", Encoding.UTF8);
    }
}
