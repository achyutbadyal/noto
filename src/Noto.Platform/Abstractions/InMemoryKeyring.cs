using System.Collections.Concurrent;

namespace Noto.Platform.Abstractions;

// Non-persistent keyring for tests and platforms without a secure store; secrets die with the process.
public sealed class InMemoryKeyring : IKeyring
{
    readonly ConcurrentDictionary<(string, string), string> _secrets = new();

    public Capability Capability => Capability.Unsupported("No OS keyring on this platform: secrets are kept in memory only.");

    public Task SetAsync(string service, string account, string secret)
    {
        _secrets[(service, account)] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string service, string account) =>
        Task.FromResult(_secrets.TryGetValue((service, account), out var s) ? s : null);

    public Task DeleteAsync(string service, string account)
    {
        _secrets.TryRemove((service, account), out _);
        return Task.CompletedTask;
    }
}
