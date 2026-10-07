using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Noto.Server.Auth;
using Noto.Server.Config;
using Noto.Server.Data;
using Noto.Server.Endpoints;
using Noto.Server.Gateway;
using Noto.Server.Middleware;
using Noto.Server.Sync;

namespace Noto.Server;

public static class ServerHost
{
    static readonly Dictionary<string, string> SwitchMappings = new()
    {
        ["--db"] = "DB", ["--data-dir"] = "DATA_DIR", ["--port"] = "PORT",
    };

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddCommandLine(args, SwitchMappings);
        ConfigureServices(builder.Services);
        if (builder.Configuration["PORT"] is { } port) builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        var app = builder.Build();
        app.Services.GetRequiredService<ServerConfig>(); // refuse to start without required secrets
        Configure(app);
        return app;
    }

    // Everything reads ServerConfig lazily so hosts built by tests see their own settings.
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(sp => ServerConfig.Load(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDnsResolver, SystemDnsResolver>();

        services.AddDbContext<ServerDbContext>((sp, o) =>
        {
            var cfg = sp.GetRequiredService<ServerConfig>();
            if (cfg.Db == DbProvider.Postgres) o.UseNpgsql(cfg.ConnectionString);
            else o.UseSqlite(cfg.ConnectionString);
        });

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower);
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<ServerConfig, TimeProvider>((o, cfg, time) =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                // Lifetimes are judged on the injected clock so expiry is testable.
                LifetimeValidator = (notBefore, expires, _, p) =>
                {
                    var now = time.GetUtcNow().UtcDateTime;
                    return expires is not null && expires.Value + p.ClockSkew >= now && (notBefore is null || notBefore.Value - p.ClockSkew <= now);
                },
                IssuerSigningKey = JwtService.Key(cfg),
                ValidIssuer = cfg.PublicUrl.GetLeftPart(UriPartial.Authority),
                ValidAudience = JwtService.Audience,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });
        services.AddAuthorization();

        services.AddCors();
        services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>().Configure<ServerConfig>((o, cfg) =>
        {
            if (cfg.CorsOrigins.Count == 0) return;
            o.AddDefaultPolicy(p => p.WithOrigins([.. cfg.CorsOrigins]).AllowAnyMethod()
                .WithHeaders("Authorization", "Content-Type", "X-Provider-Authorization").SetPreflightMaxAge(TimeSpan.FromHours(1)));
        });

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<ServerConfig>(ConfigureRateLimits);

        services.AddMemoryCache();
        services.AddSingleton<Argon2PasswordHasher>();
        services.AddSingleton<JwtService>();
        services.AddSingleton<RateGate>();
        services.AddSingleton<ServerClock>();
        services.AddSingleton<ProviderAllowlist>();
        services.AddSingleton(sp => new SafeFetcher(sp.GetRequiredService<IDnsResolver>(), SafeFetcher.CreateHandler(), sp.GetRequiredService<ServerConfig>()));
        services.AddSingleton<OAuthBroker>();
        services.AddSingleton<OpenGraphFetcher>();
        services.AddSingleton<FetchProxy>();
        services.AddScoped<AuthService>();
        services.AddScoped<SyncService>();
        services.AddScoped<TombstoneGc>();
        services.AddHostedService<TombstoneGcService>();
    }

    public static void Configure(WebApplication app)
    {
        using (var scope = app.Services.CreateScope())
        {
            var cfg = scope.ServiceProvider.GetRequiredService<ServerConfig>();
            if (cfg.Db == DbProvider.Sqlite) Directory.CreateDirectory(cfg.DataDir);
            // No EF migrations yet: schema evolution will need them before the first breaking change.
            scope.ServiceProvider.GetRequiredService<ServerDbContext>().Database.EnsureCreated();
        }

        if (app.Configuration["TRUST_PROXY"] == "true")
        {
            var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            app.UseForwardedHeaders(forwarded);
        }

        app.UseNotoSecurityHeaders();
        app.UseExceptionHandler();
        app.UseCors();
        app.UseAuthentication();
        app.UseMiddleware<DeviceStatusMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();

        var v1 = app.MapGroup("/v1");
        v1.MapAuth();
        v1.MapSync();
        v1.MapAccount();
        v1.MapGateway();
        v1.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // The web client build, when present, is served same-origin so the strict CSP applies to it.
        var wwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }
    }

    static void ConfigureRateLimits(RateLimiterOptions o, ServerConfig cfg)
    {
        var l = cfg.Limits;
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.OnRejected = async (ctx, ct) =>
        {
            if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await ctx.HttpContext.Response.WriteAsJsonAsync(ApiExceptionHandler.Make(429, "RATE_LIMITED", "Rate limited"), options: null, "application/problem+json", ct);
        };

        Policy(o, "auth", l.AuthPerMinute, http => "ip:" + http.Connection.RemoteIpAddress);
        Policy(o, "sync", l.SyncPerMinute, http => "dev:" + http.User.FindFirst(JwtService.DeviceClaim)?.Value);
        Policy(o, "snapshot", l.SnapshotPerMinute, http => "dev:" + http.User.FindFirst(JwtService.DeviceClaim)?.Value);
        Policy(o, "gateway-fetch", l.GatewayFetchPerMinute, http => "user:" + http.User.FindFirst("sub")?.Value);
        Policy(o, "gateway-opengraph", l.OpenGraphPerMinute, http => "user:" + http.User.FindFirst("sub")?.Value);
        Policy(o, "gateway-oauth", l.GatewayOAuthPerMinute, http => "ip:" + http.Connection.RemoteIpAddress);
        Policy(o, "account", l.AccountPerMinute, http => "user:" + http.User.FindFirst("sub")?.Value);
    }

    static void Policy(RateLimiterOptions o, string name, int perMinute, Func<HttpContext, string> key) =>
        o.AddPolicy(name, http => RateLimitPartition.GetFixedWindowLimiter(key(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        }));
}
