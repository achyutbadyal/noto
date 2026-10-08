using CoreKeyring = Noto.Core.Interfaces.IKeyring;
using PlatformKeyring = Noto.Platform.Abstractions.IKeyring;

namespace Noto.Desktop;

// Providers code against Core's IKeyring; the platform layer exposes its own, identical-shaped one.
sealed class KeyringAdapter(PlatformKeyring inner) : CoreKeyring
{
    public Task<string?> GetAsync(string service, string account) =>
        inner.GetAsync(service, account);

    public Task SetAsync(string service, string account, string secret) =>
        inner.SetAsync(service, account, secret);

    public Task DeleteAsync(string service, string account) => inner.DeleteAsync(service, account);
}
