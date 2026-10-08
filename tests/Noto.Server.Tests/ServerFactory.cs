using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Noto.Server.Gateway;

namespace Noto.Server.Tests;

public sealed class FakeDns : IDnsResolver
{
    public Dictionary<string, IPAddress[]> Records { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
        IPAddress.TryParse(host, out var ip)
            ? Task.FromResult(new[] { ip })
            : Task.FromResult(
                Records.TryGetValue(host, out var a) ? a : [IPAddress.Parse("93.184.216.34")]
            );
}

// Handler that answers gateway upstream calls in-process; records what it was asked.
public sealed class FakeUpstream : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    // Request bodies are captured on arrival: the caller disposes the content right after sending.
    public List<string> Bodies { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        return Respond(request);
    }
}

public sealed class ServerFactory : WebApplicationFactory<Program>
{
    readonly Dictionary<string, string?> _settings;
    readonly string _dataDir = Path.Combine(
        Path.GetTempPath(),
        "noto-test-" + Guid.NewGuid().ToString("N")
    );

    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);
    public FakeDns Dns { get; } = new();
    public FakeUpstream Upstream { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();

    public ServerFactory(Dictionary<string, string?>? overrides = null)
    {
        _settings = new Dictionary<string, string?>
        {
            ["JWT_SIGNING_KEY"] = "test-signing-key-that-is-long-enough-for-hs256!",
            ["PUBLIC_URL"] = "https://noto.test",
            ["DB"] = "sqlite",
            ["DATA_DIR"] = _dataDir,
            ["REGISTRATION"] = "open",
            // Cheap hashing and generous limits; individual tests tighten what they exercise.
            ["ARGON2_MEMORY_KB"] = "64",
            ["ARGON2_ITERATIONS"] = "1",
            ["ARGON2_PARALLELISM"] = "1",
            ["RATE_AUTH_PER_MIN"] = "1000",
            ["RATE_SYNC_PER_MIN"] = "1000",
            ["RATE_SNAPSHOT_PER_MIN"] = "1000",
            ["RATE_ACCOUNT_PER_MIN"] = "1000",
            ["RATE_GATEWAY_FETCH_PER_MIN"] = "1000",
            ["RATE_GATEWAY_FETCH_PROVIDER_PER_MIN"] = "1000",
            ["RATE_OPENGRAPH_PER_MIN"] = "1000",
            ["RATE_GATEWAY_OAUTH_PER_MIN"] = "1000",
            ["GITHUB_CLIENT_ID"] = "gh-client",
            ["GITHUB_CLIENT_SECRET"] = "gh-secret",
            ["GITLAB_CLIENT_ID"] = "gl-client",
            ["GITLAB_CLIENT_SECRET"] = "gl-secret",
        };
        foreach (var (k, v) in overrides ?? [])
            _settings[k] = v;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(_settings));
        builder.ConfigureServices(services =>
        {
            services.AddLogging(l =>
            {
                l.ClearProviders();
                l.AddProvider(Logs);
                l.SetMinimumLevel(LogLevel.Trace);
            });
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time));
            services.Replace(ServiceDescriptor.Singleton<IDnsResolver>(Dns));
            services.Replace(
                ServiceDescriptor.Singleton(sp => new SafeFetcher(
                    Dns,
                    Upstream,
                    sp.GetRequiredService<Config.ServerConfig>()
                ))
            );
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(_dataDir))
                Directory.Delete(_dataDir, true);
        }
        catch (IOException) { }
    }

    // --- helpers -----------------------------------------------------------------------------

    public sealed record Session(
        HttpClient Client,
        Guid UserId,
        Guid DeviceId,
        string AccessToken,
        string RefreshToken
    );

    public static object Device(Guid id) =>
        new
        {
            id,
            name = "Test laptop",
            platform = "macos",
        };

    public async Task<Session> RegisterAsync(string? email = null, Guid? device = null)
    {
        var client = CreateClient();
        var deviceId = device ?? Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            "/v1/auth/register",
            new
            {
                email = email ?? $"{Guid.NewGuid():N}@example.com",
                password = "correct horse battery",
                device = Device(deviceId),
            }
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var access = json.GetProperty("access_token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            access
        );
        return new Session(
            client,
            json.GetProperty("user_id").GetGuid(),
            deviceId,
            access,
            json.GetProperty("refresh_token").GetString()!
        );
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<string> Messages { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Capture(this);

    public void Dispose() { }

    sealed class Capture(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => owner.Messages.Add(formatter(state, exception) + exception);
    }
}
