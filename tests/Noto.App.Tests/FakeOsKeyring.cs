using Noto.Platform.Abstractions;

namespace Noto.App.Tests;

// A stand-in for the OS keychain that reports itself as *supported*, so the keyring sweep has something to
// do. The real one is the login keychain, which a test must never touch.
public sealed class FakeOsKeyring : IKeyring
{
    readonly Dictionary<(string, string), string> _secrets = [];

    public Capability Capability => Capability.Supported;

    public bool Holds(string service, string account) => _secrets.ContainsKey((service, account));

    public Task SetAsync(string service, string account, string secret)
    {
        _secrets[(service, account)] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string service, string account) =>
        Task.FromResult(_secrets.TryGetValue((service, account), out var secret) ? secret : null);

    public Task DeleteAsync(string service, string account)
    {
        _secrets.Remove((service, account));
        return Task.CompletedTask;
    }
}
