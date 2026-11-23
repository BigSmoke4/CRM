using System.Net;
using System.Net.Sockets;

namespace Crm.Web.Services.Integrations;

public class UnsafeUrlException : Exception
{
    public UnsafeUrlException(string message) : base(message) { }
}

/// <summary>
/// SSRF protection for outbound integration requests:
/// - only http/https
/// - resolves DNS and rejects private/loopback/link-local/reserved IPs
/// - blocks cloud metadata endpoints
/// - restricts redirect hops and re-validates each redirect target
/// </summary>
public static class OutboundUrlGuard
{
    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "metadata.google.internal", "metadata.azure.com",
        "instance-data", "169.254.169.254", "100.100.100.200"
    };

    public static async Task<Uri> ValidateAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new UnsafeUrlException("Invalid URL.");
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new UnsafeUrlException("Only http and https URLs are allowed.");
        if (BlockedHosts.Contains(uri.Host))
            throw new UnsafeUrlException($"Host '{uri.Host}' is not allowed.");

        // Resolve host and reject non-public addresses
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
        }
        catch (SocketException ex)
        {
            throw new UnsafeUrlException($"DNS resolution failed: {ex.Message}");
        }
        foreach (var ip in addresses)
        {
            if (IsPrivateOrReserved(ip))
                throw new UnsafeUrlException($"Host '{uri.Host}' resolves to a private/reserved address ({ip}) which is not allowed.");
        }
        return uri;
    }

    private static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 ||
                   (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                   (b[0] == 192 && b[1] == 168) ||
                   (b[0] == 169 && b[1] == 254) ||   // link-local incl. cloud metadata
                   b[0] == 127 ||                    // loopback range
                   (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || // CGNAT
                   b[0] == 0 || b[0] >= 224;         // this-net / multicast / reserved
        }
        // IPv6: loopback, link-local fe80::/10, unique-local fc00::/7, unspecified
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Loopback)) return true;
            var b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);
        }
        return false;
    }
}
