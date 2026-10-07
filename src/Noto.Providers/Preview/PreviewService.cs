using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Core.Time;
using Noto.Providers.Auth;
using Noto.Providers.Providers;
using Noto.Providers.Transport;

namespace Noto.Providers.Preview;

// Cache-first previews with a per-(provider, connection) rate gate, visible-first ordering and 429 backoff.
public sealed class PreviewService(
    IUnitOfWork uow, ProviderRegistry registry, IProviderHttpFactory transports, TokenManager tokens,
    IClock clock, IDelay delay, ILogger<PreviewService> logger, PreviewOptions? options = null, Func<double>? jitter = null)
{
    readonly PreviewOptions _options = options ?? new();
    readonly Func<double> _jitter = jitter ?? Random.Shared.NextDouble;
    readonly ConcurrentDictionary<string, DateTimeOffset> _lastCall = [];
    readonly ConcurrentDictionary<string, (DateTimeOffset Until, int Failures)> _backoff = [];

    // Never touches the network: what the chip can render right now.
    public Task<IReadOnlyDictionary<string, LinkPreview>> GetCachedAsync(IReadOnlyCollection<string> urls) =>
        uow.RunAsync(s => s.Previews.GetManyAsync(urls));

    public Task MarkViewedAsync(string url) => uow.RunAsync(async s => { await s.Previews.MarkViewedAsync(url, clock.UtcNow); return 0; });

    public async Task<RefreshResult> RefreshAsync(IReadOnlyList<PreviewRequest> requests, CancellationToken ct = default)
    {
        var urls = requests.Select(r => r.Url).Distinct().ToList();
        var (cache, connections) = await uow.RunAsync(async s => (await s.Previews.GetManyAsync(urls), await s.Connections.ListAsync()));
        var result = new Dictionary<string, LinkPreview>(cache);
        var changes = new List<LinkChange>();

        var work = new List<(PreviewRequest Req, Uri Uri, ProviderMatch Match)>();
        foreach (var req in requests.DistinctBy(r => r.Url))
        {
            if (!req.Force && cache.TryGetValue(req.Url, out var cached) && IsFresh(cached)) continue;
            if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri)) continue;
            // No connection → plain link; only the OpenGraph fallback needs none.
            if (registry.Match(uri, connections) is { } match && (match.Connection is not null || match.Provider.ProviderId == ProviderRegistry.FallbackProviderId))
                work.Add((req, uri, match));
        }

        // Visible first, then Waiting/Today, then the rest; groups keep the best priority of their members.
        var groups = work.GroupBy(w => (w.Match.Provider.ProviderId, w.Match.Connection?.Id))
            .OrderBy(g => g.Min(w => (int)w.Req.Priority)).ToList();

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var items = group.OrderBy(w => w.Req.Priority).ToList();
            var key = $"{group.Key.ProviderId}:{group.Key.Id}";
            var fetched = await FetchGroupAsync(key, items[0].Match, items.Select(i => i.Uri).ToList(), cache, ct);

            foreach (var (item, preview) in items.Zip(fetched))
            {
                cache.TryGetValue(item.Req.Url, out var previous);
                var stored = await StoreAsync(preview, previous, items[0].Match.Connection?.Id);
                result[item.Req.Url] = stored;
                if (previous?.StateHash is not null && stored.Status == PreviewStatus.Loaded && stored.StateHash is not null && stored.StateHash != previous.StateHash)
                    changes.Add(new LinkChange(stored.Url, previous.State, stored.State, previous.StateHash, stored.StateHash, stored));
            }
        }
        return new RefreshResult(result, changes);
    }

    bool IsFresh(LinkPreview p) => p.Status == PreviewStatus.Loaded && p.ExpiresAt is { } at && at > clock.UtcNow;

    // One entry per input URL, in order. Failures become status-carrying previews, never exceptions.
    async Task<IReadOnlyList<LinkPreview>> FetchGroupAsync(
        string key, ProviderMatch match, IReadOnlyList<Uri> uris, IReadOnlyDictionary<string, LinkPreview> cache, CancellationToken ct)
    {
        if (_backoff.TryGetValue(key, out var b) && b.Until > clock.UtcNow)
            return uris.Select(u => Degraded(u, match, cache, PreviewStatus.Stale, "Rate limited; showing cached data")).ToList();

        await WaitForTurnAsync(key, ct);
        try
        {
            var http = transports.Create(match.Provider, match.Connection?.InstanceUrl, match.Connection?.AuthMethod ?? AuthMethod.PersonalToken,
                match.Connection is null ? new StaticCredentialSource(null) : tokens.For(match.Connection));
            var previews = await match.Provider.FetchBatchAsync(uris, http, ct);
            _backoff.TryRemove(key, out _);
            await TouchConnectionAsync(match.Connection);
            return previews;
        }
        catch (ProviderHttpException e) when (e.Status == 429)
        {
            var failures = _backoff.TryGetValue(key, out var prev) ? prev.Failures + 1 : 1;
            var wait = e.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(_options.BackoffMax.TotalSeconds,
                _options.BackoffBase.TotalSeconds * Math.Pow(2, failures - 1) * (0.5 + _jitter() * 0.5)));
            _backoff[key] = (clock.UtcNow + wait, failures);
            logger.LogWarning("{Provider} rate limited; backing off {Seconds:F0}s", match.Provider.ProviderId, wait.TotalSeconds);
            return uris.Select(u => Degraded(u, match, cache, PreviewStatus.Stale, "Rate limited; showing cached data")).ToList();
        }
        catch (ProviderHttpException e) when (e.Status is 401 or 403)
        {
            await SetConnectionStatusAsync(match.Connection, ConnectionStatus.Expired);
            return uris.Select(u => Degraded(u, match, cache, PreviewStatus.AuthRequired, "Reconnect to see live status")).ToList();
        }
        catch (ProviderHttpException e) when (e.Status is 404 or 410)
        {
            return uris.Select(u => Degraded(u, match, cache, PreviewStatus.Unavailable, "No longer accessible")).ToList();
        }
        catch (AuthRequiredException e)
        {
            return uris.Select(u => Degraded(u, match, cache, PreviewStatus.AuthRequired, e.Message)).ToList();
        }
        catch (Exception e) when (e is ProviderHttpException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Type only: messages can embed URLs with credentials.
            logger.LogWarning("{Provider} fetch failed: {Kind}", match.Provider.ProviderId, e.GetType().Name);
            return uris.Select(u => Degraded(u, match, cache, PreviewStatus.Error, "Couldn't refresh")).ToList();
        }
    }

    async Task WaitForTurnAsync(string key, CancellationToken ct)
    {
        if (_lastCall.TryGetValue(key, out var last) && last + _options.MinInterval - clock.UtcNow is { } wait && wait > TimeSpan.Zero)
            await delay.DelayAsync(wait, ct);
        _lastCall[key] = clock.UtcNow;
    }

    // Keeps whatever the cache knew, so a failed refresh still renders facts.
    LinkPreview Degraded(Uri uri, ProviderMatch match, IReadOnlyDictionary<string, LinkPreview> cache, PreviewStatus status, string message)
    {
        var normalized = LinkUrl.Normalize(uri.ToString()) ?? uri.ToString();
        var basis = cache.GetValueOrDefault(normalized);
        return new LinkPreview
        {
            Url = normalized,
            ProviderId = match.Provider.ProviderId,
            ConnectionId = match.Connection?.Id,
            Title = basis?.Title ?? uri.IdnHost,
            Subtitle = basis?.Subtitle,
            ChipFacts = status == PreviewStatus.Unavailable ? [] : basis?.ChipFacts ?? [],
            Snippet = basis?.Snippet,
            AuthorName = basis?.AuthorName,
            State = basis?.State,
            StateHash = basis?.StateHash,
            Metadata = basis?.Metadata ?? [],
            FetchedAt = basis?.FetchedAt ?? clock.UtcNow,
            Status = status,
            ErrorMessage = message,
        };
    }

    async Task<LinkPreview> StoreAsync(LinkPreview preview, LinkPreview? previous, Guid? connectionId)
    {
        preview.ConnectionId ??= connectionId;
        if (preview.Status == PreviewStatus.Loaded)
        {
            preview.FetchedAt = clock.UtcNow;
            var ttl = Uri.TryCreate(preview.Url, UriKind.Absolute, out var u) ? registry.Get(preview.ProviderId)?.CacheTtl(u) : null;
            preview.ExpiresAt = clock.UtcNow + (ttl ?? TimeSpan.FromMinutes(10));
        }

        // A brand-new link starts "seen"; the change dot only appears for later movement.
        preview.ViewedStateHash = previous?.ViewedStateHash ?? preview.StateHash;
        preview.UserLastViewedAt = previous?.UserLastViewedAt;
        await uow.RunAsync(async s => { await s.Previews.PutAsync(preview); return 0; });
        return preview;
    }

    Task TouchConnectionAsync(AppConnection? connection) => connection is null ? Task.CompletedTask : uow.RunAsync(async s =>
    {
        if (await s.Connections.GetAsync(connection.Id) is { } c)
        {
            c.LastUsedAt = clock.UtcNow;
            await s.Connections.UpsertAsync(c);
        }
        return 0;
    });

    Task SetConnectionStatusAsync(AppConnection? connection, ConnectionStatus status) => connection is null ? Task.CompletedTask : uow.RunAsync(async s =>
    {
        if (await s.Connections.GetAsync(connection.Id) is { } c)
        {
            c.Status = status;
            await s.Connections.UpsertAsync(c);
        }
        return 0;
    });
}
