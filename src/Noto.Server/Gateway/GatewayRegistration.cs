using Microsoft.AspNetCore.RateLimiting;
using Noto.Server.Config;

namespace Noto.Server.Gateway;

// Everything the integration gateway (OAuth broker, provider fetch proxy, link previews) needs, in one place.
// It is the SSRF-sensitive part of the server, so it is wired separately from sync and accounts: dropping
// AddGateway/MapGateway/AddGatewayRateLimits from ServerHost removes it entirely.
public static class GatewayRegistration
{
    public static IServiceCollection AddGateway(this IServiceCollection services)
    {
        services.AddSingleton<IDnsResolver, SystemDnsResolver>();
        services.AddSingleton<ProviderAllowlist>();
        services.AddSingleton(sp => new SafeFetcher(
            sp.GetRequiredService<IDnsResolver>(),
            SafeFetcher.CreateHandler(),
            sp.GetRequiredService<ServerConfig>()
        ));
        services.AddSingleton<OAuthBroker>();
        services.AddSingleton<OpenGraphFetcher>();
        services.AddSingleton<FetchProxy>();
        return services;
    }

    // `userKey` / `ipKey` pick the rate-limit partition, so the gateway reuses the host's keying rules.
    public static void AddGatewayRateLimits(
        this RateLimiterOptions o,
        ServerConfig cfg,
        Action<RateLimiterOptions, string, int, Func<HttpContext, string>> policy
    )
    {
        var l = cfg.Limits;
        policy(
            o,
            "gateway-fetch",
            l.GatewayFetchPerMinute,
            http => "user:" + http.User.FindFirst("sub")?.Value
        );
        policy(
            o,
            "gateway-opengraph",
            l.OpenGraphPerMinute,
            http => "user:" + http.User.FindFirst("sub")?.Value
        );
        policy(
            o,
            "gateway-oauth",
            l.GatewayOAuthPerMinute,
            http => "ip:" + http.Connection.RemoteIpAddress
        );
    }
}
