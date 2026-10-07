using System.Net;
using System.Net.Sockets;

namespace Noto.Server.Gateway;

// Decides whether an address is safe to connect to on behalf of a user: public unicast only.
public static class SsrfGuard
{
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(In(b, [0], 8)                    // "this" network
                  || In(b, [10], 8)                   // private
                  || In(b, [100, 64], 10)             // carrier-grade NAT
                  || In(b, [127], 8)                  // loopback
                  || In(b, [169, 254], 16)            // link-local (cloud metadata)
                  || In(b, [172, 16], 12)             // private
                  || In(b, [192, 0, 0], 24)           // IETF protocol assignments
                  || In(b, [192, 0, 2], 24)           // documentation
                  || In(b, [192, 88, 99], 24)         // 6to4 relay
                  || In(b, [192, 168], 16)            // private
                  || In(b, [198, 18], 15)             // benchmarking
                  || In(b, [198, 51, 100], 24)        // documentation
                  || In(b, [203, 0, 113], 24)         // documentation
                  || In(b, [224], 4)                  // multicast
                  || In(b, [240], 4));                // reserved + broadcast
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any)) return false;
            if (In(b, [0xfc], 7) || In(b, [0xfe, 0x80], 10) || In(b, [0xfe, 0xc0], 10)) return false; // ULA, link-local, site-local
            if (In(b, [0xff], 8)) return false;                                                        // multicast
            if (In(b, [0x20, 0x01, 0x0d, 0xb8], 32)) return false;                                     // documentation
            if (In(b, [0x01, 0x00, 0, 0, 0, 0, 0, 0], 64)) return false;                              // discard-only
            // Transition mechanisms embed an IPv4 address that must itself be public.
            if (In(b, [0x00, 0x64, 0xff, 0x9b], 96)) return IsPublic(new IPAddress(b[12..16]));        // NAT64
            if (In(b, [0x20, 0x02], 16)) return IsPublic(new IPAddress(b[2..6]));                      // 6to4
            return true;
        }
        return false;
    }

    static bool In(byte[] address, byte[] prefix, int bits)
    {
        var fullBytes = bits / 8;
        for (var i = 0; i < fullBytes; i++)
            if (i >= prefix.Length ? address[i] != 0 : address[i] != prefix[i]) return false;
        var rem = bits % 8;
        if (rem == 0) return true;
        var mask = (byte)(0xFF << (8 - rem));
        var p = fullBytes < prefix.Length ? prefix[fullBytes] : (byte)0;
        return (address[fullBytes] & mask) == (p & mask);
    }
}

public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}

public sealed class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
        IPAddress.TryParse(host, out var literal) ? Task.FromResult(new[] { literal }) : Dns.GetHostAddressesAsync(host, ct);
}
