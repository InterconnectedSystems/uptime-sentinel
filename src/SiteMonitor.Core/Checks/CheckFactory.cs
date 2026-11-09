using SiteMonitor.Core.Models;

namespace SiteMonitor.Core.Checks;

/// <summary>A resolved check: what to run, and the host/URL to run it against.</summary>
public sealed record ResolvedCheck(ICheck Check, string Endpoint);

public static class CheckFactory
{
    public static bool TryGetUri(string address, out Uri uri)
    {
        uri = null!;
        address = address.Trim();
        if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var u)) return false;
        uri = u;
        return true;
    }

    /// <summary>Strip scheme/path/port from whatever the user typed to get a bare host or IP.</summary>
    public static string ExtractHost(string address)
    {
        address = address.Trim();
        if (TryGetUri(address, out var uri)) return uri.Host;
        if (Uri.TryCreate("http://" + address, UriKind.Absolute, out var u2)) return u2.Host;
        return address;
    }

    /// <summary>What Auto mode decides for a given address.</summary>
    public static CheckType ResolveType(MonitorTarget t)
    {
        if (t.CheckType != CheckType.Auto) return t.CheckType;
        if (TryGetUri(t.Address, out _)) return CheckType.Http;
        if (t.Port is not null) return CheckType.Tcp;
        return CheckType.Ping; // with TCP fallback, see Create()
    }

    public static ResolvedCheck Create(MonitorTarget t)
    {
        var address = t.Address.Trim();
        switch (t.CheckType)
        {
            case CheckType.Http:
                var url = TryGetUri(address, out var u) ? u.ToString() : "http://" + address;
                return new(new HttpCheck(t.ExpectedStatus, t.Keyword), url);

            case CheckType.Tcp:
            {
                int port = t.Port ?? (TryGetUri(address, out var tu) ? tu.Port : 443);
                return new(new TcpCheck(port), ExtractHost(address));
            }

            case CheckType.Ping:
                return new(new PingCheck(), ExtractHost(address));

            case CheckType.Auto:
            default:
                if (TryGetUri(address, out var au))
                    return new(new HttpCheck(t.ExpectedStatus, t.Keyword), au.ToString());
                if (t.Port is { } p)
                    return new(new TcpCheck(p), ExtractHost(address));
                // Many servers/firewalls block ICMP, so fall back to common TCP ports before declaring failure.
                return new(new FallbackCheck(new PingCheck(), new TcpCheck(443), new TcpCheck(80)), ExtractHost(address));
        }
    }
}
