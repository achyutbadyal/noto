using Noto.Core.Links;

namespace Noto.Providers;

public sealed record ProviderMatch(IAppProvider Provider, AppConnection? Connection, string? Hint);

// Picks (provider, connection) for a URL. The host chooses the connection when several exist.
public sealed class ProviderRegistry(IEnumerable<IAppProvider> providers)
{
    public const string FallbackProviderId = "opengraph";

    readonly List<IAppProvider> _providers = providers.ToList();

    public IReadOnlyList<IAppProvider> Providers => _providers;
    public IAppProvider? Get(string providerId) => _providers.FirstOrDefault(p => p.ProviderId == providerId);

    public void Register(IAppProvider provider)
    {
        _providers.RemoveAll(p => p.ProviderId == provider.ProviderId);
        _providers.Add(provider);
    }

    public ProviderMatch? Match(Uri url, IReadOnlyList<AppConnection> connections)
    {
        var specific = _providers.Where(p => p.ProviderId != FallbackProviderId).ToList();

        foreach (var connection in connections)
            if (Get(connection.ProviderId) is { } provider && provider.CanHandle(url, connection))
                return new ProviderMatch(provider, connection, null);

        // No usable connection: still recognized, so the UI can say what to connect.
        foreach (var provider in specific.Where(p => !p.IsInstanceBased))
            if (provider.CanHandle(url, null))
                return new ProviderMatch(provider, null, $"Connect {provider.DisplayName} for live status");

        foreach (var provider in specific)
            if (provider.LooksLikeOwn(url))
                return new ProviderMatch(provider, null, $"Connect {provider.DisplayName} ({url.IdnHost})");

        return Get(FallbackProviderId) is { } og ? new ProviderMatch(og, null, null) : null;
    }
}
