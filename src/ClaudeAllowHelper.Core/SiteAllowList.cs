using System.Net;
using System.Net.Sockets;

namespace ClaudeAllowHelper.Core;

public static class SiteAllowList
{
    public static bool IsAllowed(string? hostOrOrigin, IEnumerable<string>? allowedSites)
    {
        if (string.IsNullOrWhiteSpace(hostOrOrigin) || allowedSites is null)
        {
            return false;
        }

        if (!TryParseHost(hostOrOrigin, out var candidateHost, out var candidatePort))
        {
            return false;
        }

        foreach (var entry in allowedSites)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            if (!TryParseHost(entry, out var allowedHost, out var allowedPort))
            {
                continue;
            }

            if (!HostMatches(candidateHost, allowedHost))
            {
                continue;
            }

            if (allowedPort is null || allowedPort == candidatePort)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryParseHost(string value, out string host, out int? port)
    {
        host = "";
        port = null;
        var raw = value.Trim().ToLowerInvariant();
        if (raw.Length == 0)
        {
            return false;
        }

        raw = raw.TrimEnd('/');

        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            if (raw.StartsWith("//", StringComparison.Ordinal))
            {
                raw = "http:" + raw;
            }
            else if (raw.Contains('/') || raw.Contains('\\'))
            {
                raw = "http://" + raw.TrimStart('/', '\\');
            }
        }

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            host = uri.IdnHost;
            port = uri.IsDefaultPort ? null : uri.Port;
            return host.Length > 0;
        }

        var pathIndex = raw.IndexOf('/');
        if (pathIndex >= 0)
        {
            raw = raw[..pathIndex];
        }

        if (raw.StartsWith('[') && raw.Contains(']'))
        {
            var end = raw.IndexOf(']');
            host = raw[1..end];
            if (end + 1 < raw.Length && raw[end + 1] == ':')
            {
                if (int.TryParse(raw[(end + 2)..], out var ipv6Port))
                {
                    port = ipv6Port;
                }
            }

            return host.Length > 0;
        }

        var lastColon = raw.LastIndexOf(':');
        if (lastColon > 0 && lastColon < raw.Length - 1 && raw.IndexOf(':') == lastColon)
        {
            if (int.TryParse(raw[(lastColon + 1)..], out var parsedPort))
            {
                host = raw[..lastColon];
                port = parsedPort;
                return host.Length > 0;
            }
        }

        host = raw.Trim().TrimEnd('.');
        return host.Length > 0;
    }

    public static bool SameHost(string? left, string? right)
    {
        if (!TryParseHost(left ?? "", out var leftHost, out var leftPort) ||
            !TryParseHost(right ?? "", out var rightHost, out var rightPort))
        {
            return false;
        }

        if (!string.Equals(leftHost, rightHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return leftPort is null || rightPort is null || leftPort == rightPort;
    }

    public static bool LooksLikeLocalDevelopmentHost(string? hostOrOrigin)
    {
        if (!TryParseHost(hostOrOrigin ?? "", out var host, out _))
        {
            return false;
        }

        if (host is "localhost" or "127.0.0.1" or "::1" or "0:0:0:0:0:0:0:1")
        {
            return true;
        }

        if (host.EndsWith(".test", StringComparison.Ordinal) ||
            host.EndsWith(".localhost", StringComparison.Ordinal) ||
            host.EndsWith(".local", StringComparison.Ordinal) ||
            host.EndsWith(".internal", StringComparison.Ordinal))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            return IsPrivateOrLoopback(ip);
        }

        return false;
    }

    private static bool HostMatches(string candidateHost, string allowedHost)
    {
        if (string.Equals(candidateHost, allowedHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (allowedHost.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = allowedHost[1..]; // ".example.test"
            return candidateHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31);
        }

        return false;
    }
}
