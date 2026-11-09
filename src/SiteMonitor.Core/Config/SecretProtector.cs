using System.Security.Cryptography;
using System.Text;

namespace SiteMonitor.Core.Config;

/// <summary>
/// Encrypts the SMTP password with Windows DPAPI (LocalMachine scope), so the GUI (admin user)
/// and the service (LocalSystem) on the same machine can both read it, but the config file is
/// useless if copied to another machine. Non-Windows (tests only) uses a plain base64 marker.
/// </summary>
public static class SecretProtector
{
    private const string DpapiPrefix = "dpapi:";
    private const string PlainPrefix = "b64:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SiteMonitor.Smtp.v1");

    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows())
            return DpapiPrefix + Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.LocalMachine));
        return PlainPrefix + Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI secrets can only be read on Windows.");
            var data = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(data, Entropy, DataProtectionScope.LocalMachine));
        }
        if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
            return Encoding.UTF8.GetString(Convert.FromBase64String(stored[PlainPrefix.Length..]));
        return stored; // hand-edited plain-text password in config.json
    }
}
