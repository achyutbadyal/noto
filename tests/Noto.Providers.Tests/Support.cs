using System.Reflection;
using Microsoft.Extensions.Logging;
using Noto.Core.Commands;
using Noto.Core.Links;
using Noto.Core.Models;
using Noto.Core.Tests;
using Noto.Data;
using Noto.Providers.Auth;
using Noto.Providers.Preview;
using Noto.Providers.Transport;

namespace Noto.Providers.Tests;

public static class Fixture
{
    public static string Load(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith("." + name, StringComparison.Ordinal));
        using var reader = new StreamReader(asm.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }
}

// Scripted transport: routes by predicate, records every request.
public sealed class FakeHttp : IProviderHttp
{
    readonly List<(Func<ProviderRequest, bool> Match, Func<ProviderRequest, ProviderResponse> Reply)> _routes = [];

    public List<ProviderRequest> Requests { get; } = [];
    public OpenGraphData? OpenGraph { get; set; }

    public FakeHttp On(Func<ProviderRequest, bool> match, string body, int status = 200, IReadOnlyDictionary<string, string>? headers = null)
    {
        _routes.Add((match, _ => new ProviderResponse(status, body, headers ?? new Dictionary<string, string>())));
        return this;
    }

    public FakeHttp OnPath(string pathPrefix, string body, int status = 200) =>
        On(r => r.Path.StartsWith(pathPrefix, StringComparison.Ordinal), body, status);

    public Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        foreach (var (match, reply) in _routes)
            if (match(request)) return Task.FromResult(reply(request));
        return Task.FromResult(new ProviderResponse(404, "{}", new Dictionary<string, string>()));
    }

    public Task<OpenGraphData?> OpenGraphAsync(Uri url, CancellationToken ct) => Task.FromResult(OpenGraph);
}

public sealed class FakeFactory : IProviderHttpFactory
{
    public Dictionary<string, IProviderHttp> ByProvider { get; } = [];
    public List<string> CreatedFor { get; } = [];

    public IProviderHttp Create(IAppProvider provider, string? instanceUrl, AuthMethod method, ICredentialSource credentials)
    {
        CreatedFor.Add(provider.ProviderId);
        return ByProvider[provider.ProviderId];
    }
}

public sealed class CollectingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(formatter(state, exception) + (exception is null ? "" : " | " + exception));
}

// Advances the fake clock instead of sleeping.
public sealed class FakeDelay(FakeClock clock) : IDelay
{
    public List<TimeSpan> Delays { get; } = [];

    public Task DelayAsync(TimeSpan time, CancellationToken ct)
    {
        Delays.Add(time);
        clock.Advance(time);
        return Task.CompletedTask;
    }
}

public sealed class Harness : IDisposable
{
    public SqliteUnitOfWork Uow { get; }
    public FakeClock Clock { get; } = new(DateTimeOffset.Parse("2026-10-07T10:00:00Z"));
    public InMemoryKeyring Keyring { get; } = new();
    public CredentialStore Credentials { get; }
    public FakeFactory Factory { get; } = new();
    public ProviderRegistry Registry { get; }
    public TokenManager Tokens { get; }
    public FakeDelay Delay { get; }
    public CollectingLogger<PreviewService> Log { get; } = new();
    public PreviewService Previews { get; }
    public Workspace Workspace { get; } = Make.Workspace();
    public CommandBus Bus { get; }

    public Harness(IEnumerable<IAppProvider> providers, PreviewOptions? options = null,
        HttpMessageHandler? oauthHandler = null, IReadOnlyDictionary<string, OAuthClient>? oauthClients = null, string? dbPath = null)
    {
        Uow = dbPath is null ? SqliteUnitOfWork.InMemory() : new SqliteUnitOfWork($"Data Source={dbPath}");
        Credentials = new CredentialStore(Keyring);
        Registry = new ProviderRegistry(providers);
        Tokens = new TokenManager(Uow, Credentials, Registry, Clock, oauthHandler is null ? new HttpClient() : new HttpClient(oauthHandler),
            oauthClients ?? new Dictionary<string, OAuthClient>());
        Delay = new FakeDelay(Clock);
        Previews = new PreviewService(Uow, Registry, Factory, Tokens, Clock, Delay, Log, options, jitter: () => 1.0);
        Bus = new CommandBus(Uow, Clock, Guid.CreateVersion7());
        Uow.RunAsync(async s => { await s.Workspaces.UpsertAsync(Workspace); return 0; }).GetAwaiter().GetResult();
    }

    public async Task<AppConnection> ConnectAsync(string providerId, string? instanceUrl = null, string token = "tok", AuthMethod method = AuthMethod.PersonalToken)
    {
        var connection = new AppConnection
        {
            Id = Guid.CreateVersion7(), ProviderId = providerId, AuthMethod = method, DisplayLabel = "me",
            InstanceUrl = instanceUrl, ConnectedAt = Clock.UtcNow, Status = ConnectionStatus.Active,
        };
        await Credentials.SaveAsync(connection.Id, new Credential(token));
        await Uow.RunAsync(async s => { await s.Connections.UpsertAsync(connection); return 0; });
        return connection;
    }

    public async Task<Guid> ItemWithLinkAsync(string url, string title = "Task")
    {
        var id = Guid.CreateVersion7();
        await Bus.SendAsync(new CreateItem(id, Workspace.Id, title, new DateOnly(2026, 10, 7)));
        await Uow.RunAsync(async s => { await s.Links.AddExplicitAsync(id, LinkUrl.Normalize(url)!, Clock.UtcNow); return 0; });
        return id;
    }

    public void Dispose() => Uow.Dispose();
}

// Provider whose fetch results are scripted per call, for service-level behavior.
public sealed class ScriptedProvider(string id = "fake", bool instanceBased = false) : ProviderBase
{
    public List<IReadOnlyList<Uri>> Calls { get; } = [];
    public Func<IReadOnlyList<Uri>, IReadOnlyList<LinkPreview>>? Script { get; set; }
    public Exception? Throw { get; set; }
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);

    public override string ProviderId => id;
    public override string DisplayName => id;
    public override bool IsInstanceBased => instanceBased;
    public override IReadOnlyList<UrlPattern> UrlPatterns { get; } = [new($"{id}.test", "/*")];
    public override IReadOnlyList<AuthMethod> SupportedAuthMethods => [AuthMethod.PersonalToken];
    public AuthConfig Config { get; set; } = new(null, null, [], "");
    public override AuthConfig GetAuthConfig() => Config;
    public override Uri ApiRoot(string? instanceUrl) => new("https://api.test/");
    public override TimeSpan CacheTtl(Uri url) => Ttl;
    public override Task<ConnectionIdentity> ValidateAsync(IProviderHttp http, CancellationToken ct) => Task.FromResult(new ConnectionIdentity("me"));
    public override Task<LinkPreview> FetchAsync(Uri url, IProviderHttp http, CancellationToken ct) => throw new NotSupportedException();

    public override Task<IReadOnlyList<LinkPreview>> FetchBatchAsync(IReadOnlyList<Uri> urls, IProviderHttp http, CancellationToken ct)
    {
        Calls.Add(urls);
        if (Throw is not null) throw Throw;
        return Task.FromResult(Script?.Invoke(urls) ?? urls.Select(u => Preview(u, LinkState.Open, "h1")).ToList());
    }

    public LinkPreview Preview(Uri url, LinkState state, string hash, string title = "T", string kind = "issue") => new()
    {
        Url = LinkUrl.Normalize(url.ToString())!, ProviderId = id, Title = title, State = state, StateHash = hash,
        Status = PreviewStatus.Loaded, ChipFacts = [new("fact")],
        Metadata = kind is null ? [] : new() { ["kind"] = System.Text.Json.JsonDocument.Parse($"\"{kind}\"").RootElement.Clone() },
    };
}

public sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> reply) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        lock (Calls) Calls.Add((request, body));
        return reply(request, body);
    }

    public static HttpResponseMessage Json(string json, int status = 200) =>
        new((System.Net.HttpStatusCode)status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
