using System.Text.Json;
using System.Text.Json.Serialization;
using Noto.Core.Links;
using Noto.Providers.Providers;

namespace Noto.Providers.CustomApp;

public enum CustomAuthKind
{
    None,
    Bearer,
    ApiKeyHeader,
    ApiKeyQuery,
    Basic,
}

public enum CustomPreviewMode
{
    OpenGraph,
    JsonApi,
}

// A declarative app: no code, shareable as JSON. Secrets are never part of the definition.
public sealed record CustomAppDefinition(
    Guid Id,
    string Name,
    string UrlGlob,
    CustomAuthKind Auth = CustomAuthKind.None,
    string? AuthName = null,
    CustomPreviewMode Preview = CustomPreviewMode.OpenGraph,
    string? EndpointTemplate = null,
    string? TitlePath = null,
    string? SnippetPath = null,
    string? StatePath = null,
    string? DoneWhen = null
)
{
    public string ToJson() =>
        JsonSerializer.Serialize(this, CustomAppJson.Default.CustomAppDefinition);

    public static CustomAppDefinition FromJson(string json) =>
        JsonSerializer.Deserialize(json, CustomAppJson.Default.CustomAppDefinition)
        ?? throw new JsonException("Empty definition");
}

[JsonSerializable(typeof(CustomAppDefinition))]
[JsonSourceGenerationOptions(UseStringEnumConverter = true, WriteIndented = true)]
public sealed partial class CustomAppJson : JsonSerializerContext;

public sealed class CustomAppProvider : ProviderBase
{
    readonly CustomAppDefinition _def;
    readonly OpenGraphProvider _og = new();

    public CustomAppProvider(CustomAppDefinition def)
    {
        _def = def;
        var slash = def.UrlGlob.IndexOf('/');
        UrlPatterns =
        [
            slash < 0 ? new(def.UrlGlob, "/*") : new(def.UrlGlob[..slash], def.UrlGlob[slash..]),
        ];
    }

    public override string ProviderId => $"custom:{_def.Id:N}";
    public override string DisplayName => _def.Name;
    public override IReadOnlyList<UrlPattern> UrlPatterns { get; }

    public override AuthConfig GetAuthConfig() => new(null, null, [], "");

    public override IReadOnlyList<AuthMethod> SupportedAuthMethods =>
        _def.Auth switch
        {
            CustomAuthKind.None => [],
            CustomAuthKind.Bearer => [AuthMethod.PersonalToken],
            CustomAuthKind.Basic => [AuthMethod.BasicAuth],
            _ => [AuthMethod.ApiKey],
        };

    public override Uri ApiRoot(string? instanceUrl) =>
        new(
            Uri.TryCreate(
                _def.EndpointTemplate?.Replace("{path}", "x").Replace("{id}", "x"),
                UriKind.Absolute,
                out var u
            )
                ? $"{u.Scheme}://{u.Authority}/"
                : "https://invalid.example/"
        );

    public override TimeSpan CacheTtl(Uri url) =>
        _def.Preview == CustomPreviewMode.OpenGraph
            ? TimeSpan.FromHours(24)
            : TimeSpan.FromMinutes(30);

    public override IEnumerable<KeyValuePair<string, string>> AuthHeaders(
        Credential c,
        AuthMethod method
    ) =>
        _def.Auth switch
        {
            CustomAuthKind.Bearer => [Bearer(c)],
            CustomAuthKind.ApiKeyHeader => [new(_def.AuthName ?? "X-Api-Key", c.AccessToken)],
            CustomAuthKind.Basic => [Basic(c)],
            _ => [],
        };

    public override IEnumerable<KeyValuePair<string, string>> AuthQuery(
        Credential c,
        AuthMethod method
    ) =>
        _def.Auth == CustomAuthKind.ApiKeyQuery
            ? [new(_def.AuthName ?? "api_key", c.AccessToken)]
            : [];

    public override Task<ConnectionIdentity> ValidateAsync(
        IProviderHttp http,
        CancellationToken ct
    ) => Task.FromResult(new ConnectionIdentity(_def.Name));

    public override async Task<LinkPreview> FetchAsync(
        Uri url,
        IProviderHttp http,
        CancellationToken ct
    )
    {
        if (_def.Preview == CustomPreviewMode.OpenGraph || _def.EndpointTemplate is null)
        {
            var og = await _og.FetchAsync(url, http, ct);
            return Retag(og);
        }

        UrlPatterns[0].TryMatch(url, out var captures);
        var endpoint = captures.Aggregate(
            _def.EndpointTemplate,
            (t, c) => t.Replace($"{{{c.Key}}}", Uri.EscapeDataString(c.Value))
        );

        using var doc = await http.GetJsonAsync(ProviderRequest.Get(endpoint), ct);
        var root = doc.RootElement;
        var stateText = JsonPathSelector.SelectString(root, _def.StatePath);

        // A state mapping turns the app into a live link; without one there is no state or hash.
        LinkState? state =
            stateText is null ? null
            : _def.DoneWhen is { } done
            && string.Equals(stateText, done, StringComparison.OrdinalIgnoreCase)
                ? LinkState.Done
            : LinkState.Open;

        var p = New(
            url,
            JsonPathSelector.SelectString(root, _def.TitlePath) ?? url.IdnHost,
            state,
            stateText is null ? [] : [stateText]
        );
        p.Snippet = JsonPathSelector.SelectString(root, _def.SnippetPath);
        p.Subtitle = _def.Name;
        p.ChipFacts = new List<ChipFact> { new(_def.Name) }
            .Concat(stateText is null ? [] : [new ChipFact(stateText)])
            .ToList();
        Meta(p, "kind", "custom");
        return p;
    }

    LinkPreview Retag(LinkPreview og) =>
        new()
        {
            Url = og.Url,
            ProviderId = ProviderId,
            Title = og.Title,
            Snippet = og.Snippet,
            Subtitle = _def.Name,
            ChipFacts = [new(_def.Name)],
            Metadata = og.Metadata,
            Status = og.Status,
        };
}
