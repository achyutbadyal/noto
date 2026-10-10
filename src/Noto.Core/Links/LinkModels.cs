using System.Text.Json;
using System.Text.Json.Serialization;

namespace Noto.Core.Links;

// Normalized across providers so live-link rules are written once.
public enum LinkState
{
    Unknown,
    Open,
    InProgress,
    InReview,
    Blocked,
    Done,
    Closed,
}

public enum PreviewStatus
{
    Loading,
    Loaded,
    Stale,
    Error,
    AuthRequired,
    Unavailable,
}

public enum AuthMethod
{
    OAuth2,
    PersonalToken,
    ApiKey,
    BasicAuth,
}

public enum ConnectionStatus
{
    Active,
    Expired,
    RefreshFailed,
    Revoked,
}

public sealed record ChipFact(string Text, string? Glyph = null);

public sealed record TodoLink(
    Guid Id,
    Guid ItemId,
    string Url,
    int Position,
    DateTimeOffset CreatedAt,
    string Source
);

// Local-only cache row (docs/04 §6). Never synced: it holds private third-party content.
public sealed class LinkPreview
{
    public string Url { get; init; } = "";
    public string ProviderId { get; init; } = "";
    public Guid? ConnectionId { get; set; }
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public IReadOnlyList<ChipFact> ChipFacts { get; set; } = [];
    public string? Snippet { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorAvatarUrl { get; set; }
    public LinkState? State { get; set; }
    public string? StateHash { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public PreviewStatus Status { get; set; }
    public string? ErrorMessage { get; set; }

    public string? ViewedStateHash { get; set; }
    public DateTimeOffset? UserLastViewedAt { get; set; }

    // Change dot: something actionable moved since the user last looked.
    [JsonIgnore]
    public bool HasChange => StateHash is not null && StateHash != ViewedStateHash;
}

// What a URL turned out to be, resolved before anything else reads the text around it (docs/04 §6).
// Deliberately only the parts a model can use — identity, state and a short summary — never the page.
//
// `Unreadable` is set when the link exists but could not be read (offline, no connection, gone). It is
// carried rather than dropped so a caller can say "there is a link here, I just couldn't read it"
// instead of silently pretending the text had no link in it.
public sealed record LinkContext(
    string Url,
    string? Title,
    string? Provider,
    string? Summary,
    string? State,
    IReadOnlyList<string> Facts,
    string? Unreadable
);

// Resolves a URL so its meaning can be handed to something else (the AI suggester). Cache first, then a
// fetch: unauthenticated OpenGraph for any host, or the connected app when the user has one for it.
public interface ILinkResolver
{
    Task<LinkContext?> ResolveAsync(string url, CancellationToken ct = default);
}

// Metadata only. Secrets live in the OS keyring under service `app.noto.connections`.
public sealed class AppConnection
{
    public Guid Id { get; init; }
    public string ProviderId { get; init; } = "";
    public AuthMethod AuthMethod { get; init; }
    public string DisplayLabel { get; set; } = "";
    public string? InstanceUrl { get; set; }

    // Set when the provider's API is not at its default root for this site (Atlassian's cloud gateway).
    public string? ApiBaseUrl { get; set; }
    public string[] Scopes { get; set; } = [];
    public DateTimeOffset ConnectedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public ConnectionStatus Status { get; set; }
}

[JsonSerializable(typeof(LinkPreview))]
[JsonSerializable(typeof(JsonElement))]
public sealed partial class LinkJson : JsonSerializerContext;
