using System.Net;
using System.Net.Sockets;

namespace Noto.Server.Gateway;

// Decides whether an address is safe to connect to on behalf of a user: public unicast only.
public static class SsrfGuard
{
    public static bool IsPublic(IPAddress ip) => Noto.Core.Net.PublicAddress.IsPublic(ip);
}

public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}

public sealed class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
        IPAddress.TryParse(host, out var literal)
            ? Task.FromResult(new[] { literal })
            : Dns.GetHostAddressesAsync(host, ct);
}
